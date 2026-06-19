namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// A compiled regular expression that runs over byte buffers in various text code pages
    /// (Latin-1, UTF-8, UTF-16) without round-tripping through a <see cref="string"/>.
    /// Create instances with <see cref="Create"/>.
    ///
    /// Wraps a single underlying COM object, so copies of a <see cref="RegEx"/> share it:
    /// <see cref="Dispose"/> releases that object for every copy, and any match results or
    /// enumerables obtained from this regex must no longer be used afterward.
    /// </summary>
    public readonly struct RegEx : IDisposable
    {
        private static Interop.IRegExLibrary? library;
        private readonly Interop.IRegEx inner;

        /// <summary>Callback that receives a successful match.</summary>
        public delegate void MatchAction(RegExMatch match);

        /// <summary>Callback that receives a successful match and returns a result.</summary>
        public delegate T MatchFunc<T>(RegExMatch match);

        /// <summary>Callback that receives an enumerable over the matches in the input.</summary>
        public delegate void EnumerateMatchesAction(RegExMatchEnumerable matches);

        /// <summary>Callback that receives an enumerable over the matches in the input and returns a result.</summary>
        public delegate T EnumerateMatchesFunc<T>(RegExMatchEnumerable matches);

        /// <summary>Callback that receives an enumerable over the matched and unmatched segments of the input.</summary>
        public delegate void EnumerateSegmentsAction(RegExSegmentEnumerable segments);

        /// <summary>Callback that receives an enumerable over the matched and unmatched segments of the input and returns a result.</summary>
        public delegate T EnumerateSegmentsFunc<T>(RegExSegmentEnumerable segments);

        // STATIC

        private static Interop.IRegExLibrary Library
        {
            get
            {
                var value = library;
                if (value == null)
                {
                    int hr;
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X86:
                            hr = NativeMethods.X86.UnicodeRegExLibraryCreate(out value);
                            break;
                        case Architecture.X64:
                            hr = NativeMethods.X64.UnicodeRegExLibraryCreate(out value);
                            break;
                        case Architecture.Arm64:
                            hr = NativeMethods.Arm64.UnicodeRegExLibraryCreate(out value);
                            break;
                        default:
                            throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}");
                    }

                    if (hr < 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }

                    library = value;
                }

                return value;
            }
        }

        /// <summary>
        /// Compiles a regex pattern. Throws <see cref="RegExException"/> if the pattern is invalid.
        /// </summary>
        public static RegEx Create(
            string pattern,
            RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.ECMAScript,
            int lcid = 0)
        {
            const int MK_E_SYNTAX = unchecked((int)0x800401E4);

            Interop.RegExErrorCode errorCode = default;
            try
            {
                return new RegEx(Library.CreateRegEx(pattern, (Interop.RegExSyntaxFlags)syntaxFlags, (uint)lcid, out errorCode));
            }
            catch (COMException ex) when (ex.HResult == MK_E_SYNTAX)
            {
                throw new RegExException(pattern, syntaxFlags, (RegExErrorCode)errorCode, ex.Message);
            }
        }

        /// <summary>
        /// Returns a pattern that matches <paramref name="patternLiteral"/> literally, escaping any regex
        /// metacharacters for the given syntax.
        /// </summary>
        public static string EscapePatternLiteral(string patternLiteral, RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.ECMAScript)
        {
            return Library.EscapePatternLiteral(patternLiteral, (Interop.RegExSyntaxFlags)syntaxFlags);
        }

        /// <summary>
        /// Returns a replacement format string that inserts <paramref name="formatLiteral"/> literally,
        /// escaping any special replacement characters for the given format.
        /// </summary>
        public static string EscapeFormatLiteral(string formatLiteral, RegExFormatFlags formatFlags = RegExFormatFlags.Perl)
        {
            return Library.EscapeFormatLiteral(formatLiteral, (Interop.RegExFormatFlags)formatFlags);
        }

        /// <summary>
        /// Returns the set of metacharacters that <see cref="EscapePatternLiteral"/> escapes for the given syntax.
        /// </summary>
        public static string GetEscapePatternLiteralChars(RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.ECMAScript)
        {
            return Library.GetEscapePatternLiteralChars((Interop.RegExSyntaxFlags)syntaxFlags);
        }

        /// <summary>
        /// Returns the set of special replacement characters that <see cref="EscapeFormatLiteral"/> escapes for the given format.
        /// </summary>
        public static string GetEscapeFormatLiteralChars(RegExFormatFlags formatFlags = RegExFormatFlags.Perl)
        {
            return Library.GetEscapeFormatLiteralChars((Interop.RegExFormatFlags)formatFlags);
        }

        /// <summary>
        /// Returns true if the native library can encode and decode the specified text code page
        /// (UTF-8, UTF-16LE, UTF-16BE, or an installed single-byte code page). Special code page
        /// values such as CP_ACP (0) are not resolved; resolve them (for example with GetACP)
        /// before calling.
        /// </summary>
        public static bool IsCodePageSupported(int codePage)
        {
            return Library.IsCodePageSupported((uint)codePage);
        }

        /// <summary>
        /// Converts the input to a UTF-16 string.
        /// </summary>
        public static string Transcode(RegExInput input)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                return Transcode(input.Pin(ref pinScope), input.CodePage);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Converts the pinned input bytes (interpreted with <paramref name="inputCodePage"/>) to a UTF-16 string.
        /// </summary>
        public static string Transcode(RegExPinnedBytes inputBytes, int inputCodePage)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return Library.Transcode(bytes, (uint)inputCodePage);
        }

        /// <summary>
        /// Converts the input to <paramref name="outputCodePage"/> and writes it to <paramref name="output"/>.
        /// </summary>
        public static void TranscodeTo(RegExInput input, Interop.ISequentialStream output, int outputCodePage)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                TranscodeTo(input.Pin(ref pinScope), input.CodePage, output, outputCodePage);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Converts the pinned input bytes (interpreted with <paramref name="inputCodePage"/>) to
        /// <paramref name="outputCodePage"/> and writes them to <paramref name="output"/>.
        /// </summary>
        public static void TranscodeTo(RegExPinnedBytes inputBytes, int inputCodePage, Interop.ISequentialStream output, int outputCodePage)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            Library.TranscodeTo(bytes, (uint)inputCodePage, output, (uint)outputCodePage);
        }

        /// <summary>
        /// Creates an in-memory output stream. <paramref name="initialCapacity"/> is a sizing hint; pass 0 for the default.
        /// </summary>
        public static RegExMemoryStream CreateMemoryStream(int initialCapacity = 0)
        {
            return new RegExMemoryStream(Library.CreateMemoryStream(checked((uint)initialCapacity)));
        }

        /// <summary>
        /// Creates a file stream for the file at the specified path.
        /// </summary>
        public static RegExFileStream CreateFileStream(string path, RegExFileStreamFlags flags)
        {
            return new RegExFileStream(Library.CreateFileStream(path, (Interop.RegExFileStreamFlags)flags));
        }

        /// <summary>
        /// Creates a delete-on-close temporary file stream adjacent to <paramref name="finalPath"/>; commit the
        /// result by calling MoveTo on the returned stream.
        /// </summary>
        public static RegExFileStream CreateReplacementFileStream(string finalPath)
        {
            return new RegExFileStream(Library.CreateReplacementFileStream(finalPath));
        }

        // INSTANCE

        private RegEx(Interop.IRegEx inner)
        {
            this.inner = inner;
        }

        /// <summary>The pattern this regex was compiled from.</summary>
        public string Pattern => inner.Pattern;

        /// <summary>The syntax flags this regex was compiled with.</summary>
        public RegExSyntaxFlags Flags => (RegExSyntaxFlags)inner.Flags;

        /// <summary>The locale identifier (LCID) this regex was compiled with.</summary>
        public uint Lcid => inner.Lcid;

        /// <summary>
        /// Releases the underlying compiled-regex COM object. Safe to call on a default-initialized
        /// <see cref="RegEx"/> and safe to call more than once. Because copies of a <see cref="RegEx"/>
        /// share the same underlying object, dispose only when no copy (and no match result or
        /// enumerable obtained from it) is still in use.
        /// </summary>
        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }

        /// <summary>
        /// Anchored match against the input. If it matches, invokes <paramref name="matchCallback"/> with the result.
        /// </summary>
        public void Match(
            RegExInput input,
            RegExMatchOptions options,
            MatchAction matchCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                using var result = Match(input.Pin(ref pinScope), input.CodePage, options);
                if (result.IsMatch)
                {
                    matchCallback(result.Match);
                }
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Anchored match against the input. Returns <paramref name="matchCallback"/>'s result if it matches,
        /// otherwise <paramref name="noMatchReturnValue"/>.
        /// </summary>
        public T Match<T>(
            RegExInput input,
            RegExMatchOptions options,
            T noMatchReturnValue,
            MatchFunc<T> matchCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                using var result = Match(input.Pin(ref pinScope), input.CodePage, options);
                return result.IsMatch ? matchCallback(result.Match) : noMatchReturnValue;
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Anchored match against pre-pinned input bytes. The returned <see cref="RegExMatchResult"/> must be disposed.
        /// </summary>
        public RegExMatchResult Match(
            RegExPinnedBytes inputBytes,
            int inputCodePage,
            RegExMatchOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return new RegExMatchResult(inner.Match(bytes, (uint)inputCodePage, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags), inputBytes);
        }

        /// <summary>
        /// Searches the input for the first match. If found, invokes <paramref name="matchCallback"/> with the result.
        /// </summary>
        public void Search(
            RegExInput input,
            RegExMatchOptions options,
            MatchAction matchCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                using var result = Search(input.Pin(ref pinScope), input.CodePage, options);
                if (result.IsMatch)
                {
                    matchCallback(result.Match);
                }
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Searches the input for the first match. Returns <paramref name="matchCallback"/>'s result if found,
        /// otherwise <paramref name="noMatchReturnValue"/>.
        /// </summary>
        public T Search<T>(
            RegExInput input,
            RegExMatchOptions options,
            T noMatchReturnValue,
            MatchFunc<T> matchCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                using var result = Search(input.Pin(ref pinScope), input.CodePage, options);
                return result.IsMatch ? matchCallback(result.Match) : noMatchReturnValue;
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Searches pre-pinned input bytes for the first match. The returned <see cref="RegExMatchResult"/> must be disposed.
        /// </summary>
        public RegExMatchResult Search(
            RegExPinnedBytes inputBytes,
            int inputCodePage,
            RegExMatchOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return new RegExMatchResult(inner.Search(bytes, (uint)inputCodePage, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags), inputBytes);
        }

        /// <summary>
        /// Invokes <paramref name="enumerateCallback"/> with an enumerable over all matches in the input.
        /// </summary>
        public void EnumerateMatches(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateMatchesAction enumerateCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                enumerateCallback(EnumerateMatches(input.Pin(ref pinScope), input.CodePage, options));
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Invokes <paramref name="enumerateCallback"/> with an enumerable over all matches in the input and returns its result.
        /// </summary>
        public T EnumerateMatches<T>(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateMatchesFunc<T> enumerateCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                return enumerateCallback(EnumerateMatches(input.Pin(ref pinScope), input.CodePage, options));
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Returns a re-enumerable view of all matches in the pre-pinned input bytes. The input must
        /// stay pinned for the duration of each enumeration.
        /// </summary>
        public RegExMatchEnumerable EnumerateMatches(
            RegExPinnedBytes inputBytes,
            int inputCodePage,
            RegExEnumerateOptions options = default)
        {
            return new RegExMatchEnumerable(inner, inputBytes, inputCodePage, options);
        }

        /// <summary>
        /// Invokes <paramref name="enumerateCallback"/> with an enumerable over the input's matched and unmatched segments.
        /// </summary>
        public void EnumerateSegments(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateSegmentsAction enumerateCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                enumerateCallback(EnumerateSegments(input.Pin(ref pinScope), input.CodePage, options));
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Invokes <paramref name="enumerateCallback"/> with an enumerable over the input's matched and unmatched
        /// segments and returns its result.
        /// </summary>
        public T EnumerateSegments<T>(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateSegmentsFunc<T> enumerateCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                return enumerateCallback(EnumerateSegments(input.Pin(ref pinScope), input.CodePage, options));
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Returns a re-enumerable view of the matched and unmatched segments of the pre-pinned input
        /// bytes. The input must stay pinned for the duration of each enumeration.
        /// </summary>
        public RegExSegmentEnumerable EnumerateSegments(
            RegExPinnedBytes inputBytes,
            int inputCodePage,
            RegExEnumerateOptions options = default)
        {
            return new RegExSegmentEnumerable(inner, inputBytes, inputCodePage, options);
        }

        /// <summary>
        /// Replaces matches in the input using <paramref name="formatTemplate"/> and returns the result as a UTF-16 string.
        /// </summary>
        public string Replace(
            RegExInput input,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                return Replace(input.Pin(ref pinScope), input.CodePage, formatTemplate, options);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Replaces matches in the pre-pinned input bytes using <paramref name="formatTemplate"/> and returns the
        /// result as a UTF-16 string.
        /// </summary>
        public string Replace(
            RegExPinnedBytes inputBytes,
            int inputCodePage,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return inner.Replace(bytes, (uint)inputCodePage, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags, formatTemplate, (Interop.RegExFormatFlags)options.FormatFlags);
        }

        /// <summary>
        /// Replaces matches in the input using <paramref name="formatTemplate"/> and writes the result to
        /// <paramref name="outputStream"/> in <paramref name="outputCodePage"/>.
        /// </summary>
        public void ReplaceTo(
            RegExInput input,
            Interop.ISequentialStream outputStream,
            int outputCodePage,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                ReplaceTo(input.Pin(ref pinScope), input.CodePage, outputStream, outputCodePage, formatTemplate, options);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Replaces matches in the pre-pinned input bytes using <paramref name="formatTemplate"/> and writes the
        /// result to <paramref name="outputStream"/> in <paramref name="outputCodePage"/>.
        /// </summary>
        public void ReplaceTo(
            RegExPinnedBytes inputBytes,
            int inputCodePage,
            Interop.ISequentialStream outputStream,
            int outputCodePage,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            inner.ReplaceTo(bytes, (uint)inputCodePage, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags, formatTemplate, (Interop.RegExFormatFlags)options.FormatFlags, outputStream, (uint)outputCodePage);
        }

        // PRIVATE

        private static class NativeMethods
        {
            public static class X86
            {
                private const string UnicodeRegExLib = "UnicodeRegEx_x86.dll";

                [DllImport(UnicodeRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int UnicodeRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out Interop.IRegExLibrary library);
            }

            public static class X64
            {
                private const string UnicodeRegExLib = "UnicodeRegEx_x64.dll";

                [DllImport(UnicodeRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int UnicodeRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out Interop.IRegExLibrary library);
            }

            public static class Arm64
            {
                private const string UnicodeRegExLib = "UnicodeRegEx_ARM64.dll";

                [DllImport(UnicodeRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int UnicodeRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out Interop.IRegExLibrary library);
            }
        }
    }
}
