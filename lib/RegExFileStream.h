#pragma once
#include <RepStrRegEx.h>

// IRegExFileStream implementation.
//
// Threading: marshals as free-threaded (aggregates the FTM so cross-
// apartment hand-offs return the raw pointer), but most methods are
// intended for single-thread use. The internal SRW lock exists so that
// the cross-thread Cancel / CancelStatus / WaitForCancelled methods are safe
// relative to an in-progress I/O call on another thread, not to support
// concurrent I/O from multiple threads.
//
// Cancel design:
//   - m_cancelState is an atomic state machine: running -> cancelling -> cancelled.
//   - Cancel transitions running -> cancelling, calls CancelIoEx, and (if no I/O is
//     in progress) completes the transition to cancelled itself. If I/O is in progress,
//     the next IoScope to release the lock performs the transition.
//   - IoScope is a guard that takes m_lock exclusively in its constructor and
//     records whether the stream was cancelled at lock-acquisition time. The I/O
//     method tests the scope (via explicit operator bool) and returns E_ABORT if
//     the stream was cancelled. The destructor always releases the lock and,
//     if m_cancelState is cancelling, completes the transition to cancelled.
//   - Cancel uses try_lock_exclusive to detect "no I/O in progress" without
//     blocking. If the try fails, an IoScope is holding the lock; that scope's
//     destructor will see the cancelling state and complete the transition.
//   - The cancelling -> cancelled CAS in IoScope::~IoScope and Cancel ensures the
//     transition (and the m_cancelledEvent signal) happens exactly once.
class RegExFileStream final : public IRegExFileStream
{
    class IoScope;

    static constexpr size_t WriteBufferCapacity = 64 * 1024; // Write buffer size limit.

    // --- Refcount / FTM (each guarded by its own atomic / interlocked) ---

    volatile long m_refCount;
    wil::com_ptr<IUnknown> m_freeThreadedMarshaler; // Delay-created on first IMarshal query.

    // --- Per-stream I/O state (guarded by m_lock) ---

    wil::srwlock m_lock;
    wil::unique_hfile m_file;           // File owned by this object.
    std::wstring m_path;                // Path corresponding to m_file. Updated by MoveTo.
    std::vector<BYTE> m_writeBuffer;
    RegExFileStreamFlags m_flags;       // Stream flags. Used by MoveTo.

    // --- Cancel state (independent of m_lock so Cancel is non-blocking) ---

    std::atomic<RegExStreamCancelStatus> m_cancelState;
    wil::unique_event m_cancelledEvent; // Signalled when m_cancelState transitions to cancelled.

public:

    ~RegExFileStream();

    RegExFileStream(
        std::wstring path,
        RegExFileStreamFlags flags);

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
    CopyTo(
        IStream* pstm,
        ULARGE_INTEGER cb,
        _Out_opt_ ULARGE_INTEGER* pcbRead,
        _Out_opt_ ULARGE_INTEGER* pcbWritten) noexcept override;

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

    // IRegExFileStream

    HRESULT STDMETHODCALLTYPE
    get_Path(_Out_ BSTR* pPath) noexcept override;

    HRESULT STDMETHODCALLTYPE
    get_CancelStatus(_Out_ RegExStreamCancelStatus* pStatus) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Flush() noexcept override;

    HRESULT STDMETHODCALLTYPE
    Cancel() noexcept override;

    HRESULT STDMETHODCALLTYPE
    WaitForCancelled(UINT32 timeoutMs, _Out_ VARIANT_BOOL* pCancelled) noexcept override;

    HRESULT STDMETHODCALLTYPE
    MoveTo(_In_ BSTR destinationPath, RegExFileMoveFlags flags) noexcept override;

private:

    // Attempt the cancelling -> cancelled transition. Called from Cancel
    // (after try_lock_exclusive confirms no I/O is in progress) and from
    // IoScope's destructor (after the lock is released). compare_exchange
    // ensures exactly one party signals m_cancelledEvent.
    void
    UpdateCancelState() noexcept;

    // Flush the internal write buffer to the file. Caller must hold m_lock.
    // Returns S_OK on success, E_ABORT if cancelled mid-flight, or an HRESULT
    // wrapping the WriteFile failure.
    HRESULT
    FlushBufferLocked() noexcept;

    // Write all bytes in [data, data + size) to the file in a loop, chunking
    // as needed. Caller must hold m_lock. Does not touch m_writeBuffer.
    // Returns S_OK, E_ABORT if cancelled, E_FAIL if WriteFile reported
    // success with zero progress, or an HRESULT wrapping the WriteFile failure.
    HRESULT
    WriteAllLocked(_In_reads_bytes_(size) BYTE const* data, size_t size) noexcept;
};
