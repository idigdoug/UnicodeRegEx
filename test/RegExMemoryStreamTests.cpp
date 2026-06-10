#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace RegExTests
{
    // ------------------------------------------------------------------
    // Factory + initial state.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamCreateTests)
    {
    public:

        TEST_METHOD(Create_NullOutPointer_ReturnsPointer)
        {
            Assert::AreEqual(E_POINTER, GetLibrary()->CreateMemoryStream(0, nullptr));
        }

        TEST_METHOD(Create_ZeroCapacity_Succeeds)
        {
            wil::com_ptr<IRegExMemoryStream> stream;
            Assert::AreEqual(S_OK, GetLibrary()->CreateMemoryStream(0, stream.put()));
            Assert::IsNotNull(stream.get());
            Assert::AreEqual(size_t(0), StreamBytes(stream.get()).size());
        }

        TEST_METHOD(Create_NonZeroCapacity_DoesNotAffectLogicalSize)
        {
            wil::com_ptr<IRegExMemoryStream> stream;
            Assert::AreEqual(S_OK, GetLibrary()->CreateMemoryStream(1024, stream.put()));
            // Capacity hint must not change the logical size.
            Assert::AreEqual(size_t(0), StreamBytes(stream.get()).size());
        }

        TEST_METHOD(Create_NegativeCapacity_ReturnsInvalidArg)
        {
            wil::com_ptr<IRegExMemoryStream> stream;
            Assert::AreEqual(E_INVALIDARG, GetLibrary()->CreateMemoryStream(-1, stream.put()));
            Assert::IsNull(stream.get());
        }

        TEST_METHOD(InitialPosition_IsZero)
        {
            auto stream = MakeMemoryStream();
            LARGE_INTEGER zero{};
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(zero, STREAM_SEEK_CUR, &pos));
            Assert::AreEqual(UINT64(0), pos.QuadPart);
        }
    };

    // ------------------------------------------------------------------
    // QueryInterface contract.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamQITests)
    {
    public:

        TEST_METHOD(QueryInterface_IUnknown)
        {
            auto stream = MakeMemoryStream();
            wil::com_ptr<IUnknown> unk;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(unk.put())));
            Assert::IsNotNull(unk.get());
        }

        TEST_METHOD(QueryInterface_ISequentialStream)
        {
            auto stream = MakeMemoryStream();
            wil::com_ptr<ISequentialStream> seq;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(seq.put())));
            Assert::IsNotNull(seq.get());
        }

        TEST_METHOD(QueryInterface_IStream)
        {
            auto stream = MakeMemoryStream();
            wil::com_ptr<IStream> s;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(s.put())));
            Assert::IsNotNull(s.get());
        }

        TEST_METHOD(QueryInterface_IRegExMemoryStream)
        {
            auto stream = MakeMemoryStream();
            wil::com_ptr<IRegExMemoryStream> ms;
            Assert::AreEqual(S_OK, stream->QueryInterface(IID_PPV_ARGS(ms.put())));
            Assert::IsNotNull(ms.get());
        }

        TEST_METHOD(QueryInterface_Unknown_ReturnsNoInterface)
        {
            auto stream = MakeMemoryStream();
            wil::com_ptr<IDispatch> disp;
            HRESULT hr = stream->QueryInterface(IID_PPV_ARGS(disp.put()));
            Assert::AreEqual(E_NOINTERFACE, hr);
            Assert::IsNull(disp.get());
        }

        TEST_METHOD(QueryInterface_NullOut_ReturnsPointer)
        {
            auto stream = MakeMemoryStream();
            Assert::AreEqual(E_POINTER, stream->QueryInterface(IID_IUnknown, nullptr));
        }

        TEST_METHOD(RefCounting)
        {
            auto stream = MakeMemoryStream();
            IRegExMemoryStream* raw = stream.get();
            ULONG ref = raw->AddRef();
            Assert::IsTrue(ref >= 2);
            raw->Release();
            // final release happens when `stream` goes out of scope
        }
    };

    // ------------------------------------------------------------------
    // Read / Write (ISequentialStream surface).
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamReadWriteTests)
    {
    public:

        TEST_METHOD(Write_AppendsBytes)
        {
            auto stream = MakeMemoryStream();
            ULONG written = 0;
            Assert::AreEqual(S_OK, stream->Write("hello", 5, &written));
            Assert::AreEqual(ULONG(5), written);
            Assert::AreEqual("hello"sv, StreamView(stream.get()));
        }

        TEST_METHOD(Write_NullPcbWritten_Allowed)
        {
            auto stream = MakeMemoryStream();
            Assert::AreEqual(S_OK, stream->Write("hi", 2, nullptr));
            Assert::AreEqual("hi"sv, StreamView(stream.get()));
        }

        TEST_METHOD(Write_ZeroBytes_NullBuffer_Succeeds)
        {
            auto stream = MakeMemoryStream();
            ULONG written = 123;
            Assert::AreEqual(S_OK, stream->Write(nullptr, 0, &written));
            Assert::AreEqual(ULONG(0), written);
            Assert::AreEqual(size_t(0), StreamBytes(stream.get()).size());
        }

        TEST_METHOD(Write_NullBufferWithNonZeroCount_ReturnsInvalidPointer)
        {
            auto stream = MakeMemoryStream();
            ULONG written = 123;
            Assert::AreEqual(STG_E_INVALIDPOINTER, stream->Write(nullptr, 5, &written));
            // pcbWritten contract is not specified on this error path; just ensure stream is empty.
            Assert::AreEqual(size_t(0), StreamBytes(stream.get()).size());
        }

        TEST_METHOD(Write_MultipleAppends_Concatenate)
        {
            auto stream = MakeMemoryStream();
            Assert::AreEqual(S_OK, stream->Write("hello ", 6, nullptr));
            Assert::AreEqual(S_OK, stream->Write("world", 5, nullptr));
            Assert::AreEqual("hello world"sv, StreamView(stream.get()));
        }

        TEST_METHOD(Read_NullPv_NonZeroCount_ReturnsInvalidPointer)
        {
            auto stream = MakeMemoryStream();
            Assert::AreEqual(S_OK, stream->Write("hi", 2, nullptr));
            LARGE_INTEGER zero{};
            stream->Seek(zero, STREAM_SEEK_SET, nullptr);

            ULONG read = 999;
            Assert::AreEqual(STG_E_INVALIDPOINTER, stream->Read(nullptr, 5, &read));
        }

        TEST_METHOD(Read_NullPv_ZeroCount_Succeeds)
        {
            auto stream = MakeMemoryStream();
            ULONG read = 999;
            Assert::AreEqual(S_OK, stream->Read(nullptr, 0, &read));
            Assert::AreEqual(ULONG(0), read);
        }

        TEST_METHOD(Read_FromEmptyStream_ReturnsZeroBytes)
        {
            auto stream = MakeMemoryStream();
            BYTE buf[8] = {};
            ULONG read = 999;
            Assert::AreEqual(S_OK, stream->Read(buf, sizeof(buf), &read));
            Assert::AreEqual(ULONG(0), read);
        }

        TEST_METHOD(Read_AfterWrite_RequiresSeekToZero)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            // Read at end returns 0 bytes.
            BYTE buf[16] = {};
            ULONG read = 999;
            Assert::AreEqual(S_OK, stream->Read(buf, sizeof(buf), &read));
            Assert::AreEqual(ULONG(0), read);

            // Seek to start, then read.
            LARGE_INTEGER zero{};
            stream->Seek(zero, STREAM_SEEK_SET, nullptr);
            Assert::AreEqual(S_OK, stream->Read(buf, sizeof(buf), &read));
            Assert::AreEqual(ULONG(5), read);
            Assert::AreEqual(0, memcmp(buf, "hello", 5));
        }

        TEST_METHOD(Read_PartialAtEnd_ReturnsAvailableOnly)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            // Position at byte 3; ask for 10, get 2.
            LARGE_INTEGER three{};
            three.QuadPart = 3;
            stream->Seek(three, STREAM_SEEK_SET, nullptr);

            BYTE buf[10] = {};
            ULONG read = 0;
            Assert::AreEqual(S_OK, stream->Read(buf, sizeof(buf), &read));
            Assert::AreEqual(ULONG(2), read);
            Assert::AreEqual(BYTE('l'), buf[0]);
            Assert::AreEqual(BYTE('o'), buf[1]);
        }

        TEST_METHOD(Read_NullPcbRead_Allowed)
        {
            auto stream = MakeMemoryStream();
            stream->Write("abc", 3, nullptr);
            LARGE_INTEGER zero{};
            stream->Seek(zero, STREAM_SEEK_SET, nullptr);

            BYTE buf[8] = {};
            Assert::AreEqual(S_OK, stream->Read(buf, sizeof(buf), nullptr));
            Assert::AreEqual(0, memcmp(buf, "abc", 3));
        }

        TEST_METHOD(Write_OverwritesAtCurrentPosition)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello world", 11, nullptr);

            LARGE_INTEGER six{};
            six.QuadPart = 6;
            stream->Seek(six, STREAM_SEEK_SET, nullptr);
            stream->Write("WORLD", 5, nullptr);

            Assert::AreEqual("hello WORLD"sv, StreamView(stream.get()));
        }

        TEST_METHOD(Write_ExtendsBufferPastEnd)
        {
            auto stream = MakeMemoryStream();
            stream->Write("ab", 2, nullptr);

            LARGE_INTEGER five{};
            five.QuadPart = 5;
            stream->Seek(five, STREAM_SEEK_SET, nullptr);
            stream->Write("Z", 1, nullptr);

            auto bytes = StreamBytes(stream.get());
            Assert::AreEqual(size_t(6), bytes.size());
            Assert::AreEqual(BYTE('a'), bytes[0]);
            Assert::AreEqual(BYTE('b'), bytes[1]);
            // bytes 2..4 are the gap; their value is unspecified (zero-init from vector::resize).
            Assert::AreEqual(BYTE('Z'), bytes[5]);
        }
    };

    // ------------------------------------------------------------------
    // Seek.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamSeekTests)
    {
    public:

        TEST_METHOD(Seek_Set_FromZero)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            LARGE_INTEGER move{};
            move.QuadPart = 2;
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(move, STREAM_SEEK_SET, &pos));
            Assert::AreEqual(UINT64(2), pos.QuadPart);
        }

        TEST_METHOD(Seek_Cur_Forward)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            LARGE_INTEGER zero{};
            stream->Seek(zero, STREAM_SEEK_SET, nullptr);
            LARGE_INTEGER plus3{};
            plus3.QuadPart = 3;
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(plus3, STREAM_SEEK_CUR, &pos));
            Assert::AreEqual(UINT64(3), pos.QuadPart);
        }

        TEST_METHOD(Seek_Cur_Backward)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr); // position now 5

            LARGE_INTEGER minus2{};
            minus2.QuadPart = -2;
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(minus2, STREAM_SEEK_CUR, &pos));
            Assert::AreEqual(UINT64(3), pos.QuadPart);
        }

        TEST_METHOD(Seek_End_Zero_ReturnsSize)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            LARGE_INTEGER zero{};
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(zero, STREAM_SEEK_END, &pos));
            Assert::AreEqual(UINT64(5), pos.QuadPart);
        }

        TEST_METHOD(Seek_End_Negative_BeforeEnd)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            LARGE_INTEGER minus2{};
            minus2.QuadPart = -2;
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(minus2, STREAM_SEEK_END, &pos));
            Assert::AreEqual(UINT64(3), pos.QuadPart);
        }

        TEST_METHOD(Seek_PastEnd_Allowed)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hi", 2, nullptr);

            // IStream-on-HGLOBAL semantics: seek past end is allowed; subsequent
            // write fills the gap.
            LARGE_INTEGER ten{};
            ten.QuadPart = 10;
            ULARGE_INTEGER pos{};
            Assert::AreEqual(S_OK, stream->Seek(ten, STREAM_SEEK_SET, &pos));
            Assert::AreEqual(UINT64(10), pos.QuadPart);
            // Stream size is unchanged until the next write.
            Assert::AreEqual(size_t(2), StreamBytes(stream.get()).size());
        }

        TEST_METHOD(Seek_NegativeResult_Fails)
        {
            auto stream = MakeMemoryStream();
            LARGE_INTEGER minus1{};
            minus1.QuadPart = -1;
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->Seek(minus1, STREAM_SEEK_SET, nullptr));
        }

        TEST_METHOD(Seek_CurUnderflow_Fails)
        {
            auto stream = MakeMemoryStream();
            // Position is 0; subtracting 1 should fail.
            LARGE_INTEGER minus1{};
            minus1.QuadPart = -1;
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->Seek(minus1, STREAM_SEEK_CUR, nullptr));
        }

        TEST_METHOD(Seek_Overflow_Fails)
        {
            auto stream = MakeMemoryStream();
            stream->Write("x", 1, nullptr); // position 1

            // base (1) + INT64_MAX overflows.
            LARGE_INTEGER huge{};
            huge.QuadPart = INT64_MAX;
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->Seek(huge, STREAM_SEEK_CUR, nullptr));
        }

        TEST_METHOD(Seek_InvalidOrigin_Fails)
        {
            auto stream = MakeMemoryStream();
            LARGE_INTEGER zero{};
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->Seek(zero, 999, nullptr));
        }

        TEST_METHOD(Seek_NullOutPosition_Allowed)
        {
            auto stream = MakeMemoryStream();
            LARGE_INTEGER zero{};
            Assert::AreEqual(S_OK, stream->Seek(zero, STREAM_SEEK_SET, nullptr));
        }
    };

    // ------------------------------------------------------------------
    // SetSize.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamSetSizeTests)
    {
    public:

        TEST_METHOD(SetSize_GrowsBuffer)
        {
            auto stream = MakeMemoryStream();
            ULARGE_INTEGER ten{};
            ten.QuadPart = 10;
            Assert::AreEqual(S_OK, stream->SetSize(ten));
            Assert::AreEqual(size_t(10), StreamBytes(stream.get()).size());
        }

        TEST_METHOD(SetSize_ShrinksBuffer)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello world", 11, nullptr);

            ULARGE_INTEGER five{};
            five.QuadPart = 5;
            Assert::AreEqual(S_OK, stream->SetSize(five));
            Assert::AreEqual("hello"sv, StreamView(stream.get()));
        }

        TEST_METHOD(SetSize_ToZero_Empties)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            ULARGE_INTEGER zero{};
            Assert::AreEqual(S_OK, stream->SetSize(zero));
            Assert::AreEqual(size_t(0), StreamBytes(stream.get()).size());
        }

        TEST_METHOD(SetSize_DoesNotMoveCurrentPosition)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr); // position 5

            ULARGE_INTEGER twenty{};
            twenty.QuadPart = 20;
            Assert::AreEqual(S_OK, stream->SetSize(twenty));

            LARGE_INTEGER zero{};
            ULARGE_INTEGER pos{};
            stream->Seek(zero, STREAM_SEEK_CUR, &pos);
            Assert::AreEqual(UINT64(5), pos.QuadPart);
        }

        TEST_METHOD(SetSize_Excessive_ReturnsMediumFull)
        {
            auto stream = MakeMemoryStream();
            // INT64_MAX + 1 exceeds the cap (min(max_size(), INT64_MAX)).
            ULARGE_INTEGER tooBig{};
            tooBig.QuadPart = static_cast<UINT64>(INT64_MAX) + 1;
            Assert::AreEqual(STG_E_MEDIUMFULL, stream->SetSize(tooBig));
        }
    };

    // ------------------------------------------------------------------
    // CopyTo.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamCopyToTests)
    {
    public:

        TEST_METHOD(CopyTo_NullDestination_ReturnsInvalidPointer)
        {
            auto src = MakeMemoryStream();
            ULARGE_INTEGER cb{};
            cb.QuadPart = 5;
            Assert::AreEqual(STG_E_INVALIDPOINTER, src->CopyTo(nullptr, cb, nullptr, nullptr));
        }

        TEST_METHOD(CopyTo_CopiesAllBytes)
        {
            auto src = MakeMemoryStream();
            src->Write("hello world", 11, nullptr);
            LARGE_INTEGER zero{};
            src->Seek(zero, STREAM_SEEK_SET, nullptr);

            auto dst = MakeMemoryStream();
            ULARGE_INTEGER cb{};
            cb.QuadPart = 11;
            ULARGE_INTEGER read{}, written{};
            Assert::AreEqual(S_OK, src->CopyTo(dst.get(), cb, &read, &written));
            Assert::AreEqual(UINT64(11), read.QuadPart);
            Assert::AreEqual(UINT64(11), written.QuadPart);
            Assert::AreEqual("hello world"sv, StreamView(dst.get()));
        }

        TEST_METHOD(CopyTo_StopsAtSource_EOF)
        {
            auto src = MakeMemoryStream();
            src->Write("hi", 2, nullptr);
            LARGE_INTEGER zero{};
            src->Seek(zero, STREAM_SEEK_SET, nullptr);

            auto dst = MakeMemoryStream();
            ULARGE_INTEGER cb{};
            cb.QuadPart = 100; // ask for more than is available
            ULARGE_INTEGER read{}, written{};
            Assert::AreEqual(S_OK, src->CopyTo(dst.get(), cb, &read, &written));
            Assert::AreEqual(UINT64(2), read.QuadPart);
            Assert::AreEqual(UINT64(2), written.QuadPart);
        }

        TEST_METHOD(CopyTo_LargeBuffer_ChunkedTransfer)
        {
            // Exceed the internal 256 KiB chunk size to force multiple Write calls.
            auto src = MakeMemoryStream();
            constexpr size_t LargeSize = 300 * 1024;
            std::vector<BYTE> payload(LargeSize, BYTE('A'));
            src->Write(payload.data(), static_cast<ULONG>(payload.size()), nullptr);

            LARGE_INTEGER zero{};
            src->Seek(zero, STREAM_SEEK_SET, nullptr);

            auto dst = MakeMemoryStream();
            ULARGE_INTEGER cb{};
            cb.QuadPart = LargeSize;
            ULARGE_INTEGER read{}, written{};
            Assert::AreEqual(S_OK, src->CopyTo(dst.get(), cb, &read, &written));
            Assert::AreEqual(UINT64(LargeSize), read.QuadPart);
            Assert::AreEqual(UINT64(LargeSize), written.QuadPart);
            Assert::AreEqual(LargeSize, StreamBytes(dst.get()).size());
        }

        TEST_METHOD(CopyTo_ZeroBytes_DoesNothing)
        {
            auto src = MakeMemoryStream();
            src->Write("hello", 5, nullptr);
            LARGE_INTEGER zero{};
            src->Seek(zero, STREAM_SEEK_SET, nullptr);

            auto dst = MakeMemoryStream();
            ULARGE_INTEGER cb{};
            cb.QuadPart = 0;
            ULARGE_INTEGER read{}, written{};
            Assert::AreEqual(S_OK, src->CopyTo(dst.get(), cb, &read, &written));
            Assert::AreEqual(UINT64(0), read.QuadPart);
            Assert::AreEqual(UINT64(0), written.QuadPart);
        }

        TEST_METHOD(CopyTo_NullOutCounts_Allowed)
        {
            auto src = MakeMemoryStream();
            src->Write("hi", 2, nullptr);
            LARGE_INTEGER zero{};
            src->Seek(zero, STREAM_SEEK_SET, nullptr);

            auto dst = MakeMemoryStream();
            ULARGE_INTEGER cb{};
            cb.QuadPart = 2;
            Assert::AreEqual(S_OK, src->CopyTo(dst.get(), cb, nullptr, nullptr));
            Assert::AreEqual("hi"sv, StreamView(dst.get()));
        }
    };

    // ------------------------------------------------------------------
    // Stat.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamStatTests)
    {
    public:

        TEST_METHOD(Stat_NullOut_ReturnsInvalidPointer)
        {
            auto stream = MakeMemoryStream();
            Assert::AreEqual(STG_E_INVALIDPOINTER, stream->Stat(nullptr, STATFLAG_NONAME));
        }

        TEST_METHOD(Stat_ReportsTypeStreamAndCurrentSize)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            STATSTG stat{};
            Assert::AreEqual(S_OK, stream->Stat(&stat, STATFLAG_NONAME));
            Assert::AreEqual(DWORD(STGTY_STREAM), DWORD(stat.type));
            Assert::AreEqual(UINT64(5), stat.cbSize.QuadPart);
            Assert::AreEqual(DWORD(STGTY_STREAM), DWORD(stat.type));
            Assert::IsNull(stat.pwcsName);
        }

        TEST_METHOD(Stat_NoName_LeavesPwcsNameNull)
        {
            auto stream = MakeMemoryStream();

            STATSTG stat{};
            Assert::AreEqual(S_OK, stream->Stat(&stat, 0));
            Assert::IsNull(stat.pwcsName);
        }
    };

    // ------------------------------------------------------------------
    // Commit / Revert / LockRegion / UnlockRegion / Clone.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamMiscTests)
    {
    public:

        TEST_METHOD(Commit_IsNoOp)
        {
            auto stream = MakeMemoryStream();
            Assert::AreEqual(S_OK, stream->Commit(STGC_DEFAULT));
        }

        TEST_METHOD(Revert_NotImplemented)
        {
            auto stream = MakeMemoryStream();
            Assert::AreEqual(E_NOTIMPL, stream->Revert());
        }

        TEST_METHOD(LockRegion_InvalidFunction)
        {
            auto stream = MakeMemoryStream();
            ULARGE_INTEGER off{}, cb{};
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->LockRegion(off, cb, LOCK_EXCLUSIVE));
        }

        TEST_METHOD(UnlockRegion_InvalidFunction)
        {
            auto stream = MakeMemoryStream();
            ULARGE_INTEGER off{}, cb{};
            Assert::AreEqual(STG_E_INVALIDFUNCTION,
                stream->UnlockRegion(off, cb, LOCK_EXCLUSIVE));
        }

        TEST_METHOD(Clone_NotImplemented_ClearsOutPointer)
        {
            auto stream = MakeMemoryStream();
            IStream* raw = reinterpret_cast<IStream*>(static_cast<INT_PTR>(0xDEADBEEF));
            Assert::AreEqual(E_NOTIMPL, stream->Clone(&raw));
            Assert::IsNull(raw);
        }
    };

    // ------------------------------------------------------------------
    // IRegExMemoryStream surface: Reset / Reserve / GetBuffer.
    // ------------------------------------------------------------------
    TEST_CLASS(RegExMemoryStreamBufferTests)
    {
    public:

        TEST_METHOD(Reset_ClearsBufferAndPosition)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            Assert::AreEqual(S_OK, stream->Reset());
            Assert::AreEqual(size_t(0), StreamBytes(stream.get()).size());

            LARGE_INTEGER zero{};
            ULARGE_INTEGER pos{};
            stream->Seek(zero, STREAM_SEEK_CUR, &pos);
            Assert::AreEqual(UINT64(0), pos.QuadPart);
        }

        TEST_METHOD(Reset_AllowsReuse)
        {
            auto stream = MakeMemoryStream();
            stream->Write("first", 5, nullptr);
            stream->Reset();
            stream->Write("second", 6, nullptr);
            Assert::AreEqual("second"sv, StreamView(stream.get()));
        }

        TEST_METHOD(Reserve_NegativeCapacity_ReturnsInvalidArg)
        {
            auto stream = MakeMemoryStream();
            Assert::AreEqual(E_INVALIDARG, stream->Reserve(-1));
        }

        TEST_METHOD(Reserve_DoesNotChangeLogicalSize)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hi", 2, nullptr);
            Assert::AreEqual(S_OK, stream->Reserve(4096));
            Assert::AreEqual("hi"sv, StreamView(stream.get()));
        }

        TEST_METHOD(Reserve_ExcessiveCapacity_ReturnsOutOfMemory)
        {
            auto stream = MakeMemoryStream();
            // Beyond the INT64_MAX cap.
            Assert::AreEqual(E_OUTOFMEMORY, stream->Reserve(INT64_MAX));
        }

        TEST_METHOD(GetBuffer_Null_ReturnsPointer)
        {
            auto stream = MakeMemoryStream();
            LONGLONG size = 0;
            Assert::AreEqual(E_POINTER, stream->get_Buffer(nullptr));
        }

        TEST_METHOD(GetBuffer_EmptyStream_ReturnsZeroSize)
        {
            auto stream = MakeMemoryStream();
            RegExBytes bytes = { 1, 1 };
            Assert::AreEqual(S_OK, stream->get_Buffer(&bytes));
            Assert::AreEqual(LONGLONG(0), bytes.size);
        }

        TEST_METHOD(GetBuffer_AfterWrite_ReturnsLogicalSize)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);

            RegExBytes bytes = { 1, 1 };
            Assert::AreEqual(S_OK, stream->get_Buffer(&bytes));
            Assert::AreEqual(LONGLONG(5), bytes.size);
            Assert::IsTrue(bytes.data != 0);

            auto const* p = reinterpret_cast<BYTE const*>(static_cast<UINT_PTR>(bytes.data));
            Assert::AreEqual(0, memcmp(p, "hello", 5));
        }

        TEST_METHOD(GetBuffer_AfterReset_ReturnsZeroSize)
        {
            auto stream = MakeMemoryStream();
            stream->Write("hello", 5, nullptr);
            stream->Reset();

            RegExBytes bytes = { 1, 1 };
            Assert::AreEqual(S_OK, stream->get_Buffer(&bytes));
            Assert::AreEqual(LONGLONG(0), bytes.size);
        }
    };
}
