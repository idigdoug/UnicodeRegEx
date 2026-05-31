#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <windows.h>
#include <boost/regex.hpp>

#include <wil/com.h>
#include <wil/result.h>

#include <CppUnitTest.h>

#include <variant>
#include <string>
#include <vector>
#include <span>

namespace Microsoft::VisualStudio::CppUnitTestFramework
{
    template<> inline std::wstring ToString<char32_t>(const char32_t& value)
    {
        return std::to_wstring(static_cast<unsigned long>(value));
    }
}
