#include "pch.h"
#include "OutputSink.h"

#include <utf.h>

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

OutputSink::OutputSink() noexcept
    : m_bufferPos(0)
    , m_encoding(RegExEncoding_none)
    , m_pStream(nullptr)
    , m_vector()
{
}

void
OutputSink::ResetToVector(RegExEncoding outputEncoding)
{
    m_bufferPos = 0;
    m_encoding = outputEncoding;
    m_pStream = nullptr;
    m_vector.clear();
}

void
OutputSink::ResetToStream(RegExEncoding outputEncoding, _In_ ISequentialStream* pStream) noexcept
{
    assert(pStream != nullptr);
    m_bufferPos = 0;
    m_encoding = outputEncoding;
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
    RegExEncoding inputEncoding)
{
    void const* const data = inputBytes.data();
    size_t const size = inputBytes.size();

    switch (inputEncoding)
    {
    case RegExEncoding_latin1:
    {
        auto chars = std::span(static_cast<char const*>(data), size);
        auto range = latin1::CodePointIterator::FromSpan(chars);
        for (auto it = range.begin; it != range.end; ++it)
        {
            push_back(*it);
        }
        break;
    }
    case RegExEncoding_utf8:
    {
        auto chars = std::span(static_cast<char8_t const*>(data), size);
        auto range = utf8::CodePointIterator::FromSpan(chars);
        for (auto it = range.begin; it != range.end; ++it)
        {
            push_back(*it);
        }
        break;
    }
    case RegExEncoding_utf16le:
    {
        assert((size & 1) == 0);
        auto chars = std::span(static_cast<char16_t const*>(data), size / sizeof(char16_t));
        auto range = utf16le::CodePointIterator::FromSpan(chars);
        for (auto it = range.begin; it != range.end; ++it)
        {
            push_back(*it);
        }
        break;
    }
    case RegExEncoding_utf16be:
    {
        assert((size & 1) == 0);
        auto chars = std::span(static_cast<char16_t const*>(data), size / sizeof(char16_t));
        auto range = utf16be::CodePointIterator::FromSpan(chars);
        for (auto it = range.begin; it != range.end; ++it)
        {
            push_back(*it);
        }
        break;
    }
    default:
        THROW_HR(E_INVALIDARG);
    }
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

    switch (m_encoding)
    {
    case RegExEncoding_latin1:
        return latin1::ConvertInPlace(codePoints).size_bytes();
    case RegExEncoding_utf8:
        return utf8::ConvertInPlace(codePoints).size_bytes();
    case RegExEncoding_utf16le:
        return utf16le::ConvertInPlace(codePoints).size_bytes();
    case RegExEncoding_utf16be:
        return utf16be::ConvertInPlace(codePoints).size_bytes();
    default:
        THROW_HR(E_INVALIDARG);
    }
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
