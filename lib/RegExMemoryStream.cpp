#include "pch.h"
#include "RegExMemoryStream.h"

RegExMemoryStream::RegExMemoryStream(LONGLONG initialCapacity)
    : m_refCount(1)
    , m_buffer()
    , m_position(0)
{
    // Cap at min(max_size(), INT64_MAX) for the same reason ToBufferSize does:
    // keeping the logical size within signed 64-bit range simplifies Seek.
    UINT64 const cap = std::min<UINT64>(m_buffer.max_size(), static_cast<UINT64>(INT64_MAX));
    UINT64 const requested = std::min<UINT64>(static_cast<UINT64>(initialCapacity), cap);
    m_buffer.reserve(static_cast<size_t>(requested));
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept
{
    if (ppvObject == nullptr)
    {
        return E_POINTER;
    }

    if (riid == __uuidof(IUnknown) ||
        riid == __uuidof(ISequentialStream) ||
        riid == __uuidof(IStream) ||
        riid == __uuidof(IRegExMemoryStream))
    {
        *ppvObject = static_cast<IRegExMemoryStream*>(this);
        AddRef();
        return S_OK;
    }

    *ppvObject = nullptr;
    return E_NOINTERFACE;
}

ULONG STDMETHODCALLTYPE
RegExMemoryStream::AddRef() noexcept
{
    return InterlockedIncrementNoFence(&m_refCount);
}

ULONG STDMETHODCALLTYPE
RegExMemoryStream::Release() noexcept
{
    ULONG ref = InterlockedDecrementRelease(&m_refCount);
    if (ref == 0)
    {
        delete this;
    }

    return ref;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Read(_Out_writes_bytes_to_(cb, *pcbRead) void* pv, ULONG cb, _Out_opt_ ULONG* pcbRead) noexcept
{
    if (pv == nullptr && cb != 0)
    {
        return STG_E_INVALIDPOINTER;
    }

    ULONG bytesRead = 0;
    if (m_position < m_buffer.size())
    {
        UINT64 const available = m_buffer.size() - m_position;
        bytesRead = static_cast<ULONG>(std::min<UINT64>(cb, available));
        memcpy(pv, m_buffer.data() + m_position, bytesRead);
        m_position += bytesRead;
    }

    if (pcbRead)
    {
        *pcbRead = bytesRead;
    }
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Write(_In_reads_bytes_(cb) void const* pv, ULONG cb, _Out_opt_ ULONG* pcbWritten) noexcept
{
    if (pv == nullptr && cb != 0)
    {
        return STG_E_INVALIDPOINTER;
    }

    if (cb == 0)
    {
        if (pcbWritten)
        {
            *pcbWritten = 0;
        }
        return S_OK;
    }

    try
    {
        UINT64 const endPos = m_position + cb;
        if (endPos > m_buffer.size())
        {
            size_t newSize = 0;
            HRESULT const hr = ToBufferSize(endPos, &newSize);
            if (FAILED(hr))
            {
                if (pcbWritten)
                {
                    *pcbWritten = 0;
                }
                return hr;
            }
            m_buffer.resize(newSize);
        }
        memcpy(m_buffer.data() + m_position, pv, cb);
        m_position = endPos;
    }
    catch (std::bad_alloc const&)
    {
        if (pcbWritten)
        {
            *pcbWritten = 0;
        }
        return STG_E_MEDIUMFULL;
    }

    if (pcbWritten)
    {
        *pcbWritten = cb;
    }
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Seek(LARGE_INTEGER dlibMove, DWORD dwOrigin, _Out_opt_ ULARGE_INTEGER* plibNewPosition) noexcept
{
    // m_position and m_buffer.size() are both invariantly in [0, INT64_MAX]
    // (enforced by ToBufferSize / constructor / Seek itself), so the casts
    // below are unconditionally safe.
    INT64 base;
    switch (dwOrigin)
    {
    case STREAM_SEEK_SET:
        base = 0;
        break;
    case STREAM_SEEK_CUR:
        base = static_cast<INT64>(m_position);
        break;
    case STREAM_SEEK_END:
        base = static_cast<INT64>(m_buffer.size());
        break;
    default:
        return STG_E_INVALIDFUNCTION;
    }

    // Detect signed overflow when computing base + dlibMove.QuadPart.
    INT64 const delta = dlibMove.QuadPart;
    if ((delta > 0 && base > INT64_MAX - delta) ||
        (delta < 0 && base < INT64_MIN - delta))
    {
        return STG_E_INVALIDFUNCTION;
    }

    INT64 const newPos = base + delta;
    if (newPos < 0)
    {
        return STG_E_INVALIDFUNCTION;
    }

    m_position = static_cast<UINT64>(newPos);
    if (plibNewPosition)
    {
        plibNewPosition->QuadPart = m_position;
    }
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::SetSize(ULARGE_INTEGER libNewSize) noexcept
{
    size_t newSize = 0;
    RETURN_IF_FAILED(ToBufferSize(libNewSize.QuadPart, &newSize));

    try
    {
        m_buffer.resize(newSize);
    }
    catch (std::bad_alloc const&)
    {
        return STG_E_MEDIUMFULL;
    }
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::CopyTo(IStream* pstm, ULARGE_INTEGER cb, _Out_opt_ ULARGE_INTEGER* pcbRead, _Out_opt_ ULARGE_INTEGER* pcbWritten) noexcept
{
    if (pstm == nullptr)
    {
        return STG_E_INVALIDPOINTER;
    }

    UINT64 totalRead = 0;
    UINT64 totalWritten = 0;
    HRESULT hr = S_OK;

    while (cb.QuadPart > 0 && m_position < m_buffer.size())
    {
        UINT64 const available = m_buffer.size() - m_position;
        ULONG const chunk = static_cast<ULONG>(std::min<UINT64>({ cb.QuadPart, available, 0x40000ull }));

        ULONG written = 0;
        hr = pstm->Write(m_buffer.data() + m_position, chunk, &written);

        totalRead += written;
        totalWritten += written;
        m_position += written;
        cb.QuadPart -= written;

        if (FAILED(hr) || written == 0)
        {
            break;
        }
    }

    if (pcbRead)
    {
        pcbRead->QuadPart = totalRead;
    }

    if (pcbWritten)
    {
        pcbWritten->QuadPart = totalWritten;
    }
    return hr;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Commit(DWORD /*grfCommitFlags*/) noexcept
{
    // No-op for a memory stream.
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Revert() noexcept
{
    return E_NOTIMPL;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::LockRegion(ULARGE_INTEGER /*libOffset*/, ULARGE_INTEGER /*cb*/, DWORD /*dwLockType*/) noexcept
{
    return STG_E_INVALIDFUNCTION;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::UnlockRegion(ULARGE_INTEGER /*libOffset*/, ULARGE_INTEGER /*cb*/, DWORD /*dwLockType*/) noexcept
{
    return STG_E_INVALIDFUNCTION;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Stat(_Out_ STATSTG* pstatstg, DWORD grfStatFlag) noexcept
{
    if (pstatstg == nullptr)
    {
        return STG_E_INVALIDPOINTER;
    }

    *pstatstg = {};
    pstatstg->type = STGTY_STREAM;
    pstatstg->cbSize.QuadPart = m_buffer.size();
    pstatstg->grfMode = STGM_READWRITE;

    if (!(grfStatFlag & STATFLAG_NONAME))
    {
        // No name to report; leave pwcsName as nullptr.
    }
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Clone(_Outptr_ IStream** ppstm) noexcept
{
    if (ppstm)
    {
        *ppstm = nullptr;
    }
    return E_NOTIMPL;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Reset() noexcept
{
    m_buffer.clear();
    m_position = 0;
    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::Reserve(LONGLONG capacity) noexcept
{
    if (capacity < 0)
    {
        return E_INVALIDARG;
    }

    size_t newCapacity = 0;
    HRESULT const hr = ToBufferSize(static_cast<UINT64>(capacity), &newCapacity);
    if (FAILED(hr))
    {
        // Map STG_E_MEDIUMFULL to the more appropriate Reserve error code.
        return E_OUTOFMEMORY;
    }

    try
    {
        m_buffer.reserve(newCapacity);
    }
    catch (std::bad_alloc const&)
    {
        return E_OUTOFMEMORY;
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE
RegExMemoryStream::GetBuffer(_Out_ LONGLONG* pData, _Out_ LONGLONG* pSize) noexcept
{
    if (pData == nullptr || pSize == nullptr)
    {
        return E_POINTER;
    }

    *pData = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(m_buffer.data()));
    *pSize = static_cast<LONGLONG>(m_buffer.size());
    return S_OK;
}

HRESULT
RegExMemoryStream::ToBufferSize(UINT64 requested, _Out_ size_t* pSize) const noexcept
{
    // Cap at min(m_buffer.max_size(), INT64_MAX). The INT64_MAX cap is an
    // invariant on m_buffer.size() and m_position: keeping them within signed
    // 64-bit range guarantees that Seek's arithmetic never has to cope with
    // values that exceed what LARGE_INTEGER/INT64 can represent.
    UINT64 const cap = std::min<UINT64>(m_buffer.max_size(), static_cast<UINT64>(INT64_MAX));
    if (requested > cap)
    {
        *pSize = 0;
        return STG_E_MEDIUMFULL;
    }

    *pSize = static_cast<size_t>(requested);
    return S_OK;
}
