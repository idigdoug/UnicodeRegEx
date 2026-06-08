#include "pch.h"
#include "RegExFileStream.h"

// IoScope: RAII helper that acquires m_lock exclusively (after a fast-path
// cancel check) and releases it on destruction. On release, if Cancel ran
// while we held the lock, completes the cancelling -> cancelled transition.
class RegExFileStream::IoScope
{
    RegExFileStream& m_stream;
    wil::rwlock_release_exclusive_scope_exit m_lockGuard;
    bool m_running; // True iff the I/O method should run its locked code.

public:

    IoScope(IoScope const&) = delete;
    IoScope& operator=(IoScope const&) = delete;

    explicit
    IoScope(RegExFileStream& stream) noexcept
        : m_stream(stream)
        , m_lockGuard(stream.m_lock.lock_exclusive())
        , m_running(
            stream.m_cancelState.load(std::memory_order_acquire) == RegExStreamCancelStatus_running)
    {
    }

    ~IoScope() noexcept
    {
        // Release the lock explicitly before UpdateCancelState so a racing
        // Cancel that tries try_lock_exclusive can succeed and (if it CASes
        // first) finish the transition instead of us.
        m_lockGuard.reset();
        m_stream.UpdateCancelState();
    }

    // True iff the caller may proceed with its locked work. False if the
    // stream was already cancelled when we acquired the lock; the caller
    // should return E_ABORT in that case.
    explicit
    operator bool() const noexcept
    {
        return m_running;
    }
};

// Constructor / destructor / OpenFile.

RegExFileStream::~RegExFileStream()
{
    if (m_cancelState.load(std::memory_order_relaxed) == RegExStreamCancelStatus_running &&
        !m_writeBuffer.empty())
    {
        // Lock not needed during destructor.
        (void)FlushBufferLocked();
    }
}

RegExFileStream::RegExFileStream(std::wstring path, RegExFileStreamFlags flags)
    : m_refCount(1)
    , m_freeThreadedMarshaler()
    , m_lock()
    , m_file()
    , m_path(std::move(path))
    , m_writeBuffer()
    , m_flags(flags)
    , m_cancelState(RegExStreamCancelStatus_running)
    , m_cancelledEvent(wil::EventOptions::ManualReset)
{
    DWORD const dispositionBits = static_cast<DWORD>(m_flags) & 0x3;
    DWORD creationDisposition;
    switch (dispositionBits)
    {
    case RegExFileStreamFlag_open_existing:
        creationDisposition = OPEN_EXISTING;
        break;
    case RegExFileStreamFlag_create_new:
        creationDisposition = CREATE_NEW;
        break;
    case RegExFileStreamFlag_create_always:
        creationDisposition = CREATE_ALWAYS;
        break;
    case RegExFileStreamFlag_open_or_create:
        creationDisposition = OPEN_ALWAYS;
        break;
    default:
        THROW_HR(E_INVALIDARG); // Unreachable.
    }

    DWORD flagsAndAttributes = FILE_ATTRIBUTE_NORMAL;
    if (m_flags & RegExFileStreamFlag_sequential)
    {
        flagsAndAttributes |= FILE_FLAG_SEQUENTIAL_SCAN;
    }

    if (m_flags & RegExFileStreamFlag_write_through)
    {
        flagsAndAttributes |= FILE_FLAG_WRITE_THROUGH;
    }

    DWORD const shareMode = FILE_SHARE_READ | FILE_SHARE_DELETE;

    // Request DELETE only when the caller asked for delete_on_close (applied
    // below via FileDispositionInfo). MoveTo opens a side handle with DELETE
    // access when needed, so streams without delete_on_close don't force
    // readers to specify FILE_SHARE_DELETE to coexist with the stream.
    // delete_on_close is intentionally NOT passed as FILE_FLAG_DELETE_ON_CLOSE
    // so MoveTo can later cancel the disposition (the CreateFile-time flag is
    // sticky and cannot be cleared).
    DWORD desiredAccess = GENERIC_READ | GENERIC_WRITE;
    if (m_flags & RegExFileStreamFlag_delete_on_close)
    {
        desiredAccess |= DELETE;
    }

    m_file.reset(CreateFileW(
        m_path.c_str(),
        desiredAccess,
        shareMode,
        nullptr,
        creationDisposition,
        flagsAndAttributes,
        nullptr));

    if (!m_file)
    {
        THROW_LAST_ERROR();
    }

    if (m_flags & RegExFileStreamFlag_delete_on_close)
    {
        FILE_DISPOSITION_INFO dispInfo{};
        dispInfo.DeleteFile = TRUE;
        if (!SetFileInformationByHandle(m_file.get(), FileDispositionInfo, &dispInfo, sizeof(dispInfo)))
        {
            // Best-effort cleanup: caller asked for delete_on_close, so close
            // the handle and try DeleteFileW. Preserve the original error.
            DWORD const err = GetLastError();
            m_file.reset();
            (void)DeleteFileW(m_path.c_str());
            THROW_WIN32(err);
        }
    }
}

