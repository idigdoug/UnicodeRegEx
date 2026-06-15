namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;

    internal struct RegEx
    {
        private static Interop.IRegExLibrary? library;
        private Interop.IRegEx inner;

        public delegate void MatchAction(RegExMatch match);
        public delegate T MatchFunc<T>(RegExMatch match);
        public delegate void EnumerateMatchesAction(RegExMatchEnumerator enumerator);
        public delegate T EnumerateMatchesFunc<T>(RegExMatchEnumerator enumerator);
        public delegate void EnumerateSegmentsAction(RegExSegmentEnumerator enumerator);
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

        public static string EscapePatternLiteral(string patternLiteral, RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.ECMAScript)
        {
            return Library.EscapePatternLiteral(patternLiteral, (Interop.RegExSyntaxFlags)syntaxFlags);
        }

        public static string EscapeFormatLiteral(string formatLiteral, RegExFormatFlags formatFlags = RegExFormatFlags.Perl)
        {
            return Library.EscapeFormatLiteral(formatLiteral, (Interop.RegExFormatFlags)formatFlags);
        }

        public static string GetEscapePatternLiteralChars(RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.ECMAScript)
        {
            return Library.GetEscapePatternLiteralChars((Interop.RegExSyntaxFlags)syntaxFlags);
        }

        public static string GetEscapeFormatLiteralChars(RegExFormatFlags formatFlags = RegExFormatFlags.Perl)
        {
            return Library.GetEscapeFormatLiteralChars((Interop.RegExFormatFlags)formatFlags);
        }

        public static string Transcode(RegExInput input)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return Transcode(inputBytes, input.Encoding);
            }
        }

        public static string Transcode(RegExPinnedBytes inputBytes, RegExEncoding inputEncoding)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return Library.Transcode(bytes, (Interop.RegExEncoding)inputEncoding);
        }

        public static void TranscodeTo(RegExInput input, Interop.ISequentialStream output, RegExEncoding outputEncoding)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                TranscodeTo(inputBytes, input.Encoding, output, outputEncoding);
            }
        }

        public static void TranscodeTo(RegExPinnedBytes inputBytes, RegExEncoding inputEncoding, Interop.ISequentialStream output, RegExEncoding outputEncoding)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            Library.TranscodeTo(bytes, (Interop.RegExEncoding)inputEncoding, output, (Interop.RegExEncoding)outputEncoding);
        }

        public static RegExInterfaceWrapper<Interop.IRegExMemoryStream> CreateMemoryStream(int initialCapacity = 0)
        {
            return new RegExInterfaceWrapper<Interop.IRegExMemoryStream>(Library.CreateMemoryStream(initialCapacity));
        }

        public static RegExInterfaceWrapper<Interop.IRegExFileStream> CreateFileStream(string path, RegExFileStreamFlags flags)
        {
            return new RegExInterfaceWrapper<Interop.IRegExFileStream>(Library.CreateFileStream(path, (Interop.RegExFileStreamFlags)flags));
        }

        public static RegExInterfaceWrapper<Interop.IRegExFileStream> CreateReplacementFileStream(string finalPath)
        {
            return new RegExInterfaceWrapper<Interop.IRegExFileStream>(Library.CreateReplacementFileStream(finalPath));
        }

        // INSTANCE

        private RegEx(Interop.IRegEx inner)
        {
            this.inner = inner;
        }

        public string Pattern => inner.Pattern;

        public RegExSyntaxFlags Flags => (RegExSyntaxFlags)inner.Flags;

        public uint Lcid => inner.Lcid;

        public void Match(
            RegExInput input,
            RegExMatchOptions options,
            MatchAction matchCallback)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                var result = Match(inputBytes, input.Encoding, options);
                if (result.IsMatch)
                {
                    matchCallback(result.Match);
                }
            }
        }

        public T Match<T>(
            RegExInput input,
            RegExMatchOptions options,
            T noMatchReturnValue,
            MatchFunc<T> matchCallback)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                var result = Match(inputBytes, input.Encoding, options);
                return result.IsMatch ? matchCallback(result.Match) : noMatchReturnValue;
            }
        }

        public RegExMatchResult Match(
            RegExPinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            RegExMatchOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return new RegExMatchResult(inner.Match(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags), inputBytes);
        }

        public void Search(
            RegExInput input,
            RegExMatchOptions options,
            MatchAction matchCallback)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                var result = Search(inputBytes, input.Encoding, options);
                if (result.IsMatch)
                {
                    matchCallback(result.Match);
                }
            }
        }

        public T Search<T>(
            RegExInput input,
            RegExMatchOptions options,
            T noMatchReturnValue,
            MatchFunc<T> matchCallback)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                var result = Search(inputBytes, input.Encoding, options);
                return result.IsMatch ? matchCallback(result.Match) : noMatchReturnValue;
            }
        }

        public RegExMatchResult Search(
            RegExPinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            RegExMatchOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return new RegExMatchResult(inner.Search(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags), inputBytes);
        }

        public void EnumerateMatches(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateMatchesAction enumerateCallback)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                enumerateCallback(EnumerateMatches(inputBytes, input.Encoding, options));
            }
        }

        public T EnumerateMatches<T>(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateMatchesFunc<T> enumerateCallback)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return enumerateCallback(EnumerateMatches(inputBytes, input.Encoding, options));
            }
        }

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

        public void EnumerateSegments(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateSegmentsAction enumerateCallback)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                enumerateCallback(EnumerateSegments(inputBytes, input.Encoding, options));
            }
        }

        public T EnumerateSegments<T>(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateSegmentsFunc<T> enumerateCallback)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return enumerateCallback(EnumerateSegments(inputBytes, input.Encoding, options));
            }
        }

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

        public string Replace(
            RegExInput input,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return Replace(inputBytes, input.Encoding, formatTemplate, options);
            }
        }

        public string Replace(
            RegExPinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            var bytes = new Interop.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return inner.Replace(bytes, (Interop.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags, formatTemplate, (Interop.RegExFormatFlags)options.FormatFlags);
        }

        public void ReplaceTo(
            RegExInput input,
            Interop.ISequentialStream outputStream,
            RegExEncoding outputEncoding,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            RegExPinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                ReplaceTo(inputBytes, input.Encoding, outputStream, outputEncoding, formatTemplate, options);
            }
        }

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
