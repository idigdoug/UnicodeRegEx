namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// A compiled regular expression that runs over byte buffers in various text encodings
    /// (Latin-1, UTF-8, UTF-16) without round-tripping through a <see cref="string"/>.
    /// Create instances with <see cref="Create"/>.
    /// </summary>
    public struct RegEx
    {
        private static Interop.IRegExLibrary? library;
        private Interop.IRegEx inner;

        /// <summary>Callback that receives a successful match.</summary>
        public delegate void MatchAction(RegExMatch match);

        /// <summary>Callback that receives a successful match and returns a result.</summary>
        public delegate T MatchFunc<T>(RegExMatch match);

        /// <summary>Callback that receives an enumerator over the matches in the input.</summary>
        public delegate void EnumerateMatchesAction(RegExMatchEnumerator enumerator);

        /// <summary>Callback that receives an enumerator over the matches in the input and returns a result.</summary>
        public delegate T EnumerateMatchesFunc<T>(RegExMatchEnumerator enumerator);

        /// <summary>Callback that receives an enumerator over the matched and unmatched segments of the input.</summary>
        public delegate void EnumerateSegmentsAction(RegExSegmentEnumerator enumerator);

        /// <summary>Callback that receives an enumerator over the matched and unmatched segments of the input and returns a result.</summary>
        public delegate T EnumerateSegmentsFunc<T>(RegExSegmentEnumerator enumerator);

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
        /// Converts the input to a UTF-16 string.
        /// </summary>
        public static string Transcode(RegExInput input)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                return Transcode(input.Pin(ref pinScope), input.Encoding);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Converts the pinned input bytes (interpreted with <paramref name="inputEncoding"/>) to a UTF-16 string.
        /// </summary>
        public static string Transcode(RegExPinnedBytes inputBytes, RegExEncoding inputEncoding)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return Library.Transcode(bytes, (Interop.RegExEncoding)inputEncoding);
        }

        /// <summary>
        /// Converts the input to <paramref name="outputEncoding"/> and writes it to <paramref name="output"/>.
        /// </summary>
        public static void TranscodeTo(RegExInput input, Interop.ISequentialStream output, RegExEncoding outputEncoding)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                TranscodeTo(input.Pin(ref pinScope), input.Encoding, output, outputEncoding);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Converts the pinned input bytes (interpreted with <paramref name="inputEncoding"/>) to
        /// <paramref name="outputEncoding"/> and writes them to <paramref name="output"/>.
        /// </summary>
        public static void TranscodeTo(RegExPinnedBytes inputBytes, RegExEncoding inputEncoding, Interop.ISequentialStream output, RegExEncoding outputEncoding)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            Library.TranscodeTo(bytes, (Interop.RegExEncoding)inputEncoding, output, (Interop.RegExEncoding)outputEncoding);
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
                using var result = Match(input.Pin(ref pinScope), input.Encoding, options);
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
                using var result = Match(input.Pin(ref pinScope), input.Encoding, options);
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
            RegExEncoding inputEncoding,
            RegExMatchOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return new RegExMatchResult(inner.Match(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags), inputBytes);
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
                using var result = Search(input.Pin(ref pinScope), input.Encoding, options);
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
                using var result = Search(input.Pin(ref pinScope), input.Encoding, options);
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
            RegExEncoding inputEncoding,
            RegExMatchOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return new RegExMatchResult(inner.Search(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags), inputBytes);
        }

        /// <summary>
        /// Invokes <paramref name="enumerateCallback"/> with an enumerator over all matches in the input.
        /// </summary>
        public void EnumerateMatches(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateMatchesAction enumerateCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                using var enumerator = EnumerateMatches(input.Pin(ref pinScope), input.Encoding, options);
                enumerateCallback(enumerator);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Invokes <paramref name="enumerateCallback"/> with an enumerator over all matches in the input and returns its result.
        /// </summary>
        public T EnumerateMatches<T>(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateMatchesFunc<T> enumerateCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                using var enumerator = EnumerateMatches(input.Pin(ref pinScope), input.Encoding, options);
                return enumerateCallback(enumerator);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Creates an enumerator over all matches in the pre-pinned input bytes. The returned enumerator must be disposed.
        /// </summary>
        public RegExMatchEnumerator EnumerateMatches(
            RegExPinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            RegExEnumerateOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            var enumerator = inner.EnumerateMatches(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags);
            if (options.FormatTemplate != null)
            {
                enumerator.SetFormatTemplate(options.FormatTemplate, (Interop.RegExFormatFlags)options.FormatFlags);
            }

            return new RegExMatchEnumerator(enumerator, inputBytes);
        }

        /// <summary>
        /// Invokes <paramref name="enumerateCallback"/> with an enumerator over the input's matched and unmatched segments.
        /// </summary>
        public void EnumerateSegments(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateSegmentsAction enumerateCallback)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                using var enumerator = EnumerateSegments(input.Pin(ref pinScope), input.Encoding, options);
                enumerateCallback(enumerator);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Invokes <paramref name="enumerateCallback"/> with an enumerator over the input's matched and unmatched
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
                using var enumerator = EnumerateSegments(input.Pin(ref pinScope), input.Encoding, options);
                return enumerateCallback(enumerator);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Creates an enumerator over the matched and unmatched segments of the pre-pinned input bytes.
        /// The returned enumerator must be disposed.
        /// </summary>
        public RegExSegmentEnumerator EnumerateSegments(
            RegExPinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            RegExEnumerateOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            var enumerator = inner.EnumerateMatches(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags);
            if (options.FormatTemplate != null)
            {
                enumerator.SetFormatTemplate(options.FormatTemplate, (Interop.RegExFormatFlags)options.FormatFlags);
            }

            return new RegExSegmentEnumerator(enumerator, inputBytes);
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
                return Replace(input.Pin(ref pinScope), input.Encoding, formatTemplate, options);
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
            RegExEncoding inputEncoding,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return inner.Replace(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags, formatTemplate, (Interop.RegExFormatFlags)options.FormatFlags);
        }

        /// <summary>
        /// Replaces matches in the input using <paramref name="formatTemplate"/> and writes the result to
        /// <paramref name="outputStream"/> in <paramref name="outputEncoding"/>.
        /// </summary>
        public void ReplaceTo(
            RegExInput input,
            Interop.ISequentialStream outputStream,
            RegExEncoding outputEncoding,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            RegExInput.PinScope pinScope = default;
            try
            {
                ReplaceTo(input.Pin(ref pinScope), input.Encoding, outputStream, outputEncoding, formatTemplate, options);
            }
            finally
            {
                pinScope.Dispose();
            }
        }

        /// <summary>
        /// Replaces matches in the pre-pinned input bytes using <paramref name="formatTemplate"/> and writes the
        /// result to <paramref name="outputStream"/> in <paramref name="outputEncoding"/>.
        /// </summary>
        public void ReplaceTo(
            RegExPinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            Interop.ISequentialStream outputStream,
            RegExEncoding outputEncoding,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            inner.ReplaceTo(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags, formatTemplate, (Interop.RegExFormatFlags)options.FormatFlags, outputStream, (Interop.RegExEncoding)outputEncoding);
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
