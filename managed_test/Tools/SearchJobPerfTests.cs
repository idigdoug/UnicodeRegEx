namespace UnicodeRegEx.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Engine;

    /// <summary>
    /// Macro-benchmark for <see cref="SearchJob"/>'s parallel file processing. This is NOT a correctness
    /// suite in the usual sense: it asserts only that every degree of parallelism produces the identical
    /// (correct) result, and otherwise <b>reports</b> wall-clock throughput so a human can confirm that
    /// threading delivers the expected speedup. Performance is intentionally not asserted (machine-dependent
    /// and CI-variable); read the printed table.
    /// </summary>
    /// <remarks>
    /// Excluded from the normal test run via <c>[TestCategory("Perf")]</c>; run it explicitly by category.
    /// It must execute <b>host-native</b> (not emulated) or the numbers are meaningless, so it marks itself
    /// inconclusive when the process architecture does not match the OS architecture.
    ///
    /// Sample baseline (arm64 host, 20 logical processors, Debug native lib; Release shifts absolute
    /// numbers but the relative speedup is the point). Corpus: 300 files, 10.5 MB, 20910 matches.
    ///   DOP    median(ms)   files/s   MB/s    speedup
    ///   1         768.0       391      13.7    1.00x
    ///   2         364.4       823      28.9    2.11x
    ///   4         250.0      1200      42.1    3.07x
    ///   8         208.5      1439      50.5    3.68x
    ///   auto      191.5      1566      55.0    4.01x
    ///
    /// CAVEAT (reading the numbers): watch the median-vs-min spread. When median &gt;&gt; min at DOP=1 (e.g.
    /// 504 vs 264 in one run), the serial baseline was measured on cold, un-ramped cores (CPU power
    /// management / turbo warm-up), which INFLATES the reported speedup -- one such run showed a fake ~8x at
    /// auto. When median is close to min (like the table above), the numbers are trustworthy. Steady runs on
    /// this box land around ~1.9x at DOP=4 and plateau there: the corpus is small-file / I/O-bound, so beyond
    /// a few threads the open path dominates and more threads add little. A larger / more compute-heavy
    /// corpus (bigger LargeFileBytes) pushes the plateau rightward; tune the consts to explore the curve.
    ///
    /// PROFILING FINDINGS (sample-based, the numbers to trust; instrumented profiling over-weights the many
    /// small managed calls and under-weights the few long native calls, so it wrongly made the file open look
    /// dominant). On the multi-thread path, ~35% of the run is in ProcessOne, broken down roughly as:
    ///   ~10.4%  native NextMatch      -- the actual regex work (the single largest slice; healthy)
    ///   ~7.6%   FileStream ctor       -- of which ~6.8% is the underlying CreateFile syscall (~0.8% wrapper)
    ///   ~6.2%   MemoryMappedFile.CreateFromFile (~5.6% ZwCreateSection underneath)
    ///   ~3%     Stream.Close / ~2.9% ZwClose
    ///   ~0.3%   ReportHit             -- sink dispatch is negligible
    /// Conclusion: the per-file pipeline is balanced -- the dominant cost is the match itself, and open / mmap
    /// / close are unavoidable syscalls at reasonable proportions. No pathology; deliberately left unoptimized.
    /// Two known micro-opts were identified and DEFERRED (payoff too small to justify the added code/risk):
    ///   (1) a bufferless CreateFile-based open instead of FileStream -- recovers only the ~0.8% wrapper cost
    ///       and skips an unused 4 KB buffer; and
    ///   (2) direct ReadFile into a pooled buffer for small files instead of a memory map -- avoids the
    ///       section create/teardown for tiny files, but adds a second input path (pinned buffer vs. mapped
    ///       pointer) for a single-digit-percent gain that only applies below some size.
    /// Note: excluding the search tree from antivirus did NOT materially change the open cost -- the filter
    /// driver still intercepts every CreateFile to evaluate whether an exclusion applies.
    /// </remarks>
    [TestClass]
    public class SearchJobPerfTests
    {
        // Corpus knobs (tune here). The corpus is generated once per test with a fixed seed, so runs are
        // comparable across degrees of parallelism and across invocations.
        private const int FileCount = 300;
        private const int SmallFileBytes = 4 * 1024;
        private const int LargeFileBytes = 256 * 1024;
        private const int LargeFileEvery = 8;              // every Nth file is a large file
        private const int MatchEveryBytes = 512;           // one sentinel token per this many bytes
        private const string Sentinel = "NEEDLE";          // the token the search pattern matches
        private const int SubdirCount = 8;                 // spread files across this many subdirectories

        // Measurement knobs.
        private const int WarmupTrials = 1;
        private const int TimedTrials = 5;
        private static readonly int[] DegreesOfParallelism = { 1, 2, 4, 8, 0 /* auto */ };

        private string tempDir = string.Empty;

        public TestContext TestContext { get; set; } = null!;

        [TestInitialize]
        public void Setup()
        {
            var perfDir = Path.Combine(Path.GetTempPath(), "urex_perf");
            Directory.CreateDirectory(perfDir);
            tempDir = Path.Combine(perfDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        [TestMethod]
        [TestCategory("Perf")]
        public async Task Parallel_Throughput_ScalesWithDegreeOfParallelism()
        {
            RequireHostNative();

            var expectedMatches = GenerateCorpus(out var totalBytes);
            TestContext.WriteLine(
                $"Corpus: {FileCount} files, {totalBytes / (1024.0 * 1024.0):F1} MB, {expectedMatches} matches, " +
                $"{Environment.ProcessorCount} logical processors ({RuntimeInformation.ProcessArchitecture}).");
            TestContext.WriteLine("DOP\tmedian(ms)\tmin(ms)\tfiles/s\tMB/s\tspeedup");

            double? serialMedian = null;
            foreach (var dop in DegreesOfParallelism)
            {
                // Warm the file cache + JIT, then take timed trials. Every trial re-verifies the result, so
                // a faster run that silently dropped work fails the correctness assertion below.
                for (var i = 0; i < WarmupTrials; i++)
                {
                    await RunOnce(dop, expectedMatches);
                }

                var timings = new List<double>(TimedTrials);
                for (var i = 0; i < TimedTrials; i++)
                {
                    timings.Add(await RunOnce(dop, expectedMatches));
                }

                timings.Sort();
                var median = timings[timings.Count / 2];
                var min = timings[0];
                var filesPerSec = FileCount / (median / 1000.0);
                var mbPerSec = (totalBytes / (1024.0 * 1024.0)) / (median / 1000.0);

                serialMedian ??= median;
                var speedup = serialMedian.Value / median;

                var label = dop == 0 ? "auto" : dop.ToString();
                TestContext.WriteLine(
                    $"{label}\t{median,9:F1}\t{min,7:F1}\t{filesPerSec,7:F0}\t{mbPerSec,6:F1}\t{speedup:F2}x");
            }
        }

        // Runs one full SearchJob over the corpus at the given degree of parallelism, asserts the result is
        // correct (the exact expected match count, no errors, completed), and returns the elapsed wall-clock
        // milliseconds.
        private async Task<double> RunOnce(int dop, int expectedMatches)
        {
            var sink = new CountingSink();
            var request = new SearchRequest
            {
                Pattern = Sentinel,
                DefaultCodePage = RegExCodePage.Utf8,
                Directories = DirectoryDisposition.RecurseNoLinks,
                MaxDegreeOfParallelism = dop,
            };
            request.Paths.Add(tempDir);

            using var job = new SearchJob(request, sink);

            var stopwatch = Stopwatch.StartNew();
            await job.RunAsync();
            stopwatch.Stop();

            Assert.AreEqual(SearchJobState.Completed, job.State, $"DOP={dop} did not complete.");
            Assert.AreEqual(0, job.Summary.Errors, $"DOP={dop} reported errors.");
            Assert.AreEqual(expectedMatches, sink.HitCount, $"DOP={dop} produced the wrong match count.");

            return stopwatch.Elapsed.TotalMilliseconds;
        }

        // Builds the synthetic corpus and returns the total number of sentinel matches across all files.
        private int GenerateCorpus(out long totalBytes)
        {
            var random = new Random(12345); // fixed seed => reproducible corpus
            totalBytes = 0;
            var totalMatches = 0;

            for (var i = 0; i < FileCount; i++)
            {
                var size = (i % LargeFileEvery == 0) ? LargeFileBytes : SmallFileBytes;
                var relative = Path.Combine($"dir{i % SubdirCount}", $"file{i}.txt");
                var full = Path.Combine(tempDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);

                var (content, matches) = BuildContent(size, random);
                File.WriteAllText(full, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                totalBytes += Encoding.UTF8.GetByteCount(content);
                totalMatches += matches;
            }

            return totalMatches;
        }

        // Produces roughly targetBytes of filler text with a Sentinel token inserted every MatchEveryBytes,
        // returning the text and the exact number of sentinels placed. Filler is lowercase ASCII words so it
        // never accidentally contains the (uppercase) sentinel.
        private static (string Content, int Matches) BuildContent(int targetBytes, Random random)
        {
            var builder = new StringBuilder(targetBytes + 64);
            var matches = 0;
            var sinceLastSentinel = 0;

            while (builder.Length < targetBytes)
            {
                if (sinceLastSentinel >= MatchEveryBytes)
                {
                    builder.Append(Sentinel).Append(' ');
                    matches++;
                    sinceLastSentinel = 0;
                    continue;
                }

                var wordLength = 3 + random.Next(6); // 3..8 chars
                for (var i = 0; i < wordLength; i++)
                {
                    builder.Append((char)('a' + random.Next(26)));
                }

                builder.Append(' ');
                sinceLastSentinel += wordLength + 1;
            }

            return (builder.ToString(), matches);
        }

        // Marks the test inconclusive unless it is running host-native (the process architecture matches the
        // machine's TRUE native architecture). A parallel-throughput benchmark under emulation would report
        // meaningless numbers -- and the native lib is built per-arch, so an emulated host would also fail to
        // load its DLL. RuntimeInformation.OSArchitecture is unreliable here: an x64 process emulated on an
        // arm64 host reports OSArchitecture == X64. IsWow64Process2's nativeMachine reports the real host
        // architecture regardless of emulation, so it is the authoritative signal.
        private void RequireHostNative()
        {
            var process = RuntimeInformation.ProcessArchitecture;

            if (NativeMethods.TryGetNativeMachine(out var nativeArchitecture) &&
                process != nativeArchitecture)
            {
                Assert.Inconclusive(
                    $"Perf benchmark must run host-native: process is {process} but the host is " +
                    $"{nativeArchitecture} (emulated). Run the test host as {nativeArchitecture} " +
                    $"(this project prefers native arm64; the x64 vstest.console runs emulated).");
            }
        }

        private static class NativeMethods
        {
            // IMAGE_FILE_MACHINE_* values (winnt.h) mapped to the managed Architecture enum.
            private const ushort IMAGE_FILE_MACHINE_UNKNOWN = 0;
            private const ushort IMAGE_FILE_MACHINE_I386 = 0x014C;
            private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
            private const ushort IMAGE_FILE_MACHINE_ARM64 = 0xAA64;

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr GetCurrentProcess();

            // IsWow64Process2 reports the process's emulated machine and the host's *native* machine, so it
            // reveals the true host architecture even when the current process is running under emulation.
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool IsWow64Process2(IntPtr hProcess, out ushort pProcessMachine, out ushort pNativeMachine);

            // Returns the host's true native architecture. Returns false if it can't be determined (very old
            // OS without IsWow64Process2), in which case the caller skips the emulation guard.
            public static bool TryGetNativeMachine(out Architecture nativeArchitecture)
            {
                nativeArchitecture = default;

                try
                {
                    if (!IsWow64Process2(GetCurrentProcess(), out _, out var nativeMachine))
                    {
                        return false;
                    }

                    switch (nativeMachine)
                    {
                        case IMAGE_FILE_MACHINE_I386:
                            nativeArchitecture = Architecture.X86;
                            return true;
                        case IMAGE_FILE_MACHINE_AMD64:
                            nativeArchitecture = Architecture.X64;
                            return true;
                        case IMAGE_FILE_MACHINE_ARM64:
                            nativeArchitecture = Architecture.Arm64;
                            return true;
                        default:
                            return false;
                    }
                }
                catch (EntryPointNotFoundException)
                {
                    // IsWow64Process2 predates Windows 10 1709; if it's missing, skip the guard.
                    return false;
                }
                catch (DllNotFoundException)
                {
                    return false;
                }
            }
        }

        // A minimal sink that just counts hits. The engine does not serialize callbacks, so under the
        // benchmark's parallel degrees different files' OnHit calls arrive concurrently -- count atomically.
        private sealed class CountingSink : SearchSinkBase
        {
            private int hitCount;

            public int HitCount => Volatile.Read(ref hitCount);

            public override SearchResponse OnMatch(in SearchHit hit)
            {
                Interlocked.Increment(ref hitCount);
                return SearchResponse.Continue;
            }
        }
    }
}
