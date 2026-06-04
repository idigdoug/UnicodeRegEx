namespace msandbox
{
    using RepStrRegExLib;
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
        private static Encoding? encodingLatin1; // ISO-8859-1 = GetEncoding(28591)

        /// <summary>
        /// Returns the string content of the specified sub-match, or null if the sub-match did not
        /// participate in the match. Use subMatchIndex = 0 to get the entire match.
        /// </summary>
        public static string? SubMatchString(
            this IRegExMatchEnumerator enumerator,
            int subMatchIndex)
        {
            var subMatchString = enumerator.GetSubMatchString((uint)subMatchIndex, RegExEncoding.RegExEncoding_utf16le);
            switch (subMatchString.encoding)
            {
                case RegExEncoding.RegExEncoding_none:
                    return null;
                case RegExEncoding.RegExEncoding_utf16le:
                    return Marshal.PtrToStringUni((IntPtr)subMatchString.data_ptr, (int)(subMatchString.size / sizeof(char)));
                default:
                    throw new NotSupportedException($"Unsupported regex encoding: {subMatchString.encoding}");
            }
        }

        /// <summary>
        /// Formats this match based on the formatTemplate specified in the call to Matches() or in the most recent
        /// call to SetFormatTemplate. If the template has not been set, this method returns an empty string.
        /// </summary>
        public static string FormatString(this IRegExMatchEnumerator enumerator)
        {
            var output = enumerator.Format(RegExEncoding.RegExEncoding_utf16le);
            return Marshal.PtrToStringUni((IntPtr)output.data_ptr, (int)(output.size / sizeof(char)));
        }

        /// <summary>
        /// Returns a string for the bytes contained in the RegExString buffer.
        /// The buffer is valid until the next call into the match enumerator.
        /// </summary>
        public static string GetString(this RegExString regexString)
        {
            switch (regexString.encoding)
            {
                case RegExEncoding.RegExEncoding_latin1:
                    unsafe
                    {
                        if (encodingLatin1 == null)
                        {
                            encodingLatin1 = Encoding.GetEncoding(28591);
                        }
                        return encodingLatin1.GetString((byte*)(IntPtr)regexString.data_ptr, (int)regexString.size);
                    }
                case RegExEncoding.RegExEncoding_utf8:
                    unsafe
                    {
                        return Encoding.UTF8.GetString((byte*)(IntPtr)regexString.data_ptr, (int)regexString.size);
                    }
                case RegExEncoding.RegExEncoding_utf16le:
                    return Marshal.PtrToStringUni((IntPtr)regexString.data_ptr, (int)(regexString.size / sizeof(char)));
                case RegExEncoding.RegExEncoding_utf16be:
                    unsafe
                    {
                        return Encoding.BigEndianUnicode.GetString((byte*)(IntPtr)regexString.data_ptr, (int)regexString.size);
                    }
                case RegExEncoding.RegExEncoding_none:
                default:
                    throw new NotSupportedException($"Unsupported regex encoding: {regexString.encoding}");
            }
        }

        /// <summary>
        /// Iterate matches over a string (will be pinned for the duration of iteration).
        /// </summary>
        public static RegExMatchEnumerator Matches(
            this IRegEx regex,
            string data,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default,
            string? formatTemplate = null,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_default)
            => new RegExMatchEnumerator(regex, data, matchFlags, formatTemplate, formatFlags);

        /// <summary>
        /// Iterate matches over a byte array + encoding (will be pinned for the duration of iteration).
        /// </summary>
        public static RegExMatchEnumerator Matches(
            this IRegEx regex,
            byte[] data,
            RegExEncoding encoding,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default,
            string? formatTemplate = null,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_default)
            => new RegExMatchEnumerator(regex, data, encoding, matchFlags, formatTemplate, formatFlags);

        /// <summary>
        /// Iterate matches over pinned bytes + encoding.
        /// The caller must ensure the data remains valid for the lifetime of enumeration.
        /// </summary>
        public static unsafe RegExMatchEnumerator Matches(
            this IRegEx regex,
            void* data,
            IntPtr size,
            RegExEncoding encoding,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default,
            string? formatTemplate = null,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_default)
            => new RegExMatchEnumerator(regex, data, size, encoding, matchFlags, formatTemplate, formatFlags);
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
            var inputString = new RegExString
            {
                data_ptr = (long)(pin.IsAllocated ? pin.AddrOfPinnedObject() : data),
                size = (long)size,
                encoding = encoding
            };

            this.enumerator = regex.EnumerateMatches(ref inputString, 0, matchFlags);
            if (formatTemplate != null)
            {
                enumerator.SetFormatTemplate(formatTemplate, formatFlags);
            }
        }

        /// <summary>
        /// Creates an enumerator that pins a string for the duration of iteration.
        /// </summary>
        public RegExMatchEnumerator(IRegEx regex, string data, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_default)
            : this(regex, GCHandle.Alloc(data, GCHandleType.Pinned), (IntPtr)0, (IntPtr)(data.Length * sizeof(char)), RegExEncoding.RegExEncoding_utf16le, matchFlags, formatTemplate, formatFlags)
        {
        }

        /// <summary>
        /// Creates an enumerator that pins a byte array for the duration of iteration.
        /// </summary>
        public RegExMatchEnumerator(IRegEx regex, byte[] data, RegExEncoding encoding, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_default)
            : this(regex, GCHandle.Alloc(data, GCHandleType.Pinned), (IntPtr)0, (IntPtr)data.Length, encoding, matchFlags, formatTemplate, formatFlags)
        {
        }

        /// <summary>
        /// Creates an enumerator over already-stable data.
        /// </summary>
        public unsafe RegExMatchEnumerator(IRegEx regex, void* data, IntPtr size, RegExEncoding encoding, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_default)
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
