#include "pch.h"
#include "WindowsChar32RegexTraits.h"
#include <boost/regex.hpp>
#include <string_view>

#include <TextEncoding.h>

#include "Benchmarks.h"

using namespace std::string_view_literals;

int __cdecl
wmain()
{
    auto cp = Sbcs::TryFromCodePage(1252).value();
    
    std::u32string str(U"Hello world this has some 1252 chars like € and © and some supplementary chars like 😀 and some random chars like \u0378\0");
    auto chars = cp.ConvertInPlace(str);
    std::fprintf(stdout, "%hs\n", chars.data());

    try
    {
        Benchmarks();
    }
    catch (std::exception const& ex)
    {
        std::fprintf(stderr, "Unexpected exception: %s\n", ex.what());
        return 1;
    }

    return 0;
}
