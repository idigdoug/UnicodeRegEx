#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

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

        TEST_METHOD(RefCounting)
        {
            auto regex = MakeRegEx(L"test", RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT);
            IRegEx* raw = regex.get();

            ULONG ref = raw->AddRef();
            Assert::IsTrue(ref >= 2);
            raw->Release();
            // final release happens when `regex` goes out of scope
        }
    };
}
