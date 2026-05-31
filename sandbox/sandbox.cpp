#include "pch.h"
#include "WindowsChar32RegexTraits.h"
#include <boost/regex.hpp>
#include <string_view>

#include "Benchmarks.h"

using namespace std::string_view_literals;

int __cdecl
wmain()
{
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
