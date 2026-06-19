#include "pch.h"
#include "Benchmarks.h"
#include "WindowsChar32RegexTraits.h"
#include "resource.h"

#include <TextEncoding.h>
#include <boost/regex/v5/unicode_iterator.hpp>

#include <string>
#include <string_view>
#include <vector>
#include <algorithm>
#include <stdio.h>

/*
Baseline:
---
  UTF-8       : median:    1702, min:    1682, stddev: 23317 Kc
  UTF-16LE    : median:    1739, min:    1711, stddev: 14108 Kc
  UTF-16BE    : median:    1827, min:    1817, stddev: 13693 Kc
  Latin1-Rand : median:    1449, min:    1440, stddev: 13511 Kc
---
*/

#pragma warning(disable: 4189) // local variable is initialized but not referenced
#pragma warning(disable: 4505) // unreferenced local function has been removed

static constexpr unsigned ExpectedMatches = 25840;

#ifdef NDEBUG
static constexpr unsigned BenchmarkIterations = 17;
#else
static constexpr unsigned BenchmarkIterations = 1;
#endif

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

template<class EncodingT>
static void
RunIteratorBenchmark(
    char const* label,
    typename EncodingT::CodePointRange corpus)
{
    using IteratorT = typename EncodingT::CodePointIterator;
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

    // Warm-up passes to prime I-cache and branch predictors (discarded).
    for (unsigned warmup = 0; warmup < 2; ++warmup)
    {
        for (auto const& pattern : patterns)
        {
            regex_iterator it(corpus.begin, corpus.end, pattern);
            regex_iterator itEnd;
            for (; it != itEnd; ++it) {}
        }
    }

    unsigned const GroupCount = 15;
    uint64_t samples[GroupCount];

    for (unsigned group = 0; group != GroupCount; group += 1)
    {
        size_t totalMatches = 0;

        ULONG64 startCycles, endCycles;
        QueryThreadCycleTime(GetCurrentThread(), &startCycles);
        for (unsigned iter = 0; iter < BenchmarkIterations; ++iter)
        {
            for (auto const& pattern : patterns)
            {
                regex_iterator it(corpus.begin, corpus.end, pattern);
                regex_iterator itEnd;
                for (; it != itEnd; ++it)
                {
                    ++totalMatches;
                }
            }
        }
        QueryThreadCycleTime(GetCurrentThread(), &endCycles);

        samples[group] = endCycles - startCycles;

        if (totalMatches != ExpectedMatches * BenchmarkIterations)
        {
            fprintf(stderr, "ERROR: %s - expected %u matches, got %zu\n", label, ExpectedMatches * BenchmarkIterations, totalMatches);
            return;
        }
    }

    std::sort(samples, samples + GroupCount);
    uint64_t cyclesMedian = samples[GroupCount / 2];
    uint64_t cyclesMin = samples[0];
    double cyclesAvg = 0;
    for (unsigned i = 0; i < GroupCount; ++i)
        cyclesAvg += static_cast<double>(samples[i]);
    cyclesAvg /= GroupCount;
    double variance = 0;
    for (unsigned i = 0; i < GroupCount; ++i)
    {
        double diff = static_cast<double>(samples[i]) - cyclesAvg;
        variance += diff * diff;
    }
    uint64_t cyclesStdDev = static_cast<uint64_t>(std::sqrt(variance / GroupCount));

    printf("  %-12s: median: %7llu, min: %7llu, stddev: %5llu Kc\n", label,
        cyclesMedian / 1000000, cyclesMin / 1000000, cyclesStdDev / 1000);
}

// ============================================================================
// Microbenchmark harnesses (no regex)
//
// These isolate the encoding layer from regex overhead so the Latin1 vs SBCS
// difference is visible directly. Reports min/median/stddev (in kilocycles)
// plus cycles-per-code-point derived from the min (min is the cleanest
// estimator since timing noise is one-sided).
// ============================================================================

// Volatile sink to keep accumulated results observable so the optimizer cannot
// eliminate or hoist the measured loops.
static volatile uint64_t g_benchmarkSink;

static void
ReportSamples(char const* label, uint64_t* samples, unsigned count, uint64_t elements)
{
    std::sort(samples, samples + count);
    uint64_t cyclesMedian = samples[count / 2];
    uint64_t cyclesMin = samples[0];

    double cyclesAvg = 0;
    for (unsigned i = 0; i < count; ++i)
        cyclesAvg += static_cast<double>(samples[i]);
    cyclesAvg /= count;

    double variance = 0;
    for (unsigned i = 0; i < count; ++i)
    {
        double diff = static_cast<double>(samples[i]) - cyclesAvg;
        variance += diff * diff;
    }
    uint64_t cyclesStdDev = static_cast<uint64_t>(std::sqrt(variance / count));

    double cyclesPerCp = elements
        ? static_cast<double>(cyclesMin) / static_cast<double>(elements)
        : 0.0;

    printf("  %-14s: min: %8llu Kc, median: %8llu Kc, stddev: %6llu Kc, %.3f c/cp\n",
        label, cyclesMin / 1000, cyclesMedian / 1000, cyclesStdDev / 1000, cyclesPerCp);
}

