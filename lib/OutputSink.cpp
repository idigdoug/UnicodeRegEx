#include "pch.h"
#include "OutputSink.h"

#include <TextEncoding.h>

HRESULT
WriteAllBytesToStream(
    _In_ ISequentialStream* pStream,
    std::span<BYTE const> data) noexcept
{
    ULONG constexpr BytesPerWriteMax = 1 << 20; // 1 MiB
    auto const* bytes = data.data();
    auto bytesRemaining = data.size();

    while (bytesRemaining != 0)
    {
        ULONG const bytesToWrite = bytesRemaining > BytesPerWriteMax
            ? BytesPerWriteMax
            : static_cast<ULONG>(bytesRemaining);
        ULONG bytesWritten = 0;
        HRESULT const hr = pStream->Write(bytes, bytesToWrite, &bytesWritten);
        if (FAILED(hr))
        {
            return hr;
        }

        if (bytesWritten == 0)
        {
            // Defensive: stream reported success but consumed no bytes; treat
            // as failure to avoid an infinite loop.
            return E_FAIL;
        }

        bytes += bytesWritten;
        bytesRemaining -= bytesWritten;
    }

    return S_OK;
}

HRESULT
AllocBStrFromChars(
    std::span<char16_t const> chars,
    _Out_ BSTR* pResult) noexcept
{
    static_assert(sizeof(char16_t) == sizeof(OLECHAR), "OLECHAR must be UTF-16");

    *pResult = nullptr;

    size_t const charCount = chars.size();
    if (charCount > UINT_MAX)
    {
        // A BSTR length is a UINT; this buffer can't be represented as a BSTR.
        return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
    }

    *pResult = SysAllocStringLen(
        reinterpret_cast<OLECHAR const*>(chars.data()),
        static_cast<UINT>(charCount));
    return *pResult ? S_OK : E_OUTOFMEMORY;
}

HRESULT
AllocBStrFromUtf16Bytes(
    std::span<BYTE const> utf16Bytes,
    _Out_ BSTR* pResult) noexcept
{
    auto const byteCount = utf16Bytes.size();
    assert((byteCount & 1) == 0);

    return AllocBStrFromChars(
        std::span<char16_t const>(
            reinterpret_cast<char16_t const*>(utf16Bytes.data()),
            byteCount / sizeof(char16_t)),
        pResult);
}

OutputSink::OutputSink() noexcept
    : m_bufferPos(0)
    , m_outputEncoding()
    , m_pStream(nullptr)
    , m_vector()
{
}

void
OutputSink::ResetToVector(TextEncoding outputEncoding)
{
    m_bufferPos = 0;
    m_outputEncoding = outputEncoding;
    m_pStream = nullptr;
    m_vector.clear();
}

void
OutputSink::ResetToStream(TextEncoding outputEncoding, _In_ ISequentialStream* pStream) noexcept
{
    assert(pStream != nullptr);
    m_bufferPos = 0;
    m_outputEncoding = outputEncoding;
    m_pStream = pStream;
    m_vector.clear();
}

void
OutputSink::push_back(char32_t codePoint)
{
    if (m_bufferPos >= BufferCapacity)
    {
        Flush();
        __analysis_assert(m_bufferPos == 0);
    }

    m_buffer[m_bufferPos] = codePoint;
    m_bufferPos += 1;
}

void
OutputSink::AppendBytes(
    std::span<BYTE const> inputBytes,
    TextEncoding inputEncoding)
{
    void const* const data = inputBytes.data();
    size_t const size = inputBytes.size();

    std::visit([&](auto encoding)
        {
            using EncodingT = decltype(encoding);
            using CharT = typename EncodingT::encoded_char;
            assert((size & (sizeof(CharT) - 1)) == 0);
            auto chars = std::span(static_cast<CharT const*>(data), size / sizeof(CharT));
            auto range = encoding.MakeCodePointRange(chars);
            for (auto it = range.begin; it != range.end; ++it)
            {
                push_back(*it);
            }
        },
        inputEncoding);
}

void
OutputSink::AppendRawBytes(std::span<BYTE const> bytes)
{
    auto const* const pBytes = bytes.data();
    auto const byteCount = bytes.size();
    if (byteCount == 0)
    {
        return;
    }

    // Preserve ordering: emit any buffered code points before the raw bytes.
    Flush();

    if (m_pStream == nullptr)
    {
        // Don't grow beyond what can be stored in a BSTR.
        if (sizeof(size_t) > sizeof(UINT) && m_vector.size() + byteCount > UINT_MAX)
        {
            THROW_WIN32(ERROR_ARITHMETIC_OVERFLOW);
        }

        m_vector.insert(m_vector.end(), pBytes, pBytes + byteCount);
    }
    else
    {
        THROW_IF_FAILED(WriteAllBytesToStream(m_pStream, bytes));
    }
}

TextEncoding
OutputSink::OutputEncoding() const noexcept
{
    return m_outputEncoding;
}

std::span<BYTE const>
OutputSink::FinishVector()
{
    assert(m_pStream == nullptr);

    std::span<BYTE const> result;
    if (m_vector.empty())
    {
        // Small-buffer optimization: transcode in-place and return a pointer
        // into the internal buffer. No vector allocation needed.
        auto const byteCount = TranscodeBufferInPlace();
        m_bufferPos = 0;
        result = std::span<BYTE const>(reinterpret_cast<BYTE const*>(m_buffer), byteCount);
    }
    else
    {
        // Flush remaining buffered code points into the vector and return
        // the vector contents.
        Flush();
        result = std::span<BYTE const>(m_vector.data(), m_vector.size());
    }

    return result;
}

void
OutputSink::FinishStream()
{
    assert(m_pStream != nullptr);

    Flush();
    m_pStream = nullptr;
}

size_t
OutputSink::TranscodeBufferInPlace()
{
    auto codePoints = std::span<char32_t>(m_buffer, m_bufferPos);

    return std::visit([&](auto encoding)
        {
            return encoding.ConvertInPlace(codePoints).size_bytes();
        },
        m_outputEncoding);
}

void
OutputSink::Flush()
{
    if (m_bufferPos == 0)
    {
        return;
    }

    auto const byteCount = TranscodeBufferInPlace();
    auto const* const pBytes = reinterpret_cast<BYTE const*>(m_buffer);
    m_bufferPos = 0;

    if (m_pStream == nullptr)
    {
        // Don't grow beyond what can be stored in a BSTR.
        if (sizeof(size_t) > sizeof(UINT) && m_vector.size() + byteCount > UINT_MAX)
        {
            THROW_WIN32(ERROR_ARITHMETIC_OVERFLOW);
        }

        m_vector.insert(m_vector.end(), pBytes, pBytes + byteCount);
    }
    else
    {
        THROW_IF_FAILED(WriteAllBytesToStream(m_pStream, std::span(static_cast<BYTE const*>(pBytes), byteCount)));
    }
}
