#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace RegExTests
{
    TEST_CLASS(RegExCreateTests)
    {
    public:

        TEST_METHOD(CreateSimpleRegex)
        {
            RegExErrorCode errorCode = RegExErrorCode_unknown;
            wil::com_ptr<IRegEx> regex;
            HRESULT hr = TryMakeRegEx(L"hello", RegExSyntaxFlags_ECMAScript, LOCALE_NEUTRAL, &errorCode, regex);

            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(regex.get());
            Assert::AreEqual((int)RegExErrorCode_ok, (int)errorCode);
        }

        TEST_METHOD(CreateInvalidRegex)
        {
            RegExErrorCode errorCode = RegExErrorCode_ok;
            wil::com_ptr<IRegEx> regex;
            HRESULT hr = TryMakeRegEx(L"[invalid", RegExSyntaxFlags_ECMAScript, LOCALE_NEUTRAL, &errorCode, regex);

            Assert::AreEqual(MK_E_SYNTAX, hr);
            Assert::IsNull(regex.get());
            Assert::IsTrue(errorCode != RegExErrorCode_ok);
        }

        TEST_METHOD(CreateWithIcase)
        {
            auto regex = MakeRegEx(L"hello",
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_icase),
                LOCALE_INVARIANT);
            Assert::IsNotNull(regex.get());
        }

        TEST_METHOD(QueryInterface_IUnknown)
        {
            auto regex = MakeRegEx(L"test", RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT);

            wil::com_ptr<IUnknown> unk;
            HRESULT hr = regex->QueryInterface(IID_PPV_ARGS(unk.put()));
            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(unk.get());
        }

        TEST_METHOD(QueryInterface_IMarshal)
        {
            // RegEx is free-threaded so it exposes IMarshal via the free-threaded marshaler.
            auto regex = MakeRegEx(L"test", RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT);

            wil::com_ptr<IMarshal> marshal;
            HRESULT hr = regex->QueryInterface(IID_PPV_ARGS(marshal.put()));
            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(marshal.get());
        }

        TEST_METHOD(QueryInterface_Unknown_ReturnsNoInterface)
        {
            auto regex = MakeRegEx(L"test", RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT);

            // IID_IDispatch should not be supported on IRegEx.
            wil::com_ptr<IDispatch> disp;
            HRESULT hr = regex->QueryInterface(IID_PPV_ARGS(disp.put()));
            Assert::AreEqual(E_NOINTERFACE, hr);
            Assert::IsNull(disp.get());
        }

        TEST_METHOD(RefCounting)
        {
            auto regex = MakeRegEx(L"test", RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT);
            IRegEx* raw = regex.get();

            ULONG ref = raw->AddRef();
            Assert::IsTrue(ref >= 2);
            raw->Release();
            // final release happens when `regex` goes out of scope
        }

        TEST_METHOD(GetPattern_ReturnsOriginal)
        {
            auto regex = MakeRegEx(L"hello.*world", RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT);

            wil::unique_bstr pattern;
            Assert::AreEqual(S_OK, regex->get_Pattern(pattern.put()));
            Assert::AreEqual(L"hello.*world"sv, MakeView(pattern.get()));
        }

        TEST_METHOD(GetPattern_EmptyPattern)
        {
            auto regex = MakeRegEx(L"", RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT);

            wil::unique_bstr pattern;
            Assert::AreEqual(S_OK, regex->get_Pattern(pattern.put()));
            Assert::AreEqual(L""sv, MakeView(pattern.get()));
        }

        TEST_METHOD(GetFlags_ReturnsOriginal)
        {
            auto regex = MakeRegEx(L"hello",
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_icase),
                LOCALE_INVARIANT);

            RegExSyntaxFlags flags = RegExSyntaxFlags_ECMAScript;
            Assert::AreEqual(S_OK, regex->get_Flags(&flags));
            Assert::AreEqual(
                (int)(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_icase),
                (int)flags);
        }

        TEST_METHOD(GetLcid_ReturnsOriginal)
        {
            // 0x0409 = en-US
            auto regex = MakeRegEx(L"hello", RegExSyntaxFlags_ECMAScript, 0x0409);

            UINT32 lcid = 0;
            Assert::AreEqual(S_OK, regex->get_Lcid(&lcid));
            Assert::AreEqual(UINT32(0x0409), lcid);
        }

        TEST_METHOD(GetLcid_Invariant)
        {
            auto regex = MakeRegEx(L"hello", RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT);

            UINT32 lcid = 0xFFFFFFFF;
            Assert::AreEqual(S_OK, regex->get_Lcid(&lcid));
            Assert::AreEqual(UINT32(LOCALE_INVARIANT), lcid);
        }
    };

    TEST_CLASS(RegExLibraryQITests)
    {
    public:

        TEST_METHOD(QueryInterface_IUnknown)
        {
            auto library = GetLibrary();
            wil::com_ptr<IUnknown> unk;
            Assert::AreEqual(S_OK, library->QueryInterface(IID_PPV_ARGS(unk.put())));
            Assert::IsNotNull(unk.get());
        }

        TEST_METHOD(QueryInterface_IRegExLibrary)
        {
            auto library = GetLibrary();
            wil::com_ptr<IRegExLibrary> reLib;
            Assert::AreEqual(S_OK, library->QueryInterface(IID_PPV_ARGS(reLib.put())));
            Assert::IsNotNull(reLib.get());
        }

        TEST_METHOD(QueryInterface_IMarshal)
        {
            auto library = GetLibrary();
            wil::com_ptr<IMarshal> marshal;
            Assert::AreEqual(S_OK, library->QueryInterface(IID_PPV_ARGS(marshal.put())));
            Assert::IsNotNull(marshal.get());
        }

        TEST_METHOD(QueryInterface_Unknown_ReturnsNoInterface)
        {
            auto library = GetLibrary();
            wil::com_ptr<IDispatch> disp;
            HRESULT hr = library->QueryInterface(IID_PPV_ARGS(disp.put()));
            Assert::AreEqual(E_NOINTERFACE, hr);
            Assert::IsNull(disp.get());
        }

        TEST_METHOD(QueryInterface_NullOut_ReturnsPointer)
        {
            auto library = GetLibrary();
            Assert::AreEqual(E_POINTER, library->QueryInterface(IID_IUnknown, nullptr));
        }

        TEST_METHOD(RefCounting)
        {
            // Force creation of a new library instance to verify ref-counting independent
            // of the cached process-wide one.
            wil::com_ptr<IRegExLibrary> library;
            Assert::AreEqual(S_OK, UnicodeRegExLibraryCreate(library.put()));
            Assert::IsNotNull(library.get());

            IRegExLibrary* raw = library.get();
            ULONG ref = raw->AddRef();
            Assert::IsTrue(ref >= 2);
            raw->Release();
            // final release happens when `library` goes out of scope
        }
    };

    TEST_CLASS(RegExMatchEnumeratorQITests)
    {
    public:

        TEST_METHOD(QueryInterface_IUnknown)
        {
            auto regex = MakeRegEx(L"x");
            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(inputBytes, RegExCodePage_utf8, 0, RegExMatchFlag_default, enumerator.put());

            wil::com_ptr<IUnknown> unk;
            Assert::AreEqual(S_OK, enumerator->QueryInterface(IID_PPV_ARGS(unk.put())));
            Assert::IsNotNull(unk.get());
        }

        TEST_METHOD(QueryInterface_IRegExMatchResults)
        {
            auto regex = MakeRegEx(L"x");
            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(inputBytes, RegExCodePage_utf8, 0, RegExMatchFlag_default, enumerator.put());

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(S_OK, enumerator->QueryInterface(IID_PPV_ARGS(results.put())));
            Assert::IsNotNull(results.get());
        }

        TEST_METHOD(QueryInterface_IRegExMatchEnumerator)
        {
            auto regex = MakeRegEx(L"x");
            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(inputBytes, RegExCodePage_utf8, 0, RegExMatchFlag_default, enumerator.put());

            wil::com_ptr<IRegExMatchEnumerator> enum2;
            Assert::AreEqual(S_OK, enumerator->QueryInterface(IID_PPV_ARGS(enum2.put())));
            Assert::IsNotNull(enum2.get());
        }

        TEST_METHOD(QueryInterface_Unknown_ReturnsNoInterface)
        {
            auto regex = MakeRegEx(L"x");
            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(inputBytes, RegExCodePage_utf8, 0, RegExMatchFlag_default, enumerator.put());

            wil::com_ptr<IDispatch> disp;
            HRESULT hr = enumerator->QueryInterface(IID_PPV_ARGS(disp.put()));
            Assert::AreEqual(E_NOINTERFACE, hr);
            Assert::IsNull(disp.get());
        }

        TEST_METHOD(QueryInterface_NullOut_ReturnsPointer)
        {
            auto regex = MakeRegEx(L"x");
            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(inputBytes, RegExCodePage_utf8, 0, RegExMatchFlag_default, enumerator.put());

            Assert::AreEqual(E_POINTER, enumerator->QueryInterface(IID_IUnknown, nullptr));
        }
    };
}
