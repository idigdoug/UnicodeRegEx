#include "pch.h"
#include "RegExTestHelpers.h"

#include <filesystem>
#include <random>
#include <thread>
#include <atomic>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace RegExTests
{
    // RAII helper: generates a unique path under %TEMP% on construction and
    // deletes the file (if present) on destruction.
    class ScopedTempPath
    {
        std::wstring m_path;

    public:
        explicit ScopedTempPath(std::wstring_view suffix = L".dat")
        {
            wchar_t tempDir[MAX_PATH] = {};
            DWORD const len = GetTempPathW(MAX_PATH, tempDir);
            Assert::IsTrue(len > 0 && len < MAX_PATH, L"GetTempPathW failed");

            // Combine PID + tick count + a random uint64 for uniqueness.
            std::random_device rd;
            UINT64 const r = (static_cast<UINT64>(rd()) << 32) | rd();
            wchar_t name[64];
            swprintf_s(name, L"UnicodeRegExTest_%lu_%llX%.*ls",
                GetCurrentProcessId(),
                static_cast<unsigned long long>(r),
                static_cast<int>(suffix.size()),
                suffix.data());

            m_path = tempDir;
            m_path += name;
        }

        ~ScopedTempPath() noexcept
        {
            std::error_code ec;
            std::filesystem::remove(m_path, ec);
        }

        ScopedTempPath(ScopedTempPath const&) = delete;
        ScopedTempPath& operator=(ScopedTempPath const&) = delete;

        std::wstring const& Path() const noexcept { return m_path; }
        wchar_t const* c_str() const noexcept { return m_path.c_str(); }
        wil::unique_bstr Bstr() const { return wil::unique_bstr(SysAllocStringLen(m_path.data(), static_cast<UINT>(m_path.size()))); }
    };

    // Creates a file stream at the given path with the given flags.
    inline wil::com_ptr<IRegExFileStream>
    MakeFileStream(std::wstring_view path, RegExFileStreamFlags flags)
    {
        wil::unique_bstr pathBstr(SysAllocStringLen(path.data(), static_cast<UINT>(path.size())));
        wil::com_ptr<IRegExFileStream> stream;
        HRESULT hr = GetLibrary()->CreateFileStream(pathBstr.get(), flags, stream.put());
        Assert::AreEqual(S_OK, hr, L"MakeFileStream: CreateFileStream failed");
        Assert::IsNotNull(stream.get(), L"MakeFileStream: stream is null");
        return stream;
    }

    // Writes the given bytes to a file and returns its path; the file is
    // deleted when the ScopedTempPath goes out of scope.
    inline void
    WriteToDisk(std::wstring const& path, std::span<BYTE const> data)
    {
        wil::unique_hfile h(CreateFileW(
            path.c_str(),
            GENERIC_WRITE,
            0,
            nullptr,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr));
        Assert::IsTrue(h.is_valid(), L"WriteToDisk: CreateFileW failed");

        DWORD written = 0;
        BOOL const ok = WriteFile(h.get(), data.data(), static_cast<DWORD>(data.size()), &written, nullptr);
        Assert::IsTrue(ok && written == data.size(), L"WriteToDisk: WriteFile failed or short");
    }

    // Reads the entire file at path into a vector.
    inline std::vector<BYTE>
    ReadFromDisk(std::wstring const& path)
    {
        // Share modes are reciprocal: open in a way compatible with a
        // possibly-still-open RegExFileStream handle (READ|WRITE|DELETE access,
        // READ|DELETE share).
        wil::unique_hfile h(CreateFileW(
            path.c_str(),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            nullptr));
        Assert::IsTrue(h.is_valid(), (L"ReadFromDisk: CreateFileW failed " + std::to_wstring(GetLastError())).c_str());

        LARGE_INTEGER size{};
        Assert::IsTrue(GetFileSizeEx(h.get(), &size) != FALSE, L"ReadFromDisk: GetFileSizeEx failed");

        std::vector<BYTE> buf(static_cast<size_t>(size.QuadPart));
        if (!buf.empty())
        {
            DWORD read = 0;
            Assert::IsTrue(
                ReadFile(h.get(), buf.data(), static_cast<DWORD>(buf.size()), &read, nullptr) != FALSE,
                L"ReadFromDisk: ReadFile failed");
            Assert::AreEqual(buf.size(), static_cast<size_t>(read), L"ReadFromDisk: short read");
        }
        return buf;
    }

    // ------------------------------------------------------------------
    // CreateFileStream factory validation.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamCreateTests)
    {
    public:

        TEST_METHOD(Create_NullOutPointer_ReturnsPointer)
        {
            ScopedTempPath tmp;
            Assert::AreEqual(E_POINTER,
                GetLibrary()->CreateFileStream(tmp.Bstr().get(), RegExFileStreamFlag_create_always, nullptr));
        }

        TEST_METHOD(Create_EmptyPath_ReturnsInvalidArg)
        {
            wil::unique_bstr emptyPath(SysAllocString(L""));
            wil::com_ptr<IRegExFileStream> stream;
            Assert::AreEqual(E_INVALIDARG,
                GetLibrary()->CreateFileStream(emptyPath.get(), RegExFileStreamFlag_create_always, stream.put()));
            Assert::IsNull(stream.get());
        }

        TEST_METHOD(Create_NullPath_ReturnsInvalidArg)
        {
            wil::com_ptr<IRegExFileStream> stream;
            Assert::AreEqual(E_INVALIDARG,
                GetLibrary()->CreateFileStream(nullptr, RegExFileStreamFlag_create_always, stream.put()));
            Assert::IsNull(stream.get());
        }

        TEST_METHOD(Create_OpenExistingMissing_ReturnsFileNotFound)
        {
            ScopedTempPath tmp;
            wil::unique_bstr pathBstr = tmp.Bstr();
            wil::com_ptr<IRegExFileStream> stream;
            HRESULT hr = GetLibrary()->CreateFileStream(pathBstr.get(), RegExFileStreamFlag_open_existing, stream.put());
            Assert::AreEqual(HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND), hr);
            Assert::IsNull(stream.get());
        }

        TEST_METHOD(Create_CreateNewOnExisting_ReturnsFileExists)
        {
            ScopedTempPath tmp;
            WriteToDisk(tmp.Path(), std::span<BYTE const>{});

            wil::com_ptr<IRegExFileStream> stream;
            HRESULT hr = GetLibrary()->CreateFileStream(tmp.Bstr().get(), RegExFileStreamFlag_create_new, stream.put());
            Assert::AreEqual(HRESULT_FROM_WIN32(ERROR_FILE_EXISTS), hr);
            Assert::IsNull(stream.get());
        }

        TEST_METHOD(Create_CreateAlways_TruncatesExisting)
        {
            ScopedTempPath tmp;
            BYTE const original[] = { 1, 2, 3, 4, 5 };
            WriteToDisk(tmp.Path(), std::span<BYTE const>(original));

            {
                auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_create_always);
                // Stream goes out of scope, flushes and closes.
            }

            auto bytes = ReadFromDisk(tmp.Path());
            Assert::AreEqual(size_t(0), bytes.size());
        }

        TEST_METHOD(Create_OpenOrCreate_CreatesNew)
        {
            ScopedTempPath tmp;
            {
                auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_open_or_create);
            }
            std::error_code ec;
            Assert::IsTrue(std::filesystem::exists(tmp.Path(), ec));
        }

        TEST_METHOD(Create_DeleteOnClose_RemovesFile)
        {
            ScopedTempPath tmp;
            {
                auto stream = MakeFileStream(
                    tmp.Path(),
                    static_cast<RegExFileStreamFlags>(
                        RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
                // Don't probe existence while open: a delete-pending file may
                // make GetFileAttributesEx return ACCESS_DENIED.
            }
            std::error_code ec;
            Assert::IsFalse(std::filesystem::exists(tmp.Path(), ec), L"File should be gone after Release");
        }
    };

    // ------------------------------------------------------------------
    // QueryInterface contract.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamQITests)
    {
    public:

        TEST_METHOD(QueryInterface_IUnknown)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            wil::com_ptr<IUnknown> unk;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(unk.put())));
            Assert::IsNotNull(unk.get());
        }

        TEST_METHOD(QueryInterface_ISequentialStream)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            wil::com_ptr<ISequentialStream> seq;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(seq.put())));
            Assert::IsNotNull(seq.get());
        }

        TEST_METHOD(QueryInterface_IStream)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            wil::com_ptr<IStream> s;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(s.put())));
            Assert::IsNotNull(s.get());
        }

        TEST_METHOD(QueryInterface_IRegExFileStream)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            wil::com_ptr<IRegExFileStream> fs;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(fs.put())));
            Assert::IsNotNull(fs.get());
        }

        TEST_METHOD(QueryInterface_IMarshal_Succeeds)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            wil::com_ptr<IMarshal> marshal;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(marshal.put())));
            Assert::IsNotNull(marshal.get());
        }

        TEST_METHOD(QueryInterface_Unknown_ReturnsNoInterface)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            wil::com_ptr<IDispatch> disp;
            Assert::AreEqual(E_NOINTERFACE, stream->QueryInterface(IID_PPV_ARGS(disp.put())));
            Assert::IsNull(disp.get());
        }

        TEST_METHOD(QueryInterface_NullOut_ReturnsPointer)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            Assert::AreEqual(E_POINTER, stream->QueryInterface(IID_IUnknown, nullptr));
        }
    };

    // ------------------------------------------------------------------
    // Read / Write round-trips.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamReadWriteTests)
    {
    public:

        TEST_METHOD(Write_PersistsAfterClose)
        {
            ScopedTempPath tmp;
            {
                auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_create_new);
                ULONG written = 0;
                Assert::AreEqual(S_OK, stream->Write("hello world", 11, &written));
                Assert::AreEqual(ULONG(11), written);
            }

            auto bytes = ReadFromDisk(tmp.Path());
            Assert::AreEqual(size_t(11), bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), "hello world", 11));
        }

        TEST_METHOD(Write_NullPv_ZeroCb_Succeeds)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            ULONG written = 99;
            Assert::AreEqual(S_OK, stream->Write(nullptr, 0, &written));
            Assert::AreEqual(ULONG(0), written);
        }

        TEST_METHOD(Write_NullPv_NonZeroCb_ReturnsInvalidPointer)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            ULONG written = 99;
            Assert::AreEqual(STG_E_INVALIDPOINTER, stream->Write(nullptr, 5, &written));
            Assert::AreEqual(ULONG(0), written);
        }

        TEST_METHOD(Write_LargePayload_BypassesBuffer)
        {
            // Payload >= WriteBufferCapacity (64 KiB) takes the direct-write
            // path; ensure it still produces correct file contents.
            ScopedTempPath tmp;
            constexpr size_t LargeSize = 128 * 1024;
            std::vector<BYTE> payload(LargeSize);
            for (size_t i = 0; i < LargeSize; ++i)
            {
                payload[i] = static_cast<BYTE>(i & 0xFF);
            }

            {
                auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_create_new);
                ULONG written = 0;
                Assert::AreEqual(S_OK, stream->Write(payload.data(), static_cast<ULONG>(payload.size()), &written));
                Assert::AreEqual(ULONG(LargeSize), written);
            }

            auto bytes = ReadFromDisk(tmp.Path());
            Assert::AreEqual(LargeSize, bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), payload.data(), LargeSize));
        }

        TEST_METHOD(Read_PrePopulatedFile_ReturnsContents)
        {
            ScopedTempPath tmp;
            BYTE const content[] = { 'a', 'b', 'c', 'd', 'e' };
            WriteToDisk(tmp.Path(), std::span<BYTE const>(content));

            auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_open_existing);
            BYTE buf[16] = {};
            ULONG read = 0;
            Assert::AreEqual(S_OK, stream->Read(buf, sizeof(buf), &read));
            Assert::AreEqual(ULONG(5), read);
            Assert::AreEqual(0, memcmp(buf, content, 5));
        }

        TEST_METHOD(Read_NullPv_NonZeroCb_ReturnsInvalidPointer)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            ULONG read = 99;
            Assert::AreEqual(STG_E_INVALIDPOINTER, stream->Read(nullptr, 5, &read));
            Assert::AreEqual(ULONG(0), read);
        }

        TEST_METHOD(Read_AtEnd_ReturnsZeroBytes)
        {
            ScopedTempPath tmp;
            BYTE const content[] = { 'x', 'y' };
            WriteToDisk(tmp.Path(), std::span<BYTE const>(content));

            auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_open_existing);
            // Seek to end.
            LARGE_INTEGER zero{};
            stream->Seek(zero, STREAM_SEEK_END, nullptr);

            BYTE buf[16] = {};
            ULONG read = 999;
            Assert::AreEqual(S_OK, stream->Read(buf, sizeof(buf), &read));
            Assert::AreEqual(ULONG(0), read);
        }

        TEST_METHOD(Read_FlushesBufferedWritesFirst)
        {
            // Read should observe data that was just Write()n in the same stream.
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));

            stream->Write("hello", 5, nullptr);

            // Seek back and read.
            LARGE_INTEGER zero{};
            stream->Seek(zero, STREAM_SEEK_SET, nullptr);
            BYTE buf[16] = {};
            ULONG read = 0;
            Assert::AreEqual(S_OK, stream->Read(buf, sizeof(buf), &read));
            Assert::AreEqual(ULONG(5), read);
            Assert::AreEqual(0, memcmp(buf, "hello", 5));
        }
    };

    // ------------------------------------------------------------------
    // Seek.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamSeekTests)
    {
    public:

        TEST_METHOD(Seek_Set_FromZero)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Write("0123456789", 10, nullptr);

            LARGE_INTEGER three{};
            three.QuadPart = 3;
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(three, STREAM_SEEK_SET, &pos));
            Assert::AreEqual(UINT64(3), pos.QuadPart);
        }

        TEST_METHOD(Seek_Cur_QueryPosition)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Write("hi", 2, nullptr);

            LARGE_INTEGER zero{};
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(zero, STREAM_SEEK_CUR, &pos));
            // Position is at end after the write (whether or not the buffer is flushed).
            Assert::AreEqual(UINT64(2), pos.QuadPart);
        }

        TEST_METHOD(Seek_End_Zero_ReturnsLogicalEnd)
        {
            ScopedTempPath tmp;
            BYTE const content[] = { 1, 2, 3, 4, 5, 6, 7 };
            WriteToDisk(tmp.Path(), std::span<BYTE const>(content));

            auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_open_existing);
            LARGE_INTEGER zero{};
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(zero, STREAM_SEEK_END, &pos));
            Assert::AreEqual(UINT64(7), pos.QuadPart);
        }

        TEST_METHOD(Seek_InvalidOrigin_Fails)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            LARGE_INTEGER zero{};
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->Seek(zero, 999, nullptr));
        }

        TEST_METHOD(Seek_NullOutPosition_Allowed)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            LARGE_INTEGER zero{};
            Assert::AreEqual(S_OK, stream->Seek(zero, STREAM_SEEK_SET, nullptr));
        }
    };

    // ------------------------------------------------------------------
    // SetSize / Stat.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamSetSizeStatTests)
    {
    public:

        TEST_METHOD(SetSize_Grows_FileGrows)
        {
            ScopedTempPath tmp;
            {
                auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_create_new);
                ULARGE_INTEGER size{};
                size.QuadPart = 100;
                Assert::AreEqual(S_OK, stream->SetSize(size));
            }
            Assert::AreEqual(uintmax_t(100), std::filesystem::file_size(tmp.Path()));
        }

        TEST_METHOD(SetSize_Shrinks_FileShrinks)
        {
            ScopedTempPath tmp;
            std::vector<BYTE> large(1024, BYTE('A'));
            WriteToDisk(tmp.Path(), std::span<BYTE const>(large));

            {
                auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_open_existing);
                ULARGE_INTEGER size{};
                size.QuadPart = 10;
                Assert::AreEqual(S_OK, stream->SetSize(size));
            }

            auto bytes = ReadFromDisk(tmp.Path());
            Assert::AreEqual(size_t(10), bytes.size());
        }

        TEST_METHOD(Stat_NullOut_ReturnsInvalidPointer)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            Assert::AreEqual(STG_E_INVALIDPOINTER, stream->Stat(nullptr, STATFLAG_NONAME));
        }

        TEST_METHOD(Stat_ReportsTypeStreamAndSize)
        {
            ScopedTempPath tmp;
            BYTE const content[] = { 'x', 'y', 'z' };
            WriteToDisk(tmp.Path(), std::span<BYTE const>(content));

            auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_open_existing);
            STATSTG stat{};
            Assert::AreEqual(S_OK, stream->Stat(&stat, STATFLAG_NONAME));
            Assert::AreEqual(DWORD(STGTY_STREAM), DWORD(stat.type));
            Assert::AreEqual(UINT64(3), stat.cbSize.QuadPart);
        }

        TEST_METHOD(Stat_IncludesBufferedWrites)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));

            // Small write that stays in the buffer (does not hit disk yet).
            stream->Write("abc", 3, nullptr);

            STATSTG stat{};
            Assert::AreEqual(S_OK, stream->Stat(&stat, STATFLAG_NONAME));
            // Size = on-disk size (0) + buffered (3).
            Assert::AreEqual(UINT64(3), stat.cbSize.QuadPart);
        }
    };

    // ------------------------------------------------------------------
    // Flush / Commit.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamFlushTests)
    {
    public:

        TEST_METHOD(Flush_PersistsBufferedWrites)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_create_new);
            stream->Write("hello", 5, nullptr);

            Assert::AreEqual(S_OK, stream->Flush());

            // Re-open with another handle and read the contents.
            auto bytes = ReadFromDisk(tmp.Path());
            Assert::AreEqual(size_t(5), bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), "hello", 5));
        }

        TEST_METHOD(Commit_PersistsBufferedWrites)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(tmp.Path(), RegExFileStreamFlag_create_new);
            stream->Write("commit-test", 11, nullptr);

            Assert::AreEqual(S_OK, stream->Commit(STGC_DEFAULT));

            auto bytes = ReadFromDisk(tmp.Path());
            Assert::AreEqual(size_t(11), bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), "commit-test", 11));
        }
    };

    // ------------------------------------------------------------------
    // CopyTo / Revert / LockRegion / UnlockRegion / Clone.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamMiscTests)
    {
    public:

        TEST_METHOD(CopyTo_NotImplemented)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            ULARGE_INTEGER cb{};
            cb.QuadPart = 1;
            ULARGE_INTEGER read{}, written{};
            Assert::AreEqual(E_NOTIMPL, stream->CopyTo(nullptr, cb, &read, &written));
            Assert::AreEqual(UINT64(0), read.QuadPart);
            Assert::AreEqual(UINT64(0), written.QuadPart);
        }

        TEST_METHOD(Revert_NotImplemented)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            Assert::AreEqual(E_NOTIMPL, stream->Revert());
        }

        TEST_METHOD(LockRegion_InvalidFunction)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            ULARGE_INTEGER off{}, cb{};
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->LockRegion(off, cb, LOCK_EXCLUSIVE));
        }

        TEST_METHOD(UnlockRegion_InvalidFunction)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            ULARGE_INTEGER off{}, cb{};
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->UnlockRegion(off, cb, LOCK_EXCLUSIVE));
        }

        TEST_METHOD(Clone_NotImplemented_ClearsOutPointer)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            IStream* raw = reinterpret_cast<IStream*>(static_cast<INT_PTR>(0xDEADBEEF));
            Assert::AreEqual(E_NOTIMPL, stream->Clone(&raw));
            Assert::IsNull(raw);
        }
    };

    // ------------------------------------------------------------------
    // Path property and initial CancelStatus.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamPropertiesTests)
    {
    public:

        TEST_METHOD(Path_ReturnsOriginalPath)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            wil::unique_bstr path;
            Assert::AreEqual(S_OK, stream->get_Path(path.put()));
            Assert::AreEqual(tmp.Path(), std::wstring(path.get(), SysStringLen(path.get())));
        }

        TEST_METHOD(Path_NullOut_ReturnsPointer)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            Assert::AreEqual(E_POINTER, stream->get_Path(nullptr));
        }

        TEST_METHOD(CancelStatus_InitiallyRunning)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            RegExStreamCancelStatus status = RegExStreamCancelStatus_cancelled;
            Assert::AreEqual(S_OK, stream->get_CancelStatus(&status));
            Assert::AreEqual((int)RegExStreamCancelStatus_running, (int)status);
        }

        TEST_METHOD(CancelStatus_NullOut_ReturnsPointer)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            Assert::AreEqual(E_POINTER, stream->get_CancelStatus(nullptr));
        }
    };

    // ------------------------------------------------------------------
    // Cancel / WaitForCancelled.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamCancelTests)
    {
    public:

        TEST_METHOD(Cancel_FromIdle_ReachesCancelled)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));

            Assert::AreEqual(S_OK, stream->Cancel());

            RegExStreamCancelStatus status = RegExStreamCancelStatus_running;
            Assert::AreEqual(S_OK, stream->get_CancelStatus(&status));
            // With no I/O in progress, Cancel completes the transition synchronously.
            Assert::AreEqual((int)RegExStreamCancelStatus_cancelled, (int)status);
        }

        TEST_METHOD(Cancel_Idempotent)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));

            Assert::AreEqual(S_OK, stream->Cancel());
            Assert::AreEqual(S_OK, stream->Cancel());

            RegExStreamCancelStatus status = RegExStreamCancelStatus_running;
            stream->get_CancelStatus(&status);
            Assert::AreEqual((int)RegExStreamCancelStatus_cancelled, (int)status);
        }

        TEST_METHOD(Write_AfterCancel_ReturnsAbort)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();

            ULONG written = 99;
            Assert::AreEqual(E_ABORT, stream->Write("x", 1, &written));
            Assert::AreEqual(ULONG(0), written);
        }

        TEST_METHOD(Read_AfterCancel_ReturnsAbort)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();

            BYTE buf[8] = {};
            ULONG read = 99;
            Assert::AreEqual(E_ABORT, stream->Read(buf, sizeof(buf), &read));
            Assert::AreEqual(ULONG(0), read);
        }

        TEST_METHOD(Seek_AfterCancel_ReturnsAbort)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();

            LARGE_INTEGER zero{};
            Assert::AreEqual(E_ABORT, stream->Seek(zero, STREAM_SEEK_SET, nullptr));
        }

        TEST_METHOD(SetSize_AfterCancel_ReturnsAbort)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();

            ULARGE_INTEGER size{};
            size.QuadPart = 10;
            Assert::AreEqual(E_ABORT, stream->SetSize(size));
        }

        TEST_METHOD(Stat_AfterCancel_ReturnsAbort)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();

            STATSTG stat{};
            Assert::AreEqual(E_ABORT, stream->Stat(&stat, STATFLAG_NONAME));
        }

        TEST_METHOD(Flush_AfterCancel_ReturnsAbort)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();
            Assert::AreEqual(E_ABORT, stream->Flush());
        }

        TEST_METHOD(WaitForCancelled_BeforeCancel_ReturnsNotValidState)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));

            VARIANT_BOOL cancelled = VARIANT_TRUE;
            Assert::AreEqual(E_NOT_VALID_STATE, stream->WaitForCancelled(0, &cancelled));
            Assert::AreEqual(VARIANT_FALSE, cancelled);
        }

        TEST_METHOD(WaitForCancelled_AfterCancel_ReturnsTrue)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();

            VARIANT_BOOL cancelled = VARIANT_FALSE;
            Assert::AreEqual(S_OK, stream->WaitForCancelled(0, &cancelled));
            Assert::AreEqual(VARIANT_TRUE, cancelled);
        }

        TEST_METHOD(WaitForCancelled_NullOut_ReturnsPointer)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            Assert::AreEqual(E_POINTER, stream->WaitForCancelled(0, nullptr));
        }

        TEST_METHOD(WaitForCancelled_DuringConcurrentIo_BlocksUntilCancelled)
        {
            // Race scenario: a worker thread does a tight loop of Write calls while
            // the main thread calls Cancel. When Cancel fires while the worker is
            // inside an IoScope, the stream is left in the 'cancelling' state and
            // WaitForCancelled must block on the cancelled event until the worker's
            // IoScope destructor finishes the transition to 'cancelled'.
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));

            std::atomic<bool> stopWorker{ false };
            std::thread worker([&]
            {
                BYTE payload[64] = { 'x' };
                ULONG written = 0;
                while (!stopWorker.load(std::memory_order_relaxed))
                {
                    HRESULT hr = stream->Write(payload, sizeof(payload), &written);
                    if (hr == E_ABORT)
                    {
                        break; // Stream has been cancelled.
                    }
                }
            });

            // Give the worker a moment to start hammering on Write.
            Sleep(10);

            Assert::AreEqual(S_OK, stream->Cancel());

            VARIANT_BOOL cancelled = VARIANT_FALSE;
            HRESULT hr = stream->WaitForCancelled(5000, &cancelled);
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(VARIANT_TRUE, cancelled);

            stopWorker.store(true, std::memory_order_relaxed);
            worker.join();

            // After WaitForCancelled returns TRUE, the stream is in the terminal
            // cancelled state and further reads should still see it.
            RegExStreamCancelStatus status = RegExStreamCancelStatus_running;
            Assert::AreEqual(S_OK, stream->get_CancelStatus(&status));
            Assert::AreEqual((int)RegExStreamCancelStatus_cancelled, (int)status);
        }

        TEST_METHOD(WaitForCancelled_MultipleWaiters_AllReleased)
        {
            // Multiple threads calling WaitForCancelled concurrently should all
            // observe the cancelled state once cancellation completes (the event
            // is a manual-reset event so it stays signalled).
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));

            std::atomic<bool> stopWorker{ false };
            std::thread worker([&]
            {
                BYTE payload[64] = { 'x' };
                ULONG written = 0;
                while (!stopWorker.load(std::memory_order_relaxed))
                {
                    HRESULT hr = stream->Write(payload, sizeof(payload), &written);
                    if (hr == E_ABORT)
                    {
                        break;
                    }
                }
            });

            Sleep(10);

            Assert::AreEqual(S_OK, stream->Cancel());

            std::atomic<int> successes{ 0 };
            std::thread waiters[3];
            for (auto& t : waiters)
            {
                t = std::thread([&]
                {
                    VARIANT_BOOL cancelled = VARIANT_FALSE;
                    if (SUCCEEDED(stream->WaitForCancelled(5000, &cancelled)) && cancelled)
                    {
                        successes.fetch_add(1, std::memory_order_relaxed);
                    }
                });
            }

            for (auto& t : waiters)
            {
                t.join();
            }

            stopWorker.store(true, std::memory_order_relaxed);
            worker.join();

            Assert::AreEqual(3, successes.load(std::memory_order_relaxed));
        }

        TEST_METHOD(WaitForCancelled_AfterCancel_Idempotent)
        {
            // Once cancelled, repeated WaitForCancelled calls keep returning TRUE.
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();

            for (int i = 0; i < 3; ++i)
            {
                VARIANT_BOOL cancelled = VARIANT_FALSE;
                Assert::AreEqual(S_OK, stream->WaitForCancelled(0, &cancelled));
                Assert::AreEqual(VARIANT_TRUE, cancelled);
            }
        }
    };

    // ------------------------------------------------------------------
    // MoveTo.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExFileStreamMoveToTests)
    {
    public:

        TEST_METHOD(MoveTo_NullDestination_ReturnsInvalidArg)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            Assert::AreEqual(E_INVALIDARG, stream->MoveTo(nullptr, RegExFileMoveFlag_default));
        }

        TEST_METHOD(MoveTo_EmptyDestination_ReturnsInvalidArg)
        {
            ScopedTempPath tmp;
            auto stream = MakeFileStream(
                tmp.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            wil::unique_bstr empty(SysAllocString(L""));
            Assert::AreEqual(E_INVALIDARG, stream->MoveTo(empty.get(), RegExFileMoveFlag_default));
        }

        TEST_METHOD(MoveTo_RenamesFileAndUpdatesPath)
        {
            ScopedTempPath src, dst;
            {
                auto stream = MakeFileStream(src.Path(), RegExFileStreamFlag_create_new);
                stream->Write("payload", 7, nullptr);

                Assert::AreEqual(S_OK, stream->MoveTo(dst.Bstr().get(), RegExFileMoveFlag_default));

                // Path property reflects the new location.
                wil::unique_bstr path;
                Assert::AreEqual(S_OK, stream->get_Path(path.put()));
                Assert::AreEqual(dst.Path(), std::wstring(path.get(), SysStringLen(path.get())));
            }

            // Source path no longer exists; destination contains the written data.
            std::error_code ec;
            Assert::IsFalse(std::filesystem::exists(src.Path(), ec));
            auto bytes = ReadFromDisk(dst.Path());
            Assert::AreEqual(size_t(7), bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), "payload", 7));
        }

        TEST_METHOD(MoveTo_OntoExisting_WithoutReplace_Fails)
        {
            ScopedTempPath src, dst;
            BYTE const existing[] = { 'O', 'L', 'D' };
            WriteToDisk(dst.Path(), std::span<BYTE const>(existing));

            {
                auto stream = MakeFileStream(src.Path(), RegExFileStreamFlag_create_new);
                stream->Write("NEW", 3, nullptr);

                HRESULT hr = stream->MoveTo(dst.Bstr().get(), RegExFileMoveFlag_default);
                Assert::AreEqual(HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS), hr);
            }

            // Existing file at destination is unchanged.
            auto bytes = ReadFromDisk(dst.Path());
            Assert::AreEqual(size_t(3), bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), "OLD", 3));
        }

        TEST_METHOD(MoveTo_OntoExisting_WithReplace_Succeeds)
        {
            ScopedTempPath src, dst;
            BYTE const existing[] = { 'O', 'L', 'D' };
            WriteToDisk(dst.Path(), std::span<BYTE const>(existing));

            {
                auto stream = MakeFileStream(src.Path(), RegExFileStreamFlag_create_new);
                stream->Write("NEWPAYLOAD", 10, nullptr);

                Assert::AreEqual(S_OK,
                    stream->MoveTo(dst.Bstr().get(), RegExFileMoveFlag_replace_existing));
            }

            auto bytes = ReadFromDisk(dst.Path());
            Assert::AreEqual(size_t(10), bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), "NEWPAYLOAD", 10));
        }

        TEST_METHOD(MoveTo_StreamWithoutDeleteOnClose_DoesNotForceShareDelete)
        {
            // A stream created without delete_on_close should not require
            // readers to specify FILE_SHARE_DELETE in order to coexist with
            // the open handle, even after MoveTo. (MoveTo opens a transient
            // side handle with DELETE access rather than carrying it on
            // m_file for the stream's lifetime.)
            ScopedTempPath src, dst;
            auto stream = MakeFileStream(src.Path(), RegExFileStreamFlag_create_new);
            stream->Write("payload", 7, nullptr);
            Assert::AreEqual(S_OK, stream->MoveTo(dst.Bstr().get(), RegExFileMoveFlag_default));

            // Stream still holds the renamed file open. A second opener with
            // GENERIC_READ and FILE_SHARE_READ | FILE_SHARE_WRITE (NO
            // FILE_SHARE_DELETE) must succeed.
            wil::unique_hfile h(CreateFileW(
                dst.c_str(),
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                nullptr,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                nullptr));
            Assert::IsTrue(h.is_valid(),
                (L"second open failed with " + std::to_wstring(GetLastError())).c_str());
        }

        TEST_METHOD(MoveTo_OntoOpenDestination_WithReplace_PosixSemantics)
        {
            // POSIX-semantics rename should succeed even when the destination
            // has another opener (with a compatible share mode). The
            // pre-existing reader continues to see the old (renamed-away)
            // inode; a fresh open of the destination path sees the new file.
            ScopedTempPath src, dst;
            BYTE const oldContent[] = { 'O', 'L', 'D' };
            WriteToDisk(dst.Path(), std::span<BYTE const>(oldContent));

            // Hold the destination open for read with FILE_SHARE_READ |
            // FILE_SHARE_WRITE | FILE_SHARE_DELETE so the rename can detach
            // this name from the inode.
            wil::unique_hfile destReader(CreateFileW(
                dst.c_str(),
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                nullptr));
            Assert::IsTrue(destReader.is_valid(),
                (L"destination open failed with " + std::to_wstring(GetLastError())).c_str());

            {
                auto stream = MakeFileStream(src.Path(), RegExFileStreamFlag_create_new);
                stream->Write("NEW", 3, nullptr);

                Assert::AreEqual(S_OK,
                    stream->MoveTo(dst.Bstr().get(), RegExFileMoveFlag_replace_existing));
            }

            // The existing reader continues to see the old content (its handle
            // refers to the renamed-away inode).
            BYTE existingReaderBuf[8] = {};
            DWORD existingRead = 0;
            Assert::IsTrue(ReadFile(destReader.get(), existingReaderBuf, sizeof(existingReaderBuf), &existingRead, nullptr) != FALSE);
            Assert::AreEqual(DWORD(3), existingRead);
            Assert::AreEqual(0, memcmp(existingReaderBuf, "OLD", 3));

            // A fresh open of the destination path sees the new content.
            auto bytes = ReadFromDisk(dst.Path());
            Assert::AreEqual(size_t(3), bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), "NEW", 3));
        }

        TEST_METHOD(MoveTo_AfterCancel_ReturnsAbort)
        {
            ScopedTempPath src, dst;
            auto stream = MakeFileStream(
                src.Path(),
                static_cast<RegExFileStreamFlags>(
                    RegExFileStreamFlag_create_new | RegExFileStreamFlag_delete_on_close));
            stream->Cancel();

            Assert::AreEqual(E_ABORT,
                stream->MoveTo(dst.Bstr().get(), RegExFileMoveFlag_default));
        }
    };

    // ------------------------------------------------------------------
    // CreateReplacementFileStream.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExReplacementFileStreamTests)
    {
    public:

        TEST_METHOD(Create_NullOutPointer_ReturnsPointer)
        {
            ScopedTempPath tmp;
            Assert::AreEqual(E_POINTER,
                GetLibrary()->CreateReplacementFileStream(tmp.Bstr().get(), nullptr));
        }

        TEST_METHOD(Create_EmptyFinalPath_ReturnsInvalidArg)
        {
            wil::unique_bstr empty(SysAllocString(L""));
            wil::com_ptr<IRegExFileStream> stream;
            Assert::AreEqual(E_INVALIDARG,
                GetLibrary()->CreateReplacementFileStream(empty.get(), stream.put()));
            Assert::IsNull(stream.get());
        }

        TEST_METHOD(Create_NullFinalPath_ReturnsInvalidArg)
        {
            wil::com_ptr<IRegExFileStream> stream;
            Assert::AreEqual(E_INVALIDARG,
                GetLibrary()->CreateReplacementFileStream(nullptr, stream.put()));
            Assert::IsNull(stream.get());
        }

        TEST_METHOD(Create_TempFilePath_DiffersFromFinalPath_AndIsAdjacent)
        {
            ScopedTempPath finalPath;

            // Track temp path so we can verify cleanup after close.
            std::wstring tempPath;
            {
                wil::com_ptr<IRegExFileStream> stream;
                Assert::AreEqual(S_OK,
                    GetLibrary()->CreateReplacementFileStream(finalPath.Bstr().get(), stream.put()));

                wil::unique_bstr path;
                Assert::AreEqual(S_OK, stream->get_Path(path.put()));
                tempPath.assign(path.get(), SysStringLen(path.get()));

                Assert::AreNotEqual(finalPath.Path(), tempPath, L"temp path must differ from final path");

                // Temp file should live in the same directory as the final path.
                Assert::AreEqual(
                    std::filesystem::path(finalPath.Path()).parent_path().wstring(),
                    std::filesystem::path(tempPath).parent_path().wstring());
            }

            // After Release, delete-on-close removes the temp file and the
            // final path was never created.
            std::error_code ec;
            Assert::IsFalse(std::filesystem::exists(tempPath, ec), L"temp file should be cleaned up");
            Assert::IsFalse(std::filesystem::exists(finalPath.Path(), ec), L"final path was not committed; should not exist");
        }

        TEST_METHOD(Commit_ViaMoveTo_AtomicallyReplacesFinalPath)
        {
            ScopedTempPath finalPath;
            std::wstring tempPath;
            {
                wil::com_ptr<IRegExFileStream> stream;
                Assert::AreEqual(S_OK,
                    GetLibrary()->CreateReplacementFileStream(finalPath.Bstr().get(), stream.put()));

                wil::unique_bstr path;
                stream->get_Path(path.put());
                tempPath.assign(path.get(), SysStringLen(path.get()));

                stream->Write("committed-payload", 17, nullptr);

                Assert::AreEqual(S_OK,
                    stream->MoveTo(finalPath.Bstr().get(), RegExFileMoveFlag_replace_existing));
            }

            // After MoveTo + Release: the temp path is gone, the final path
            // exists with our content.
            std::error_code ec;
            Assert::IsFalse(std::filesystem::exists(tempPath, ec));
            Assert::IsTrue(std::filesystem::exists(finalPath.Path(), ec));
            auto bytes = ReadFromDisk(finalPath.Path());
            Assert::AreEqual(size_t(17), bytes.size());
            Assert::AreEqual(0, memcmp(bytes.data(), "committed-payload", 17));
        }

        TEST_METHOD(Abandon_WithoutMoveTo_DoesNotCreateFinalPath)
        {
            ScopedTempPath finalPath;
            {
                wil::com_ptr<IRegExFileStream> stream;
                Assert::AreEqual(S_OK,
                    GetLibrary()->CreateReplacementFileStream(finalPath.Bstr().get(), stream.put()));
                stream->Write("partial", 7, nullptr);
                // Release without MoveTo: delete_on_close drops the temp file.
            }
            std::error_code ec;
            Assert::IsFalse(std::filesystem::exists(finalPath.Path(), ec));
        }
    };
}