// Measures raw decode iteration (encoded char -> char32_t) with no regex.
// The inner sum loop may auto-vectorize for Latin1's identity mapping but not
// for SBCS's table lookup, so this can show a larger gap than the amortized
// regex scenario.
template<class EncodingT>
static void
RunRawIterationBenchmark(
    char const* label,
    typename EncodingT::CodePointRange corpus,
    size_t codePointCount)
{
    // Warm-up passes (discarded).
    for (unsigned warmup = 0; warmup < 2; ++warmup)
    {
        char32_t sum = 0;
        for (auto it = corpus.begin; it != corpus.end; ++it)
            sum += *it;
        g_benchmarkSink = sum;
    }

    unsigned const GroupCount = 15;
    uint64_t samples[GroupCount];

    for (unsigned group = 0; group != GroupCount; group += 1)
    {
        ULONG64 startCycles, endCycles;
        QueryThreadCycleTime(GetCurrentThread(), &startCycles);
        for (unsigned iter = 0; iter < BenchmarkIterations; ++iter)
        {
            char32_t sum = 0;
            for (auto it = corpus.begin; it != corpus.end; ++it)
                sum += *it;
            g_benchmarkSink = sum; // Observable each pass: prevents hoisting.
        }
        QueryThreadCycleTime(GetCurrentThread(), &endCycles);

        samples[group] = endCycles - startCycles;
    }

    ReportSamples(label, samples, GroupCount,
        static_cast<uint64_t>(codePointCount) * BenchmarkIterations);
}

// Measures the encode path (char32_t -> encoded char) via ConvertInPlace.
// ConvertInPlace overwrites its input, so the source buffer is restored before
// each conversion; the restore is excluded from the timed region.
template<class EncodingT>
static void
RunConvertInPlaceBenchmark(
    char const* label,
    EncodingT enc,
    std::vector<char32_t> const& source)
{
    std::vector<char32_t> work(source.size());

    // Warm-up + sanity check (discarded from timing).
    for (unsigned warmup = 0; warmup < 2; ++warmup)
    {
        std::copy(source.begin(), source.end(), work.begin());
        auto encoded = enc.ConvertInPlace(work);
        if (encoded.size() != source.size())
        {
            fprintf(stderr, "ERROR: %s - ConvertInPlace returned %zu bytes, expected %zu\n",
                label, encoded.size(), source.size());
            return;
        }
        g_benchmarkSink = encoded.empty() ? 0u : static_cast<unsigned char>(encoded.back());
    }

    unsigned const GroupCount = 15;
    uint64_t samples[GroupCount];

    for (unsigned group = 0; group != GroupCount; group += 1)
    {
        uint64_t groupCycles = 0;
        for (unsigned iter = 0; iter < BenchmarkIterations; ++iter)
        {
            // Restore the destructive input buffer (excluded from timing).
            std::copy(source.begin(), source.end(), work.begin());

            ULONG64 startCycles, endCycles;
            QueryThreadCycleTime(GetCurrentThread(), &startCycles);
            auto encoded = enc.ConvertInPlace(work);
            QueryThreadCycleTime(GetCurrentThread(), &endCycles);

            groupCycles += endCycles - startCycles;
            g_benchmarkSink = encoded.empty() ? 0u : static_cast<unsigned char>(encoded.back());
        }

        samples[group] = groupCycles;
    }

    ReportSamples(label, samples, GroupCount,
        static_cast<uint64_t>(source.size()) * BenchmarkIterations);
}

// ============================================================================
// Corpus conversion helpers
// ============================================================================

static std::vector<char>
ConvertUtf8ToLatin1(std::string_view utf8Data)
{
    std::vector<char> result;
    result.reserve(utf8Data.size());

    auto [begin, end] = Utf8().MakeCodePointRange(
        std::span(reinterpret_cast<char8_t const*>(utf8Data.data()), utf8Data.size()));

    for (auto it = begin; it != end; ++it)
    {
        char32_t cp = *it;
        result.push_back(CodePoint::IsLatin1(cp) ? static_cast<char>(cp) : '?');
    }

    return result;
}

// Decodes UTF-8 into a vector of code points (used as the source for the
// ConvertInPlace encode benchmarks).
static std::vector<char32_t>
ConvertUtf8ToCodePoints(std::string_view utf8Data)
{
    std::vector<char32_t> result;
    result.reserve(utf8Data.size());

    auto [begin, end] = Utf8().MakeCodePointRange(
        std::span(reinterpret_cast<char8_t const*>(utf8Data.data()), utf8Data.size()));

    for (auto it = begin; it != end; ++it)
    {
        result.push_back(*it);
    }

    return result;
}

