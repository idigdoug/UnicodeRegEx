namespace UnicodeRegEx.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Runtime.InteropServices;
    using UnicodeRegEx;
    using UnicodeRegEx.CommandLine;
    using UnicodeRegEx.Tools;

    internal static class Program
    {
        private static volatile bool cancelled;

        private static int Main(string[] args)
        {
            try
            {
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cancelled = true;
                };

                const string usage = "Usage: UnicodeRegExCli [options] <pattern> [path]...";

                // Precedence: built-in defaults < .config (<appSettings>) < command line.
                var options = new SearchSettings();
                var configErrors = new List<string>();
                AppConfigSettings.Apply(options, configErrors);

                var parsed = CommandLineParser.Parse(options, args);

                if (parsed.Status == ParseStatus.HelpRequested)
                {
                    Console.Out.WriteLine(HelpFormatter.Format(usage, options));
                    return 0;
                }

                if (configErrors.Count > 0 || parsed.Status == ParseStatus.Error)
                {
                    foreach (var error in configErrors)
                    {
                        Console.Error.WriteLine($"error: {error}");
                    }

                    foreach (var error in parsed.Errors)
                    {
                        Console.Error.WriteLine($"error: {error}");
                    }

                    Console.Error.WriteLine(usage);
                    return 2;
                }

                if (parsed.Positionals.Count == 0)
                {
                    Console.Error.WriteLine("error: missing <pattern>");
                    Console.Error.WriteLine(usage);
                    return 2;
                }

                var pattern = parsed.Positionals[0];
                var paths = parsed.Positionals.GetRange(1, parsed.Positionals.Count - 1);
                if (paths.Count == 0)
                {
                    paths.Add(".");
                }

                var template = options.Replace.Value;
                var apply = options.Apply.Value;
                if (apply && template == null)
                {
                    Console.Error.WriteLine("error: --apply requires --replace");
                    Console.Error.WriteLine(usage);
                    return 2;
                }

                var syntaxFlags = options.IgnoreCase.Value
                    ? RegExSyntaxFlags.ECMAScript | RegExSyntaxFlags.ICase
                    : RegExSyntaxFlags.ECMAScript;
                using var regex = RegEx.Create(pattern, syntaxFlags);

                var defaultCodePage = ResolveDefaultCodePage(options.Encoding.Value);

                var anyMatch = false;
                var errors = 0;
                foreach (var file in EnumerateFiles(paths))
                {
                    if (cancelled)
                    {
                        break;
                    }

                    try
                    {
                        anyMatch |= apply
                            ? ApplyReplaceFile(regex, file, defaultCodePage, template!)
                            : MatchFile(regex, file, defaultCodePage, template);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Console.Error.WriteLine($"{file}: {ex.Message}");
                    }
                }

                if (errors > 0)
                {
                    return 2;
                }

                return anyMatch ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 2;
            }
        }

        private static unsafe bool MatchFile(RegEx regex, string path, int defaultCodePage, string? replaceTemplate)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (length == 0)
            {
                return false;
            }

            using var mmf = MemoryMappedFile.CreateFromFile(
                stream, null, 0, MemoryMappedFileAccess.Read, null, HandleInheritability.None, leaveOpen: true);
            using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var handle = view.SafeMemoryMappedViewHandle;

            byte* basePtr = null;
            handle.AcquirePointer(ref basePtr);
            try
            {
                var data = basePtr + view.PointerOffset;

                var codePage = DetectCodePage(data, length, defaultCodePage, out var bomLength);
                if (codePage != RegExCodePage.Utf16LE &&
                    codePage != RegExCodePage.Utf16BE &&
                    LooksBinary(data, length))
                {
                    return false;
                }

                var unit = CodeUnitSize(codePage);

                // Lambdas can't capture pointers, so pass the view's base address as an IntPtr.
                var dataAddress = (IntPtr)data;

                var enumerateOptions = replaceTemplate == null
                    ? default
                    : new RegExEnumerateOptions { FormatTemplate = replaceTemplate };

                return regex.EnumerateMatches(
                    new RegExInput(handle, (nuint)view.PointerOffset, (nuint)length, codePage),
                    enumerateOptions,
                    matches =>
                    {
                        var bytes = (byte*)dataAddress;
                        var matched = false;
                        long scan = 0;
                        long curLineStart = 0;
                        long lastPrintedLineStart = -1;
                        var line = 1;

                        foreach (var match in matches)
                        {
                            var begin = (long)match.GetSubMatch(0).Begin;

                            // Advance a running line counter (and current line start) up to the match.
                            while (scan < begin)
                            {
                                if (IsAsciiUnit(bytes, scan, length, codePage, 0x0A))
                                {
                                    line++;
                                    curLineStart = scan + unit;
                                }

                                scan += unit;
                            }

                            // In replace-preview mode, show each match's replacement and move on.
                            if (replaceTemplate != null)
                            {
                                Console.Out.WriteLine($"{path}:{line}: {match.Text} => {match.Format()}");
                                matched = true;
                                continue;
                            }

                            // Grep mode: print each matching line at most once.
                            if (curLineStart == lastPrintedLineStart)
                            {
                                continue;
                            }

                            lastPrintedLineStart = curLineStart;

                            var lineEnd = begin;
                            while (lineEnd < length && !IsAsciiUnit(bytes, lineEnd, length, codePage, 0x0A))
                            {
                                lineEnd += unit;
                            }

                            // Drop a trailing CR so CRLF lines don't render a stray carriage return.
                            if (lineEnd - unit >= curLineStart && IsAsciiUnit(bytes, lineEnd - unit, length, codePage, 0x0D))
                            {
                                lineEnd -= unit;
                            }

                            var displayStart = curLineStart == 0 && bomLength > 0 ? bomLength : curLineStart;
                            var size = lineEnd > displayStart ? checked((int)(lineEnd - displayStart)) : 0;
                            var text = size > 0 ? match.CopyInput((nuint)displayStart, size) : string.Empty;

                            Console.Out.WriteLine($"{path}:{line}: {text}");
                            matched = true;
                        }

                        return matched;
                    });
            }
            finally
            {
                handle.ReleasePointer();
            }
        }

        // Single-pass replace: rewrites the file atomically if (and only if) it contains a match,
        // so files without matches are left untouched.
        private static unsafe bool ApplyReplaceFile(RegEx regex, string path, int defaultCodePage, string template)
        {
            string replaced;
            int codePage;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var length = stream.Length;
                if (length == 0)
                {
                    return false;
                }

                using var mmf = MemoryMappedFile.CreateFromFile(
                    stream, null, 0, MemoryMappedFileAccess.Read, null, HandleInheritability.None, leaveOpen: true);
                using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                var handle = view.SafeMemoryMappedViewHandle;

                byte* basePtr = null;
                handle.AcquirePointer(ref basePtr);
                try
                {
                    var data = basePtr + view.PointerOffset;

                    codePage = DetectCodePage(data, length, defaultCodePage, out _);
                    if (codePage != RegExCodePage.Utf16LE &&
                        codePage != RegExCodePage.Utf16BE &&
                        LooksBinary(data, length))
                    {
                        return false;
                    }

                    var input = new RegExInput(handle, (nuint)view.PointerOffset, (nuint)length, codePage);
                    if (!regex.Search(input, default, false, _ => true))
                    {
                        return false;
                    }

                    replaced = regex.Replace(input, template);
                }
                finally
                {
                    handle.ReleasePointer();
                }
            }

            // The file is unmapped now; rewrite it atomically in its original code page.
            var bytes = RegExEncoding.FromCodePage(codePage).GetBytes(replaced);
            WriteAllBytesAtomic(path, bytes);
            Console.Out.WriteLine($"{path}: updated");
            return true;
        }

        private static void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full)!;
            var temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temp, bytes);
            try
            {
                File.Replace(temp, full, null);
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // Best-effort cleanup.
                }

                throw;
            }
        }

        // Resolves the parsed default code page: turns the CP_ACP sentinel into the real ANSI code
        // page, then falls back to UTF-8 (with a warning) if the engine can't handle the result.
        private static int ResolveDefaultCodePage(int codePage)
        {
            if (codePage == RegExCodePage.SystemDefault)
            {
                codePage = NativeMethods.GetACP();
            }

            if (!RegEx.IsCodePageSupported(codePage))
            {
                Console.Error.WriteLine($"Code page {RegExCodePage.GetName(codePage)} is not supported, using UTF-8.");
                return RegExCodePage.Utf8;
            }

            return codePage;
        }

        private static IEnumerable<string> EnumerateFiles(IReadOnlyList<string> paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    yield return path;
                }
                else if (Directory.Exists(path))
                {
                    foreach (var file in EnumerateDirectory(path))
                    {
                        yield return file;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"{path}: no such file or directory");
                }
            }
        }

        private static IEnumerable<string> EnumerateDirectory(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                if (cancelled)
                {
                    yield break;
                }

                var current = stack.Pop();
                string[] files;
                string[] subdirectories;
                try
                {
                    files = Directory.GetFiles(current);
                    subdirectories = Directory.GetDirectories(current);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"{current}: {ex.Message}");
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                foreach (var subdirectory in subdirectories)
                {
                    stack.Push(subdirectory);
                }
            }
        }

        private static unsafe int DetectCodePage(byte* data, long length, int defaultCodePage, out int bomLength)
        {
            if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                bomLength = 3;
                return RegExCodePage.Utf8;
            }

            if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                bomLength = 2;
                return RegExCodePage.Utf16LE;
            }

            if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                bomLength = 2;
                return RegExCodePage.Utf16BE;
            }

            bomLength = 0;
            return defaultCodePage;
        }

        private static unsafe bool LooksBinary(byte* data, long length)
        {
            var n = (int)Math.Min(length, 8000);
            for (var i = 0; i < n; i++)
            {
                if (data[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CodeUnitSize(int codePage) =>
            codePage == RegExCodePage.Utf16LE || codePage == RegExCodePage.Utf16BE ? 2 : 1;

        private static unsafe bool IsAsciiUnit(byte* data, long index, long length, int codePage, byte value)
        {
            if (codePage == RegExCodePage.Utf16LE)
            {
                return index + 1 < length && data[index] == value && data[index + 1] == 0x00;
            }

            if (codePage == RegExCodePage.Utf16BE)
            {
                return index + 1 < length && data[index] == 0x00 && data[index + 1] == value;
            }

            return data[index] == value;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            internal static extern int GetACP();
        }
    }
}
