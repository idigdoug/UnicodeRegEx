namespace msandbox
{
    using RepStrRegEx;
    using System;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Exception thrown by RegEx.Create when pattern parsing fails.
    /// </summary>
    internal class RegExException : Exception
    {
        private readonly string pattern;
        private readonly RegExSyntaxFlags syntaxFlags;
        private readonly RegExErrorCode errorCode;
        private readonly string nativeMessage;

        public string Pattern { get { return pattern; } }
        public RegExSyntaxFlags SyntaxFlags { get { return syntaxFlags; } }
        public RegExErrorCode ErrorCode { get { return errorCode; } }
        public string NativeMessage { get { return nativeMessage; } }

        public RegExException(string pattern, RegExSyntaxFlags syntaxFlags, RegExErrorCode errorCode, string? nativeMessage)
            : base(FormatMessage(pattern, errorCode, nativeMessage))
        {
            this.pattern = pattern;
            this.syntaxFlags = syntaxFlags;
            this.errorCode = errorCode;
            this.nativeMessage = nativeMessage ?? errorCode.ToString();
        }

        private static string FormatMessage(string pattern, RegExErrorCode errorCode, string? nativeMessage)
        {
            if (nativeMessage == null)
            {
                return $"Failed to compile regex ({errorCode}): {pattern}";
            }
            else
            {
                return $"Failed to compile regex ({errorCode}, {nativeMessage}): {pattern}";
            }
        }
    }

    internal static class RegExExtensions
    {
        public static IntPtr Data(in this RegExBytes self) => unchecked((IntPtr)self.data);
        public static unsafe byte* DataPtr(in this RegExBytes self) => (byte*)unchecked((IntPtr)self.data);
        public static IntPtr Size(in this RegExBytes self) => unchecked((IntPtr)self.size);
        public static int SizeInt(in this RegExBytes self) => checked((int)self.size);

        /// <summary>
        /// Returns a string for the bytes contained in the RegExBytes buffer.
        /// The buffer is valid until the next call into the match enumerator.
        /// </summary>
        public static string GetString(this RegExBytes self, RegExEncoding encoding)
        {
            unsafe
            {
                switch (encoding)
                {
                    case RegExEncoding.RegExEncoding_latin1:
                        return RegEx.EncodingLatin1.GetString(self.DataPtr(), self.SizeInt());
                    case RegExEncoding.RegExEncoding_utf8:
                        return Encoding.UTF8.GetString(self.DataPtr(), self.SizeInt());
                    case RegExEncoding.RegExEncoding_utf16le:
                        return Marshal.PtrToStringUni(self.Data(), self.SizeInt() / sizeof(char));
                    case RegExEncoding.RegExEncoding_utf16be:
                        return Encoding.BigEndianUnicode.GetString(self.DataPtr(), self.SizeInt());
                    case RegExEncoding.RegExEncoding_none:
                    default:
                        throw new NotSupportedException($"Unsupported regex encoding: {encoding}");
                }
            }
        }

        public static IntPtr Offset(in this RegExSubMatch self) => unchecked((IntPtr)self.offset);
        public static int OffsetInt(in this RegExSubMatch self) => checked((int)self.offset);
        public static IntPtr Size(in this RegExSubMatch self) => unchecked((IntPtr)self.size);
        public static int SizeInt(in this RegExSubMatch self) => checked((int)self.size);
        public static bool Matched(in this RegExSubMatch self) => self.matched != 0;
        public static RegExBytes ToBytes(in this RegExSubMatch self, in RegExBytes input)
        {
            if (self.offset > input.size || self.size > input.size - self.offset)
            {
                throw new ArgumentOutOfRangeException(nameof(self), "Submatch is out of bounds of the input.");
            }
            return new RegExBytes
            {
                data = input.data + self.offset,
                size = self.size
            };
        }

        /// <summary>
        /// Iterate matches over a string (will be pinned for the duration of iteration).
        /// </summary>
        public static RegExMatchEnumerator MatchEnumerator(
            this IRegEx self,
            string data,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default,
            string? formatTemplate = null,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            => new RegExMatchEnumerator(self, data, matchFlags, formatTemplate, formatFlags);

        /// <summary>
        /// Iterate matches over a byte array + encoding (will be pinned for the duration of iteration).
        /// </summary>
        public static RegExMatchEnumerator MatchEnumerator(
            this IRegEx self,
            byte[] data,
            RegExEncoding encoding,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default,
            string? formatTemplate = null,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            => new RegExMatchEnumerator(self, data, encoding, matchFlags, formatTemplate, formatFlags);

        /// <summary>
        /// Iterate matches over pinned bytes + encoding.
        /// The caller must ensure the data remains valid for the lifetime of enumeration.
        /// </summary>
        public static unsafe RegExMatchEnumerator MatchEnumerator(
            this IRegEx self,
            IntPtr data,
            IntPtr size,
            RegExEncoding encoding,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default,
            string? formatTemplate = null,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            => new RegExMatchEnumerator(self, data, size, encoding, matchFlags, formatTemplate, formatFlags);

        /// <summary>
        /// Iterate matches over pinned bytes + encoding.
        /// The caller must ensure the data remains valid for the lifetime of enumeration.
        /// </summary>
        public static unsafe RegExMatchEnumerator MatchEnumerator(
            this IRegEx self,
            void* data,
            IntPtr size,
            RegExEncoding encoding,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default,
            string? formatTemplate = null,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            => new RegExMatchEnumerator(self, data, size, encoding, matchFlags, formatTemplate, formatFlags);
    }

    internal ref struct RegExMatchEnumerator
    {
        private GCHandle pin;
        private IRegExMatchEnumerator enumerator;

        private RegExMatchEnumerator(
            IRegEx regex,
            GCHandle pin,
            IntPtr data,
            IntPtr size,
            RegExEncoding encoding,
            RegExMatchFlags matchFlags,
            string? formatTemplate,
            RegExFormatFlags formatFlags)
        {
            this.pin = pin;
            var inputString = RegEx.Bytes(
                pin.IsAllocated ? pin.AddrOfPinnedObject() : data,
                size);
            this.enumerator = regex.EnumerateMatches(ref inputString, encoding, 0, matchFlags);
            if (formatTemplate != null)
            {
                enumerator.SetFormatTemplate(formatTemplate, formatFlags);
            }
        }

        /// <summary>
        /// Creates an enumerator that pins a string for the duration of iteration.
        /// </summary>
        public RegExMatchEnumerator(IRegEx regex, string data, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            : this(regex, GCHandle.Alloc(data, GCHandleType.Pinned), (IntPtr)0, (IntPtr)(data.Length * sizeof(char)), RegExEncoding.RegExEncoding_utf16le, matchFlags, formatTemplate, formatFlags)
        {
        }

        /// <summary>
        /// Creates an enumerator that pins a byte array for the duration of iteration.
        /// </summary>
        public RegExMatchEnumerator(IRegEx regex, byte[] data, RegExEncoding encoding, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            : this(regex, GCHandle.Alloc(data, GCHandleType.Pinned), (IntPtr)0, (IntPtr)data.Length, encoding, matchFlags, formatTemplate, formatFlags)
        {
        }

        /// <summary>
        /// Creates an enumerator over already-stable data.
        /// </summary>
        public RegExMatchEnumerator(IRegEx regex, IntPtr data, IntPtr size, RegExEncoding encoding, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            : this(regex, default, data, size, encoding, matchFlags, formatTemplate, formatFlags)
        {
        }

        /// <summary>
        /// Creates an enumerator over already-stable data.
        /// </summary>
        public unsafe RegExMatchEnumerator(IRegEx regex, void* data, IntPtr size, RegExEncoding encoding, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            : this(regex, default, (IntPtr)data, size, encoding, matchFlags, formatTemplate, formatFlags)
        {
        }

        public RegExMatchEnumerator GetEnumerator() => this;

        public IRegExMatchEnumerator Current => enumerator;

        public bool MoveNext() => enumerator.NextMatch();

        public void Dispose()
        {
            if (enumerator != null)
            {
                Marshal.FinalReleaseComObject(enumerator);
            }

            if (pin.IsAllocated)
            {
                pin.Free();
            }
        }
    }

    internal static class RegEx
    {
        private static Encoding? encodingLatin1; // ISO-8859-1 = GetEncoding(28591)

        public static Encoding EncodingLatin1
        {
            get
            {
                if (encodingLatin1 == null)
                {
                    encodingLatin1 = Encoding.GetEncoding(28591);
                }

                return encodingLatin1;
            }
        }

        public static RegExBytes Bytes(IntPtr data, IntPtr size)
            => new RegExBytes { data = (long)data, size = (long)size };

        public static IRegEx Create(
            string pattern,
            RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.RegExSyntaxFlags_ECMAScript,
            int lcid = 0)
        {
            const int MK_E_SYNTAX = unchecked((int)0x800401E4);

            RegExErrorCode errorCode = RegExErrorCode.RegExErrorCode_unknown;
            try
            {
                return GetLibrary().CreateRegEx(pattern, syntaxFlags, (uint)lcid, out errorCode);
            }
            catch (COMException ex) when (ex.HResult == MK_E_SYNTAX)
            {
                throw new RegExException(pattern, syntaxFlags, errorCode, ex.Message);
            }
        }

        private static IRegExLibrary? s_library;

        private static IRegExLibrary GetLibrary()
        {
            if (s_library == null)
            {
                IRegExLibrary library;
                int hr;
                switch (RuntimeInformation.ProcessArchitecture)
                {
                    case Architecture.X86:
                        hr = NativeMethods.X86.RepStrRegExLibraryCreate(out library);
                        break;
                    case Architecture.X64:
                        hr = NativeMethods.X64.RepStrRegExLibraryCreate(out library);
                        break;
                    case Architecture.Arm64:
                        hr = NativeMethods.Arm64.RepStrRegExLibraryCreate(out library);
                        break;
                    default:
                        throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}");
                }

                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                s_library = library;
            }

            return s_library;
        }

        private static class NativeMethods
        {
            public static class X86
            {
                private const string RepStrRegExLib = "RepStrRegEx_x86.dll";

                [DllImport(RepStrRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int RepStrRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out IRegExLibrary library);
            }

            public static class X64
            {
                private const string RepStrRegExLib = "RepStrRegEx_x64.dll";

                [DllImport(RepStrRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int RepStrRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out IRegExLibrary library);
            }

            public static class Arm64
            {
                private const string RepStrRegExLib = "RepStrRegEx_ARM64.dll";

                [DllImport(RepStrRegExLib, ExactSpelling = true, PreserveSig = true)]
                public static extern int RepStrRegExLibraryCreate(
                    [MarshalAs(UnmanagedType.Interface)] out IRegExLibrary library);
            }
        }
    }
}
