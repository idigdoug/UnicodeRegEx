namespace msandbox
{
    using System;
    using System.Runtime.InteropServices;

    /*
    Ideas:

    // Yields alternating Unmatched / Match segments across the whole input,
    // including the trailing unmatched tail. Decides nothing — just exposes structure.
    ref struct SegmentEnumerator {
        Segment Current;  // { bool IsMatch; nuint Offset; nuint Size; RegExMatchResults Match; }
        bool MoveNext();
    }

    // Binds (stream, encoding) so every write agrees on encoding. No policy, just consistency.
    readonly struct RegExWriter {        // wraps ISequentialStream + RegExEncoding
        void WriteInput(RegExMatchResults r, nuint offset, nuint size);  // -> CopyInputTo(..., boundEncoding)
        void WriteFormat(RegExMatchResults r);                            // -> FormatTo(boundEncoding)
        void WriteBytes(PinnedBytes raw);                                 // verbatim escape hatch
    }
    */

    internal struct RegEx
    {
        private static RepStrRegEx.IRegExLibrary? library;
        private RepStrRegEx.IRegEx inner;

        public delegate void MatchAction(RegExMatchResults matchResults);
        public delegate T MatchFunc<T>(RegExMatchResults matchResults);
        public delegate void EnumerateAction(RegExMatchEnumerator enumerator);
        public delegate T EnumerateFunc<T>(RegExMatchEnumerator enumerator);

        // STATIC

        private static RepStrRegEx.IRegExLibrary Library
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
                            hr = NativeMethods.X86.RepStrRegExLibraryCreate(out value);
                            break;
                        case Architecture.X64:
                            hr = NativeMethods.X64.RepStrRegExLibraryCreate(out value);
                            break;
                        case Architecture.Arm64:
                            hr = NativeMethods.Arm64.RepStrRegExLibraryCreate(out value);
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

            RepStrRegEx.RegExErrorCode errorCode = default;
            try
            {
                return new RegEx(Library.CreateRegEx(pattern, (RepStrRegEx.RegExSyntaxFlags)syntaxFlags, (uint)lcid, out errorCode));
            }
            catch (COMException ex) when (ex.HResult == MK_E_SYNTAX)
            {
                throw new RegExException(pattern, syntaxFlags, (RegExErrorCode)errorCode, ex.Message);
            }
        }

        public static string EscapePatternLiteral(string patternLiteral, RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.ECMAScript)
        {
            return Library.EscapePatternLiteral(patternLiteral, (RepStrRegEx.RegExSyntaxFlags)syntaxFlags);
        }

        public static string EscapeFormatLiteral(string formatLiteral, RegExFormatFlags formatFlags = RegExFormatFlags.Perl)
        {
            return Library.EscapeFormatLiteral(formatLiteral, (RepStrRegEx.RegExFormatFlags)formatFlags);
        }

        public static string GetEscapePatternLiteralChars(RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.ECMAScript)
        {
            return Library.GetEscapePatternLiteralChars((RepStrRegEx.RegExSyntaxFlags)syntaxFlags);
        }

        public static string GetEscapeFormatLiteralChars(RegExFormatFlags formatFlags = RegExFormatFlags.Perl)
        {
            return Library.GetEscapeFormatLiteralChars((RepStrRegEx.RegExFormatFlags)formatFlags);
        }

        public static string Transcode(RegExInput input)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return Transcode(inputBytes, input.Encoding);
            }
        }

        public static string Transcode(PinnedBytes inputBytes, RegExEncoding inputEncoding)
        {
            var bytes = new RepStrRegEx.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return Library.Transcode(bytes, (RepStrRegEx.RegExEncoding)inputEncoding);
        }

        public static void TranscodeTo(RegExInput input, RepStrRegEx.ISequentialStream output, RegExEncoding outputEncoding)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                TranscodeTo(inputBytes, input.Encoding, output, outputEncoding);
            }
        }

        public static void TranscodeTo(PinnedBytes inputBytes, RegExEncoding inputEncoding, RepStrRegEx.ISequentialStream output, RegExEncoding outputEncoding)
        {
            var bytes = new RepStrRegEx.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            Library.TranscodeTo(bytes, (RepStrRegEx.RegExEncoding)inputEncoding, output, (RepStrRegEx.RegExEncoding)outputEncoding);
        }

        public static RegExInterfaceWrapper<RepStrRegEx.IRegExMemoryStream> CreateMemoryStream(int initialCapacity = 0)
        {
            return new RegExInterfaceWrapper<RepStrRegEx.IRegExMemoryStream>(Library.CreateMemoryStream(initialCapacity));
        }

        public static RegExInterfaceWrapper<RepStrRegEx.IRegExFileStream> CreateFileStream(string path, RegExFileStreamFlags flags)
        {
            return new RegExInterfaceWrapper<RepStrRegEx.IRegExFileStream>(Library.CreateFileStream(path, (RepStrRegEx.RegExFileStreamFlags)flags));
        }

        public static RegExInterfaceWrapper<RepStrRegEx.IRegExFileStream> CreateReplacementFileStream(string finalPath)
        {
            return new RegExInterfaceWrapper<RepStrRegEx.IRegExFileStream>(Library.CreateReplacementFileStream(finalPath));
        }

        // INSTANCE

        private RegEx(RepStrRegEx.IRegEx inner)
        {
            this.inner = inner;
        }

        public string Pattern => inner.Pattern;

        public RegExSyntaxFlags Flags => (RegExSyntaxFlags)inner.Flags;

        public uint Lcid => inner.Lcid;

        public void Match(
            RegExInput input,
            RegExMatchOptions options,
            MatchAction callback)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                callback(Match(inputBytes, input.Encoding, options));
            }
        }

        public T Match<T>(
            RegExInput input,
            RegExMatchOptions options,
            MatchFunc<T> callback)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return callback(Match(inputBytes, input.Encoding, options));
            }
        }

        public RegExMatch Match(
            PinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            RegExMatchOptions options = default)
        {
            var bytes = new RepStrRegEx.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return new RegExMatch(inner.Match(bytes, (RepStrRegEx.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (RepStrRegEx.RegExMatchFlags)options.MatchFlags), inputBytes);
        }

        public void Search(
            RegExInput input,
            RegExMatchOptions options,
            MatchAction callback)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                callback(Search(inputBytes, input.Encoding, options));
            }
        }

        public T Search<T>(
            RegExInput input,
            RegExMatchOptions options,
            MatchFunc<T> callback)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return callback(Search(inputBytes, input.Encoding, options));
            }
        }

        public RegExMatch Search(
            PinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            RegExMatchOptions options = default)
        {
            var bytes = new RepStrRegEx.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return new RegExMatch(inner.Search(bytes, (RepStrRegEx.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (RepStrRegEx.RegExMatchFlags)options.MatchFlags), inputBytes);
        }

        public void EnumerateMatches(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateAction callback)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                callback(EnumerateMatches(inputBytes, input.Encoding, options));
            }
        }

        public T EnumerateMatches<T>(
            RegExInput input,
            RegExEnumerateOptions options,
            EnumerateFunc<T> callback)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return callback(EnumerateMatches(inputBytes, input.Encoding, options));
            }
        }

        public RegExMatchEnumerator EnumerateMatches(
            PinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            RegExEnumerateOptions options = default)
        {
            var bytes = new RepStrRegEx.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            var enumerator = inner.EnumerateMatches(bytes, (RepStrRegEx.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (RepStrRegEx.RegExMatchFlags)options.MatchFlags);
            if (options.FormatTemplate != null)
            {
                enumerator.SetFormatTemplate(options.FormatTemplate, (RepStrRegEx.RegExFormatFlags)options.FormatFlags);
            }

            return new RegExMatchEnumerator(enumerator, inputBytes);
        }

        public string Replace(
            RegExInput input,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                return Replace(inputBytes, input.Encoding, formatTemplate, options);
            }
        }

        public string Replace(
            PinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            var bytes = new RepStrRegEx.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            return inner.Replace(bytes, (RepStrRegEx.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (RepStrRegEx.RegExMatchFlags)options.MatchFlags, formatTemplate, (RepStrRegEx.RegExFormatFlags)options.FormatFlags);
        }

        public void ReplaceTo(
            RegExInput input,
            RepStrRegEx.ISequentialStream outputStream,
            RegExEncoding outputEncoding,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            PinnedBytes inputBytes;
            using (var pin = input.Pin(out inputBytes))
            {
                ReplaceTo(inputBytes, input.Encoding, outputStream, outputEncoding, formatTemplate, options);
            }
        }

        public void ReplaceTo(
            PinnedBytes inputBytes,
            RegExEncoding inputEncoding,
            RepStrRegEx.ISequentialStream outputStream,
            RegExEncoding outputEncoding,
            string formatTemplate,
            RegExReplaceOptions options = default)
        {
            var bytes = new RepStrRegEx.RegExBytes { data = (nint)inputBytes.Data, size = (nint)inputBytes.Size };
            inner.ReplaceTo(bytes, (RepStrRegEx.RegExEncoding)inputEncoding, (long)options.StartByteOffset, (RepStrRegEx.RegExMatchFlags)options.MatchFlags, formatTemplate, (RepStrRegEx.RegExFormatFlags)options.FormatFlags, outputStream, (RepStrRegEx.RegExEncoding)outputEncoding);
        }

        // PRIVATE

        private static class NativeMethods
        {
            public static class X86
            {
                private const string RepStrRegExLib = "RepStrRegEx_x86.dll";

                [DllImport(RepStrRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int RepStrRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out RepStrRegEx.IRegExLibrary library);
            }

            public static class X64
            {
                private const string RepStrRegExLib = "RepStrRegEx_x64.dll";

                [DllImport(RepStrRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int RepStrRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out RepStrRegEx.IRegExLibrary library);
            }

            public static class Arm64
            {
                private const string RepStrRegExLib = "RepStrRegEx_ARM64.dll";

                [DllImport(RepStrRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int RepStrRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out RepStrRegEx.IRegExLibrary library);
            }
        }
    }
}
