#include "pch.h"
#include "Benchmarks.h"
#include "WindowsChar32RegexTraits.h"
#include <utf.h>
#include "resource.h"

#include <boost/regex/v5/unicode_iterator.hpp>

#include <string>
#include <string_view>
#include <vector>
#include <chrono>
#include <stdio.h>

#pragma warning(disable: 4505) // C4505: unreferenced local function has been removed

/*
---
  UTF-8       :  9400ms
---
*/

static constexpr unsigned ExpectedMatches = 25840;
static constexpr unsigned BenchmarkIterations = 50;

// ============================================================================
// Helpers
// ============================================================================

// Loads an RCDATA resource from the current module.
static std::string_view
LoadTextResource(int resourceId)
{
    HMODULE hModule = GetModuleHandleW(nullptr);
    HRSRC hRes = FindResourceW(hModule, MAKEINTRESOURCEW(resourceId), RT_RCDATA);
    if (!hRes) return {};
    HGLOBAL hData = LoadResource(hModule, hRes);
    if (!hData) return {};
    auto* ptr = static_cast<char const*>(LockResource(hData));
    auto size = SizeofResource(hModule, hRes);
    return { ptr, size };
}

// ============================================================================
// Benchmark template
// ============================================================================

static constexpr wchar_t const* TestPatterns[] = {
    L"\\b[Ww]hale\\b",          // common word
    L"\\b[A-Z][a-z]+\\b",       // capitalized words
    L"Chapter \\d+",            // chapter headings
    L"\\b\\w{10,}\\b",          // long words (10+ chars)
};
static constexpr size_t TestPatternCount = sizeof(TestPatterns) / sizeof(TestPatterns[0]);

template<class IteratorT>
static void
RunIteratorBenchmark(
    char const* label,
    std::pair<IteratorT, IteratorT> corpus)
{
    using regex_type = boost::basic_regex<char32_t, WindowsChar32RegexTraits>;
    using regex_iterator = boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>;

    // Pre-compile all patterns (excluded from timing).
    std::vector<regex_type> patterns;
    patterns.reserve(TestPatternCount);
    for (auto* pat : TestPatterns)
    {
        std::u32string pat32;
        for (auto* p = pat; *p; ++p)
            pat32.push_back(static_cast<char32_t>(*p));
        patterns.emplace_back(pat32.data(), pat32.data() + pat32.size());
    }

    size_t totalMatches = 0;
    auto start = std::chrono::high_resolution_clock::now();

    for (unsigned iter = 0; iter < BenchmarkIterations; ++iter)
    {
        for (auto const& pattern : patterns)
        {
            regex_iterator it(corpus.first, corpus.second, pattern);
            regex_iterator itEnd;
            for (; it != itEnd; ++it)
            {
                ++totalMatches;
            }
        }
    }

    auto elapsed = std::chrono::high_resolution_clock::now() - start;
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(elapsed).count();

    if (totalMatches != ExpectedMatches * BenchmarkIterations)
    {
        printf("  %-12s: %5lldms (MISMATCH: %zu != %u)\n", label, ms, totalMatches, ExpectedMatches * BenchmarkIterations);
    }
    else
    {
        printf("  %-12s: %5lldms\n", label, ms);
    }
}

// ============================================================================
// Corpus conversion helpers
// ============================================================================

static std::vector<char>
ConvertUtf8ToLatin1(std::string_view utf8Data)
{
    std::vector<char> result;
    result.reserve(utf8Data.size());

    auto [begin, end] = utf8::CodePointIterator::FromSpan(
        std::span(reinterpret_cast<char8_t const*>(utf8Data.data()), utf8Data.size()));

    for (auto it = begin; it != end; ++it)
    {
        char32_t cp = *it;
        result.push_back(utf::IsLatin1(cp) ? static_cast<char>(cp) : '?');
    }

    return result;
}

static std::vector<char16_t>
ConvertUtf8ToUtf16LE(std::string_view utf8Data)
{
    std::vector<char16_t> result;
    result.reserve(utf8Data.size()); // rough estimate

    auto [begin, end] = utf8::CodePointIterator::FromSpan(
        std::span(reinterpret_cast<char8_t const*>(utf8Data.data()), utf8Data.size()));

    for (auto it = begin; it != end; ++it)
    {
        char16_t buf[2];
        unsigned n = utf16le::Encode(buf, *it);
        result.insert(result.end(), buf, buf + n);
    }

    return result;
}

static std::vector<char16_t>
ConvertUtf8ToUtf16BE(std::string_view utf8Data)
{
    std::vector<char16_t> result;
    result.reserve(utf8Data.size());

    auto [begin, end] = utf8::CodePointIterator::FromSpan(
        std::span(reinterpret_cast<char8_t const*>(utf8Data.data()), utf8Data.size()));

    for (auto it = begin; it != end; ++it)
    {
        char16_t buf[2];
        unsigned n = utf16be::Encode(buf, *it);
        result.insert(result.end(), buf, buf + n);
    }

    return result;
}

// ============================================================================
// Entry point
// ============================================================================

void
Benchmarks()
{
    auto mobyText = LoadTextResource(IDR_MOBY_DICK);
    if (mobyText.empty())
    {
        fprintf(stderr, "Failed to load MobyDick.txt resource.\n");
        return;
    }

    printf("Corpus: MobyDick.txt (%zu bytes)\n", mobyText.size());
    printf("Patterns: %zu\n", TestPatternCount);
    printf("---\n");

    using u32_to_u8_it = boost::u8_to_u32_iterator<char8_t const*, char32_t>;

    auto const u8begin = reinterpret_cast<char8_t const*>(mobyText.data());
    auto const u8end = u8begin + mobyText.size();

    // UTF-8
    RunIteratorBenchmark("UTF-8-Boost", std::pair(
        u32_to_u8_it(u8begin, u8begin, u8end),
        u32_to_u8_it(u8end, u8begin, u8end)));
    RunIteratorBenchmark("UTF-8",
        utf8::CodePointIterator::FromSpan({ u8begin, u8end }));

#if 1
    // UTF-16LE
    auto utf16leData = ConvertUtf8ToUtf16LE(mobyText);
    RunIteratorBenchmark<utf16le::CodePointIterator>("UTF-16LE",
        utf16le::CodePointIterator::FromSpan({ utf16leData.data(), utf16leData.size() }));

    // UTF-16BE
    auto utf16beData = ConvertUtf8ToUtf16BE(mobyText);
    RunIteratorBenchmark<utf16be::CodePointIterator>("UTF-16BE",
        utf16be::CodePointIterator::FromSpan({ utf16beData.data(), utf16beData.size() }));

    // Latin1 (random-access) - properly converted from UTF-8
    auto latin1Data = ConvertUtf8ToLatin1(mobyText);
    RunIteratorBenchmark<latin1::CodePointIterator>("Latin1-Rand",
        latin1::CodePointIterator::FromSpan({ latin1Data.data(), latin1Data.size() }));
#endif

    printf("---\nDone.\n");
}
