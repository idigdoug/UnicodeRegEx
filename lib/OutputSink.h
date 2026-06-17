#pragma once
#include <UnicodeRegEx.h>
#include <TextEncoding.h>

HRESULT
WriteAllBytesToStream(
    _In_ ISequentialStream* pStream,
    std::span<BYTE const> data) noexcept;

// Allocates a BSTR from a buffer of UTF-16 bytes (the byte count must be even).
// Centralizes the bytes -> BSTR-length narrowing: a BSTR length is a UINT, so a
// buffer larger than UINT_MAX UTF-16 code units cannot be represented and would
// otherwise silently truncate the length. Returns E_OUTOFMEMORY on allocation
// failure, or a failure HRESULT if the code-unit count exceeds UINT_MAX.
HRESULT
AllocBStrFromUtf16Bytes(
    std::span<BYTE const> utf16Bytes,
    _Out_ BSTR* pResult) noexcept;

// Allocates a BSTR from a buffer of UTF-16 code units. Like AllocBStrFromUtf16Bytes,
// but the length is already a code-unit count. Returns E_OUTOFMEMORY on allocation
// failure, or a failure HRESULT if the count exceeds UINT_MAX.
HRESULT
AllocBStrFromChars(
    std::span<char16_t const> chars,
    _Out_ BSTR* pResult) noexcept;

// Accumulates char32_t code points via push_back (for use with std::back_inserter),
// transcodes them in batches into the configured output encoding, and either
// accumulates the transcoded bytes in an internal vector or forwards them to an
// ISequentialStream.
//
// Lifecycle:
//   1. Construct (or call ResetToVector / ResetToStream).
//   2. push_back code points (e.g. via std::back_inserter(sink)).
//   3. Call FinishVector() or FinishStream() (matching the ResetTo* call) to
//      flush any buffered code points.
//      - FinishVector() returns RegExBytes pointing at the accumulated bytes.
//        The pointer is valid until the next ResetTo* (or until this object is
//        destroyed).
//      - FinishStream() returns void and clears the stored stream pointer.
//   4. Reuse: call ResetToVector / ResetToStream again.
//
// Small-buffer optimization: when the destination is the vector and the entire
// transcoded output fits in the internal buffer (no flushes happened during
// push_back), FinishVector() returns a pointer into the internal buffer and does
// not touch the vector.
class OutputSink
{
    // Internal buffer capacity, in char32_t code points.
    // Starts with char32_t data but transcoded in-place so it may hold non-char32 bytes.
    static constexpr size_t BufferCapacity = 128;

    char32_t m_buffer[BufferCapacity];
    size_t m_bufferPos;
    TextEncoding m_outputEncoding;
    ISequentialStream* m_pStream; // non-owning; nullptr means flush to vector.
    std::vector<BYTE> m_vector;

public:

    // Required by std::back_insert_iterator.
    using value_type = char32_t;

    // Constructs a sink in vector mode with no configured encoding. Call
    // ResetToVector or ResetToStream before pushing code points.
    OutputSink() noexcept;

    OutputSink(OutputSink const&) = delete;
    OutputSink& operator=(OutputSink const&) = delete;

    // Reset to accumulate transcoded bytes in the internal vector.
    // Clears any previously buffered data.
    // Vector is limited to UINT_MAX bytes so that the result can fit in a BSTR.
    void
    ResetToVector(TextEncoding outputEncoding);

    // Reset to forward transcoded bytes to the given stream.
    // The sink does NOT take a reference on the stream; the caller must keep it
    // alive until Finish() returns (Finish() clears the stored pointer).
    void
    ResetToStream(TextEncoding outputEncoding, _In_ ISequentialStream* pStream) noexcept;

    // Append a code point. When the internal buffer fills, the contents are
    // transcoded and either appended to the vector or written to the stream.
    // May throw (bad_alloc, or HRESULT wrapped by THROW_HR on stream write failure).
    void
    push_back(char32_t codePoint);

    // Append bytes. May throw (bad_alloc, or HRESULT wrapped by THROW_HR on stream write failure).
    void
    AppendBytes(std::span<BYTE const> inputBytes, TextEncoding inputEncoding);

    // Append bytes that are ALREADY in this sink's configured output encoding.
    // Copies them verbatim (no decode/re-encode round-trip through char32_t), so
    // malformed sequences in the input are preserved byte-for-byte. Any buffered
    // code points are flushed first to preserve ordering.
    // May throw (bad_alloc, or HRESULT wrapped by THROW_HR on stream write failure).
    void
    AppendRawBytes(std::span<BYTE const> bytes);

    // The output encoding configured by the most recent ResetTo* call.
    TextEncoding
    OutputEncoding() const noexcept;

    // Flush any buffered code points and return the accumulated bytes.
    // Must only be called after ResetToVector.
    std::span<BYTE const>
    FinishVector();

    // Flush any buffered code points to the stream and clear the stored stream pointer.
    // Must only be called after ResetToStream.
    void
    FinishStream();

private:

    // Transcode the contents of m_buffer in-place into the configured encoding.
    // Returns the byte count after transcoding.
    size_t
    TranscodeBufferInPlace();

    // Transcode the buffer and either append to m_vector or write to m_pStream.
    // Resets m_bufferPos to 0.
    void
    Flush();
};