// IUnknown.

HRESULT STDMETHODCALLTYPE
RegExFileStream::QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept
{
    if (ppvObject == nullptr)
    {
        return E_POINTER;
    }

    if (riid == __uuidof(IUnknown) ||
        riid == __uuidof(ISequentialStream) ||
        riid == __uuidof(IStream) ||
        riid == __uuidof(IRegExFileStream))
    {
        *ppvObject = static_cast<IRegExFileStream*>(this);
        AddRef();
        return S_OK;
    }

    if (riid == __uuidof(IMarshal))
    {
        if (!m_freeThreadedMarshaler)
        {
            wil::com_ptr<IUnknown> ftm;
            RETURN_IF_FAILED(CoCreateFreeThreadedMarshaler(this, ftm.put()));
            if (nullptr == InterlockedCompareExchangePointer(
                reinterpret_cast<void**>(m_freeThreadedMarshaler.addressof()),
                ftm.get(),
                nullptr))
            {
                (void)ftm.detach();
            }
        }

        return m_freeThreadedMarshaler->QueryInterface(riid, ppvObject);
    }

    *ppvObject = nullptr;
    return E_NOINTERFACE;
}

ULONG STDMETHODCALLTYPE
RegExFileStream::AddRef() noexcept
{
    return InterlockedIncrementNoFence(&m_refCount);
}

ULONG STDMETHODCALLTYPE
RegExFileStream::Release() noexcept
{
    ULONG ref = InterlockedDecrementRelease(&m_refCount);
    if (ref == 0)
    {
        delete this;
    }

    return ref;
}

// ISequentialStream

