namespace UnicodeRegEx.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Runtime.InteropServices;
    using UnicodeRegEx;

    /// <summary>
    /// A minimal grep-style search over files, used to break ground on the CLI. Each file is
    /// memory-mapped and searched in its own text code page (detected from a byte-order mark,
    /// otherwise the default selected with --encoding) without decoding it to a managed string.
    /// </summary>
    internal static class Program
    {
        // Code page used when --encoding is omitted, unknown, or names a page the native
        // library can't handle. UTF-8 covers most source files and is always supported.
        private const int FallbackCodePage = RegExCodePage.Utf8;

        private static volatile bool cancelled;

        [DllImport("kernel32.dll")]
        private static extern uint GetACP();

        private static int Main(string[] args)
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancelled = true;
            };

            if (!TryParseArgs(args, out var pattern, out var paths, out var ignoreCase, out var encodingSpec))
            {
                Console.Error.WriteLine("Usage: UnicodeRegExCli <pattern> [path]... [-i|--ignore-case] [-e|--encoding utf8|acp|<codepage>]");
                return 2;
            }

            RegEx regex;
            try
            {
                var syntaxFlags = RegExSyntaxFlags.ECMAScript;
                if (ignoreCase)
                {
                    syntaxFlags |= RegExSyntaxFlags.ICase;
                }

                regex = RegEx.Create(pattern, syntaxFlags);
            }
            catch (RegExException ex)
            {
                Console.Error.WriteLine($"Invalid pattern '{pattern}': {ex.Message}");
                return 2;
            }

            var defaultCodePage = ResolveDefaultCodePage(encodingSpec);

            var anyMatch = false;
            var errors = 0;
            try
            {
                foreach (var file in EnumerateFiles(paths))
                {
                    if (cancelled)
                    {
                        break;
                    }

                    try
                    {
                        anyMatch |= GrepFile(regex, file, defaultCodePage);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Console.Error.WriteLine($"{file}: {ex.Message}");
                    }
                }
            }
            finally
            {
                regex.Dispose();
            }

            if (errors > 0)
            {
                return 2;
            }

            return anyMatch ? 0 : 1;
        }

        private static unsafe bool GrepFile(RegEx regex, string path, int defaultCodePage)
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

                return regex.EnumerateMatches(
                    new RegExInput(handle, (nuint)view.PointerOffset, (nuint)length, codePage),
                    default,
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

                            // Print each matching line at most once, like grep.
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

                            Console.WriteLine($"{path}:{line}: {text}");
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

        private static bool TryParseArgs(
            string[] args,
            out string pattern,
            out List<string> paths,
            out bool ignoreCase,
            out string? encodingSpec)
        {
            pattern = string.Empty;
            paths = new List<string>();
            ignoreCase = false;
            encodingSpec = null;

            var havePattern = false;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "-i" || arg == "--ignore-case")
                {
                    ignoreCase = true;
                }
                else if (arg == "-e" || arg == "--encoding")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"{arg}: missing code page value");
                        return false;
                    }

                    encodingSpec = args[++i];
                }
                else if (arg.StartsWith("--encoding=", StringComparison.Ordinal))
                {
                    encodingSpec = arg.Substring("--encoding=".Length);
                }
                else if (!havePattern)
                {
                    pattern = arg;
                    havePattern = true;
                }
                else
                {
                    paths.Add(arg);
                }
            }

            if (!havePattern)
            {
                return false;
            }

            if (paths.Count == 0)
            {
                paths.Add(".");
            }

            return true;
        }

        // Resolves the --encoding value (utf8 | acp | <codepage>) to a code page the native
        // library can handle, falling back to UTF-8 (with a warning) when it can't.
        private static int ResolveDefaultCodePage(string? encodingSpec)
        {
            if (string.IsNullOrEmpty(encodingSpec))
            {
                return FallbackCodePage;
            }

            var spec = encodingSpec!;
            int codePage;
            switch (spec.ToLowerInvariant())
            {
                case "utf8":
                case "utf-8":
                    return RegExCodePage.Utf8;
                case "acp":
                case "ansi":
                    codePage = (int)GetACP();
                    break;
                case "latin1":
                case "iso-8859-1":
                    codePage = RegExCodePage.Latin1;
                    break;
                default:
                    if (!int.TryParse(spec, out codePage))
                    {
                        Console.Error.WriteLine($"Unknown encoding '{spec}', using UTF-8.");
                        return FallbackCodePage;
                    }

                    break;
            }

            if (!RegEx.IsCodePageSupported(codePage))
            {
                Console.Error.WriteLine($"Code page {codePage} is not supported, using UTF-8.");
                return FallbackCodePage;
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
    }
}
