#pragma once
#include <RepStrRegEx.h>

// Apartment-threaded IRegExMemoryStream implementation.
// Backed by a std::vector<BYTE> with a separate position cursor that supports
// IStream::Seek (position may sit anywhere in [0, size]). Writes past the end
// extend the logical size; seeks past the end leave the gap unwritten until
// the next Write fills it (matching IStream-on-HGLOBAL behavior).
class RegExMemoryStream final : public IRegExMemoryStream
{
    volatile long m_refCount;
    std::vector<BYTE> m_buffer; // logical size = m_buffer.size()
    UINT64 m_position;          // current Read/Write position; may equal m_buffer.size() (EOF)

public:

    explicit RegExMemoryStream(LONGLONG initialCapacity);

    // IUnknown

    HRESULT STDMETHODCALLTYPE
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override;

    ULONG STDMETHODCALLTYPE
    AddRef() noexcept override;

    ULONG STDMETHODCALLTYPE
    Release() noexcept override;

    // ISequentialStream

    HRESULT STDMETHODCALLTYPE
    Read(_Out_writes_bytes_to_(cb, *pcbRead) void* pv, ULONG cb, _Out_opt_ ULONG* pcbRead) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Write(_In_reads_bytes_(cb) void const* pv, ULONG cb, _Out_opt_ ULONG* pcbWritten) noexcept override;

    // IStream

    HRESULT STDMETHODCALLTYPE
    Seek(LARGE_INTEGER dlibMove, DWORD dwOrigin, _Out_opt_ ULARGE_INTEGER* plibNewPosition) noexcept override;

    HRESULT STDMETHODCALLTYPE
    SetSize(ULARGE_INTEGER libNewSize) noexcept override;

    HRESULT STDMETHODCALLTYPE
    CopyTo(IStream* pstm, ULARGE_INTEGER cb, _Out_opt_ ULARGE_INTEGER* pcbRead, _Out_opt_ ULARGE_INTEGER* pcbWritten) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Commit(DWORD grfCommitFlags) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Revert() noexcept override;

    HRESULT STDMETHODCALLTYPE
    LockRegion(ULARGE_INTEGER libOffset, ULARGE_INTEGER cb, DWORD dwLockType) noexcept override;

    HRESULT STDMETHODCALLTYPE
    UnlockRegion(ULARGE_INTEGER libOffset, ULARGE_INTEGER cb, DWORD dwLockType) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Stat(_Out_ STATSTG* pstatstg, DWORD grfStatFlag) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Clone(_Outptr_ IStream** ppstm) noexcept override;

    // IRegExMemoryStream

    HRESULT STDMETHODCALLTYPE
    Reset() noexcept override;

    HRESULT STDMETHODCALLTYPE
    Reserve(LONGLONG capacity) noexcept override;

    HRESULT STDMETHODCALLTYPE
    GetBuffer(_Out_ LONGLONG* pData, _Out_ LONGLONG* pSize) noexcept override;

private:

    // Convert a desired byte count (as UINT64) to size_t after validating
    // that it fits within m_buffer's maximum capacity. Returns S_OK and the
    // cast size, or STG_E_MEDIUMFULL if the request exceeds what the
    // vector can hold (possible on 32-bit, or with extreme requests on 64-bit).
    HRESULT
    ToBufferSize(UINT64 requested, _Out_ size_t* pSize) const noexcept;
};