HRESULT STDMETHODCALLTYPE
RegExFileStream::Read(_Out_writes_bytes_to_(cb, *pcbRead) void* pv, ULONG cb, _Out_opt_ ULONG* pcbRead) noexcept
{
    if (pcbRead)
    {
        *pcbRead = 0;
    }

    if (pv == nullptr && cb != 0)
    {
        return STG_E_INVALIDPOINTER;
    }

    IoScope scope(*this);
    if (!scope)
    {
        return E_ABORT;
    }

    // Reads bypass the write buffer; flush first so we read consistent file state.
    RETURN_IF_FAILED(FlushBufferLocked());

    DWORD bytesRead = 0;
    if (!ReadFile(m_file.get(), pv, cb, &bytesRead, nullptr))
    {
        DWORD const err = GetLastError();
        if (err == ERROR_OPERATION_ABORTED)
        {
            return E_ABORT;
        }

        return HRESULT_FROM_WIN32(err);
    }

    if (pcbRead)
    {
        *pcbRead = bytesRead;
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::Write(_In_reads_bytes_(cb) void const* pv, ULONG cb, _Out_opt_ ULONG* pcbWritten) noexcept
{
    if (pcbWritten)
    {
        *pcbWritten = 0;
    }

    if (pv == nullptr && cb != 0)
    {
        return STG_E_INVALIDPOINTER;
    }

    IoScope scope(*this);
    if (!scope)
    {
        return E_ABORT;
    }

    if (cb == 0)
    {
        return S_OK;
    }

    // Large payloads bypass the buffer entirely: flush whatever's accumulated
    // (so file-position order is preserved), then write the caller's bytes
    // directly. Avoids pointlessly copying through the 64 KB buffer.
    if (cb >= WriteBufferCapacity)
    {
        RETURN_IF_FAILED(FlushBufferLocked());
        RETURN_IF_FAILED(WriteAllLocked(static_cast<BYTE const*>(pv), cb));
        if (pcbWritten)
        {
            *pcbWritten = cb;
        }

        return S_OK;
    }

    try
    {
        if (m_writeBuffer.capacity() < WriteBufferCapacity)
        {
            m_writeBuffer.reserve(WriteBufferCapacity);
        }
    }
    catch (std::bad_alloc const&)
    {
        return E_OUTOFMEMORY;
    }

    ULONG remaining = cb;
    auto* src = static_cast<BYTE const*>(pv);

    while (remaining > 0)
    {
        size_t const room = WriteBufferCapacity - m_writeBuffer.size();
        size_t const chunk = std::min<size_t>(remaining, room);

        try
        {
            m_writeBuffer.insert(m_writeBuffer.end(), src, src + chunk);
        }
        catch (std::bad_alloc const&)
        {
            return E_OUTOFMEMORY; // Unreachable.
        }

        src += chunk;
        remaining -= static_cast<ULONG>(chunk);

        if (m_writeBuffer.size() == WriteBufferCapacity)
        {
            RETURN_IF_FAILED(FlushBufferLocked());
        }
    }

    if (pcbWritten)
    {
        *pcbWritten = cb;
    }

    return S_OK;
}

// IStream.

HRESULT STDMETHODCALLTYPE
RegExFileStream::Seek(LARGE_INTEGER dlibMove, DWORD dwOrigin, _Out_opt_ ULARGE_INTEGER* plibNewPosition) noexcept
{
    DWORD moveMethod;
    switch (dwOrigin)
    {
    case STREAM_SEEK_SET:
        moveMethod = FILE_BEGIN;
        break;
    case STREAM_SEEK_CUR:
        moveMethod = FILE_CURRENT;
        break;
    case STREAM_SEEK_END:
        moveMethod = FILE_END;
        break;
    default:
        return STG_E_INVALIDFUNCTION;
    }

    IoScope scope(*this);
    if (!scope)
    {
        return E_ABORT;
    }

    // Flush buffer so subsequent reads/writes are positioned correctly.
    RETURN_IF_FAILED(FlushBufferLocked());

    LARGE_INTEGER newPos;
    if (!SetFilePointerEx(m_file.get(), dlibMove, &newPos, moveMethod))
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    if (plibNewPosition)
    {
        plibNewPosition->QuadPart = static_cast<UINT64>(newPos.QuadPart);
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::SetSize(ULARGE_INTEGER libNewSize) noexcept
{
    IoScope scope(*this);
    if (!scope)
    {
        return E_ABORT;
    }

    RETURN_IF_FAILED(FlushBufferLocked());

    FILE_END_OF_FILE_INFO info{};
    info.EndOfFile.QuadPart = static_cast<LONGLONG>(libNewSize.QuadPart);
    if (!SetFileInformationByHandle(m_file.get(), FileEndOfFileInfo, &info, sizeof(info)))
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::CopyTo(IStream* /*pstm*/, ULARGE_INTEGER /*cb*/, _Out_opt_ ULARGE_INTEGER* pcbRead, _Out_opt_ ULARGE_INTEGER* pcbWritten) noexcept
{
    if (pcbRead)
    {
        pcbRead->QuadPart = 0;
    }

    if (pcbWritten)
    {
        pcbWritten->QuadPart = 0;
    }

    return E_NOTIMPL;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::Commit(DWORD /*grfCommitFlags*/) noexcept
{
    return Flush();
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::Revert() noexcept
{
    return E_NOTIMPL;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::LockRegion(ULARGE_INTEGER /*libOffset*/, ULARGE_INTEGER /*cb*/, DWORD /*dwLockType*/) noexcept
{
    return STG_E_INVALIDFUNCTION;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::UnlockRegion(ULARGE_INTEGER /*libOffset*/, ULARGE_INTEGER /*cb*/, DWORD /*dwLockType*/) noexcept
{
    return STG_E_INVALIDFUNCTION;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::Stat(_Out_ STATSTG* pstatstg, DWORD /*grfStatFlag*/) noexcept
{
    if (pstatstg == nullptr)
    {
        return STG_E_INVALIDPOINTER;
    }

    IoScope scope(*this);
    if (!scope)
    {
        return E_ABORT;
    }

    *pstatstg = {};
    pstatstg->type = STGTY_STREAM;
    pstatstg->grfMode = STGM_READWRITE;

    LARGE_INTEGER size{};
    if (!GetFileSizeEx(m_file.get(), &size))
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    pstatstg->cbSize.QuadPart = static_cast<UINT64>(size.QuadPart) + m_writeBuffer.size();
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::Clone(_Outptr_ IStream** ppstm) noexcept
{
    if (ppstm)
    {
        *ppstm = nullptr;
    }

    return E_NOTIMPL;
}

// IRegExFileStream.

HRESULT STDMETHODCALLTYPE
RegExFileStream::get_Path(_Out_ BSTR* pPath) noexcept
{
    if (pPath == nullptr)
    {
        return E_POINTER;
    }

    // Shared lock against concurrent MoveTo, which can replace m_path. Most
    // callers won't race, but the FTM aggregation means cross-thread calls
    // are technically possible.
    auto guard = m_lock.lock_shared();
    *pPath = SysAllocStringLen(m_path.c_str(), static_cast<UINT>(m_path.size()));
    return *pPath ? S_OK : E_OUTOFMEMORY;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::Flush() noexcept
{
    IoScope scope(*this);
    if (!scope)
    {
        return E_ABORT;
    }

    RETURN_IF_FAILED(FlushBufferLocked());

    if (!FlushFileBuffers(m_file.get()))
    {
        DWORD const err = GetLastError();
        if (err == ERROR_OPERATION_ABORTED)
        {
            return E_ABORT;
        }

        return HRESULT_FROM_WIN32(err);
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::Cancel() noexcept
{
    // Transition running -> cancelling. Idempotent: subsequent Cancels just
    // re-attempt CancelIoEx (may be useful). The compare_exchange uses release on
    // success so observers that load-acquire see CancelIoEx's effects.
    RegExStreamCancelStatus expected = RegExStreamCancelStatus_running;
    bool const transitionedToCancelling = m_cancelState.compare_exchange_strong(
        expected,
        RegExStreamCancelStatus_cancelling,
        std::memory_order_release,
        std::memory_order_acquire);
    (void)transitionedToCancelling;

    if (expected == RegExStreamCancelStatus_cancelled)
    {
        return S_OK; // already cancelled, nothing to do
    }

    // Attempt to interrupt any pending synchronous I/O on the file handle.
    // CancelIoEx is safe to call from another thread.
    (void)CancelIoEx(m_file.get(), nullptr);

    // If no I/O is currently in progress (no IoScope holds the lock), we
    // transition straight to cancelled. try_lock_exclusive is non-blocking:
    // if an IoScope holds the lock, we leave the transition to its destructor.
    if (auto guard = m_lock.try_lock_exclusive())
    {
        // Release the lock before UpdateCancelState so we don't delay any I/O
        // method that's about to try-acquire it.
        guard.reset();
        UpdateCancelState();
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::get_CancelStatus(_Out_ RegExStreamCancelStatus* pStatus) noexcept
{
    if (pStatus == nullptr)
    {
        return E_POINTER;
    }

    *pStatus = m_cancelState.load(std::memory_order_acquire);
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::WaitForCancelled(UINT32 timeoutMs, _Out_ VARIANT_BOOL* pCancelled) noexcept
{
    if (pCancelled == nullptr)
    {
        return E_POINTER;
    }

    *pCancelled = VARIANT_FALSE;

    RegExStreamCancelStatus const status = m_cancelState.load(std::memory_order_acquire);
    if (status == RegExStreamCancelStatus_cancelled)
    {
        *pCancelled = VARIANT_TRUE;
        return S_OK;
    }

    if (status == RegExStreamCancelStatus_running)
    {
        return E_NOT_VALID_STATE;
    }

    // Cancelling: wait for the event.
    DWORD const wait = WaitForSingleObject(m_cancelledEvent.get(), timeoutMs);
    if (wait == WAIT_OBJECT_0)
    {
        *pCancelled = VARIANT_TRUE;
        return S_OK;
    }

    if (wait == WAIT_TIMEOUT)
    {
        return S_OK;
    }

    return HRESULT_FROM_WIN32(GetLastError());
}

HRESULT STDMETHODCALLTYPE
RegExFileStream::MoveTo(_In_ BSTR destinationPath, RegExFileMoveFlags flags) noexcept
{
    auto const destinationPathLen = wcsnlen(destinationPath, SysStringLen(destinationPath));
    if (destinationPathLen == 0 || destinationPathLen > 32768)
    {
        return E_INVALIDARG;
    }

    std::wstring destination;
    std::unique_ptr<BYTE[]> fileRenameInfoBuffer;
    auto const fileRenameInfoSize = static_cast<unsigned>(
        sizeof(FILE_RENAME_INFO) + destinationPathLen * sizeof(WCHAR));
    try
    {
        destination.assign(destinationPath, destinationPathLen);

        // FILE_RENAME_INFO with trailing FileName[] buffer.
        fileRenameInfoBuffer = std::make_unique<BYTE[]>(fileRenameInfoSize);
    }
    catch (std::bad_alloc const&)
    {
        return E_OUTOFMEMORY;
    }

    auto* renameInfo = reinterpret_cast<FILE_RENAME_INFO*>(fileRenameInfoBuffer.get());
    // The first union member is shared with the legacy ReplaceIfExists BOOLEAN
    // (low bit of Flags). Setting Flags is forward-compatible with both
    // FileRenameInfoEx and the legacy FileRenameInfo.
    renameInfo->Flags =
        ((flags & RegExFileMoveFlag_replace_existing) ? FILE_RENAME_FLAG_REPLACE_IF_EXISTS : 0u) |
        FILE_RENAME_FLAG_POSIX_SEMANTICS;
    renameInfo->RootDirectory = nullptr;
    renameInfo->FileNameLength = static_cast<unsigned>(destination.size() * sizeof(WCHAR));
    memcpy(renameInfo->FileName, destination.c_str(), renameInfo->FileNameLength);

    IoScope scope(*this);
    if (!scope)
    {
        return E_ABORT;
    }

    // Flush any buffered writes before renaming.
    RETURN_IF_FAILED(FlushBufferLocked());

    // Decide which handle to issue the rename on. The main handle was opened
    // with DELETE access only when delete_on_close was requested; if not,
    // open a side handle now. This keeps non-delete-on-close streams from
    // forcing readers to specify FILE_SHARE_DELETE just to coexist.
    wil::unique_hfile sideHandle;
    HANDLE renameHandle;
    if (m_flags & RegExFileStreamFlag_delete_on_close)
    {
        // Clear the delete-on-close flag so the file lives on after the move.
        FILE_DISPOSITION_INFO dispInfo{};
        dispInfo.DeleteFile = FALSE;
        if (!SetFileInformationByHandle(m_file.get(), FileDispositionInfo, &dispInfo, sizeof(dispInfo)))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        renameHandle = m_file.get();
    }
    else
    {
        // Open a side handle just for the rename. The main handle was opened
        // without DELETE so other openers don't have to specify
        // FILE_SHARE_DELETE just to coexist with the stream. DuplicateHandle
        // can't be used here because it can only copy or reduce access bits,
        // not add new ones.
        sideHandle.reset(CreateFileW(
            m_path.c_str(),
            DELETE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            nullptr));
        if (!sideHandle)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        renameHandle = sideHandle.get();
    }

    // Prefer FileRenameInfoEx for POSIX semantics: lets the rename succeed
    // even if the destination has other openers (their handles continue to
    // refer to the renamed-away inode). Fall back to legacy FileRenameInfo
    // on filesystems that don't support the Ex variant (older NTFS, FAT,
    // many network filesystems).
    bool renamed =
        SetFileInformationByHandle(renameHandle, FileRenameInfoEx, renameInfo, fileRenameInfoSize) ||
        SetFileInformationByHandle(renameHandle, FileRenameInfo, renameInfo, fileRenameInfoSize);
    if (!renamed)
    {
        DWORD const err = GetLastError();

        // Best effort: if rename failed and we cleared delete_on_close, try to
        // restore it so the temp file still gets cleaned up.
        if (m_flags & RegExFileStreamFlag_delete_on_close)
        {
            FILE_DISPOSITION_INFO restoreInfo{};
            restoreInfo.DeleteFile = TRUE;
            (void)SetFileInformationByHandle(m_file.get(), FileDispositionInfo, &restoreInfo, sizeof(restoreInfo));
        }

        return HRESULT_FROM_WIN32(err);
    }

    // Successfully renamed. Update our recorded path and clear the
    // delete_on_close flag (the file now belongs at destinationPath).
    m_path = std::move(destination);
    m_flags = static_cast<RegExFileStreamFlags>(m_flags & ~RegExFileStreamFlag_delete_on_close);
    return S_OK;
}

// Private helpers.

void
RegExFileStream::UpdateCancelState() noexcept
{
    // Attempt the cancelling -> cancelled transition. Called from Cancel
    // (after try_lock_exclusive confirms no I/O is in progress) and from
    // IoScope's destructor (after the lock is released). compare_exchange
    // ensures exactly one party signals m_cancelledEvent.
    RegExStreamCancelStatus expected = RegExStreamCancelStatus_cancelling;
    if (m_cancelState.compare_exchange_strong(
        expected,
        RegExStreamCancelStatus_cancelled,
        std::memory_order_release,
        std::memory_order_relaxed))
    {
        SetEvent(m_cancelledEvent.get());
    }
}

HRESULT
RegExFileStream::FlushBufferLocked() noexcept
{
    HRESULT const hr = WriteAllLocked(m_writeBuffer.data(), m_writeBuffer.size());
    m_writeBuffer.clear();
    return hr;
}

HRESULT
RegExFileStream::WriteAllLocked(_In_reads_bytes_(size) BYTE const* data, size_t size) noexcept
{
    while (size > 0)
    {
        // Re-check cancel state on every iteration so a Cancel that arrives
        // mid-loop bounds the worst-case extra-write to a single chunk. (The
        // entry check on each public method runs only once per call; without
        // this loop check, a multi-chunk write would keep issuing WriteFile
        // calls after Cancel was visible.)
        // There remains an unavoidable race window where Cancel fires after
        // this check but before WriteFile starts — in that case the in-flight
        // WriteFile may still complete. CancelIoEx will abort it if it has
        // already started, but it cannot prevent one that hasn't yet.
        if (m_cancelState.load(std::memory_order_acquire) != RegExStreamCancelStatus_running)
        {
            return E_ABORT;
        }

        // Cap loop at 1GB.
        DWORD const chunk = static_cast<DWORD>(std::min<size_t>(size, 0x40000000UL));
        DWORD written = 0;
        if (!WriteFile(m_file.get(), data, chunk, &written, nullptr))
        {
            DWORD const err = GetLastError();
            return err == ERROR_OPERATION_ABORTED
                ? E_ABORT
                : HRESULT_FROM_WIN32(err);
        }

        if (written == 0)
        {
            // Defensive: WriteFile reported success but made no progress.
            // Should not happen on a disk file, but guard against an infinite
            // loop and treat it as a failure.
            return E_FAIL;
        }

        data += written;
        size -= written;
    }

    return S_OK;
}
