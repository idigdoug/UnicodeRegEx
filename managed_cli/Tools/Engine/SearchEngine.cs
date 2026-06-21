namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Threading;
    using UnicodeRegEx;

    /// <summary>
    /// Executes a <see cref="SearchRequest"/> over its files, pushing hits/changes/errors to an
    /// <see cref="ISearchSink"/> as they happen and returning a <see cref="SearchSummary"/>.
    /// Front-end-neutral: the CLI streams the results to the console; a GUI feeds them into a live
    /// view. The request carries the resolved default code page
    /// (<see cref="SearchRequest.ResolvedDefaultCodePage"/>); callers should
    /// <see cref="SearchRequest.Validate"/> it first (an unsupported code page is reported there).
    /// </summary>
    public sealed class SearchEngine
    {
        public SearchSummary Run(SearchRequest request, ISearchSink sink, CancellationToken cancellation)
        {
            // An invalid pattern is a setup failure for the whole run; it propagates to the caller.
            // Per-file failures are caught below and reported as errors without aborting the run.
            using var regex = RegEx.Create(request.Pattern, request.SyntaxFlags);

            var anyMatch = false;
            var filesChanged = 0;
            var errors = 0;
            var cancelled = false;

            foreach (var path in EnumerateFiles(request.Paths, sink, cancellation))
            {
                if (cancellation.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                try
                {
                    if (request.Apply)
                    {
                        if (ApplyReplaceFile(regex, path, request, sink))
                        {
                            filesChanged++;
                            anyMatch = true;
                        }
                    }
                    else if (MatchFile(regex, path, request, sink))
                    {
                        anyMatch = true;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    sink.OnError(path, ex.Message);
                }
            }

            return new SearchSummary(anyMatch, filesChanged, errors, cancelled);
        }

        private static unsafe bool MatchFile(RegEx regex, string path, SearchRequest request, ISearchSink sink)
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
            try
            {
                handle.AcquirePointer(ref basePtr);
                var data = basePtr + view.PointerOffset;

                var codePage = DetectCodePage(data, length, request.ResolvedDefaultCodePage, out var bomLength);
                if (codePage != RegExCodePage.Utf16LE &&
                    codePage != RegExCodePage.Utf16BE &&
                    LooksBinary(data, length))
                {
                    return false;
                }

                var unit = CodeUnitSize(codePage);

                // Lambdas can't capture pointers, so pass the view's base address as an IntPtr.
                var dataAddress = (IntPtr)data;

                var replaceTemplate = request.ReplaceTemplate;
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
                        long lastHitLineStart = -1;
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

                            // In replace-preview mode, emit each match's replacement and move on.
                            if (replaceTemplate != null)
                            {
                                sink.OnHit(new SearchHit(path, line, match.Text, match.Format()));
                                matched = true;
                                continue;
                            }

                            // Search mode: emit each matching line at most once.
                            if (curLineStart == lastHitLineStart)
                            {
                                continue;
                            }

                            lastHitLineStart = curLineStart;

                            var lineEnd = begin;
                            while (lineEnd < length && !IsAsciiUnit(bytes, lineEnd, length, codePage, 0x0A))
                            {
                                lineEnd += unit;
                            }

                            // Drop a trailing CR so CRLF lines don't carry a stray carriage return.
                            if (lineEnd - unit >= curLineStart && IsAsciiUnit(bytes, lineEnd - unit, length, codePage, 0x0D))
                            {
                                lineEnd -= unit;
                            }

                            var displayStart = curLineStart == 0 && bomLength > 0 ? bomLength : curLineStart;
                            var size = lineEnd > displayStart ? checked((int)(lineEnd - displayStart)) : 0;
                            var text = size > 0 ? match.CopyInput((nuint)displayStart, size) : string.Empty;

                            sink.OnHit(new SearchHit(path, line, text, null));
                            matched = true;
                        }

                        return matched;
                    });
            }
            finally
            {
                if (basePtr != null)
                {
                    handle.ReleasePointer();
                }
            }
        }

        // Single-pass replace: rewrites the file atomically if (and only if) it contains a match,
        // so files without matches are left untouched.
        private static unsafe bool ApplyReplaceFile(RegEx regex, string path, SearchRequest request, ISearchSink sink)
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
                try
                {
                    handle.AcquirePointer(ref basePtr);
                    var data = basePtr + view.PointerOffset;

                    codePage = DetectCodePage(data, length, request.ResolvedDefaultCodePage, out _);
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

                    replaced = regex.Replace(input, request.ReplaceTemplate!);
                }
                finally
                {
                    if (basePtr != null)
                    {
                        handle.ReleasePointer();
                    }
                }
            }

            // The file is unmapped now; rewrite it atomically in its original code page.
            var bytes = RegExEncoding.FromCodePage(codePage).GetBytes(replaced);
            WriteAllBytesAtomic(path, bytes);
            sink.OnFileChanged(path);
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

        private static IEnumerable<string> EnumerateFiles(IEnumerable<string> paths, ISearchSink sink, CancellationToken cancellation)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    yield return path;
                }
                else if (Directory.Exists(path))
                {
                    foreach (var file in EnumerateDirectory(path, sink, cancellation))
                    {
                        yield return file;
                    }
                }
                else
                {
                    sink.OnError(path, "no such file or directory");
                }
            }
        }

        private static IEnumerable<string> EnumerateDirectory(string root, ISearchSink sink, CancellationToken cancellation)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                if (cancellation.IsCancellationRequested)
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
                    sink.OnError(current, ex.Message);
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