static std::vector<char16_t>
ConvertUtf8ToUtf16LE(std::string_view utf8Data)
{
    std::vector<char16_t> result;
    result.reserve(utf8Data.size()); // rough estimate

    auto [begin, end] = Utf8().MakeCodePointRange(
        std::span(reinterpret_cast<char8_t const*>(utf8Data.data()), utf8Data.size()));

    for (auto it = begin; it != end; ++it)
    {
        char16_t buf[2];
        unsigned n = Utf16LE().Encode(buf, *it);
        result.insert(result.end(), buf, buf + n);
    }

    return result;
}

static std::vector<char16_t>
ConvertUtf8ToUtf16BE(std::string_view utf8Data)
{
    std::vector<char16_t> result;
    result.reserve(utf8Data.size());

    auto [begin, end] = Utf8().MakeCodePointRange(
        std::span(reinterpret_cast<char8_t const*>(utf8Data.data()), utf8Data.size()));

    for (auto it = begin; it != end; ++it)
    {
        char16_t buf[2];
        unsigned n = Utf16BE().Encode(buf, *it);
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

    auto latin1Data = ConvertUtf8ToLatin1(mobyText);

    auto const u8begin = reinterpret_cast<char8_t const*>(mobyText.data());
    auto const u8end = u8begin + mobyText.size();

#ifdef NDEBUG
    if (!SetThreadAffinityMask(GetCurrentThread(), 2))
    {
        printf("Failed to set thread affinity.\n");
    }

    if (!SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_ABOVE_NORMAL))
    {
        printf("Failed to set thread priority.\n");
    }
#endif

#if 0
    using u32_to_u8_it = boost::u8_to_u32_iterator<char8_t const*, char32_t>;
    RunIteratorBenchmark("UTF-8-Boost", std::pair(
        u32_to_u8_it(u8begin, u8begin, u8end),
        u32_to_u8_it(u8end, u8begin, u8end)));
    RunIteratorBenchmark<Utf8>("UTF-8",
        Utf8().MakeCodePointRange({ u8begin, u8end }));

#endif

#if 0
    for (unsigned pass = 0; pass != 3; pass += 1)
    {
        RunIteratorBenchmark<Latin1>("Latin1-Rand",
            Latin1().MakeCodePointRange({ latin1Data.data(), latin1Data.size() }));

        RunIteratorBenchmark<Sbcs>("cp28591-Rand",
            Sbcs::TryFromCodePage(28591).value().MakeCodePointRange({ latin1Data.data(), latin1Data.size() }));
    }
#endif

#if 1
    // Microbenchmarks isolating the encoding layer from regex overhead, so the
    // Latin1 (identity) vs SBCS (table lookup) difference is directly visible.
    {
        auto codePoints = ConvertUtf8ToCodePoints(mobyText);
        size_t const cpCount = codePoints.size();
        auto sbcs28591 = Sbcs::TryFromCodePage(28591).value();

        // Decode path: encoded char -> char32_t, no regex. Interleaved pairs so
        // each Latin1/SBCS comparison runs back-to-back under like conditions.
        printf("--- Raw decode iteration (char -> char32_t, no regex) ---\n");
        for (unsigned pass = 0; pass < 3; ++pass)
        {
            RunRawIterationBenchmark<Latin1>("Latin1-Decode",
                Latin1().MakeCodePointRange({ latin1Data.data(), latin1Data.size() }), cpCount);
            RunRawIterationBenchmark<Sbcs>("cp28591-Decode",
                sbcs28591.MakeCodePointRange({ latin1Data.data(), latin1Data.size() }), cpCount);
        }

        // Encode path: char32_t -> encoded char via ConvertInPlace.
        printf("--- ConvertInPlace encode (char32_t -> char) ---\n");
        for (unsigned pass = 0; pass < 3; ++pass)
        {
            RunConvertInPlaceBenchmark<Latin1>("Latin1-Encode", Latin1(), codePoints);
            RunConvertInPlaceBenchmark<Sbcs>("cp28591-Encode", sbcs28591, codePoints);
        }
    }
#endif

#if 0
    RunIteratorBenchmark<Utf16LE>("UTF-16LE",
        Utf16LE().MakeCodePointRange({ utf16leData.data(), utf16leData.size() }));

    auto utf16beData = ConvertUtf8ToUtf16BE(mobyText);
    RunIteratorBenchmark<Utf16BE>("UTF-16BE",
        Utf16BE().MakeCodePointRange({ utf16beData.data(), utf16beData.size() }));

    auto latin1Data = ConvertUtf8ToLatin1(mobyText);
    RunIteratorBenchmark<Latin1>("Latin1-Rand",
        Latin1().MakeCodePointRange({ latin1Data.data(), latin1Data.size() }));
#endif

    printf("---\nDone.\n");
}
