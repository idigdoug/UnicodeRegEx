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

        public string Pattern => pattern;
        public RegExSyntaxFlags SyntaxFlags => syntaxFlags;
        public RegExErrorCode ErrorCode => errorCode;
        public string NativeMessage => nativeMessage;

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
            nuint data,
            nuint size,
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
            nuint size,
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
            nuint data,
            nuint size,
            RegExEncoding encoding,
            RegExMatchFlags matchFlags,
            string? formatTemplate,
            RegExFormatFlags formatFlags)
        {
            this.pin = pin;
            var inputString = new PinnedBytes(
                pin.IsAllocated ? (nuint)(nint)pin.AddrOfPinnedObject() : data,
                size);
            this.enumerator = regex.EnumerateMatches(inputString, encoding, 0, matchFlags);
            if (formatTemplate != null)
            {
                enumerator.SetFormatTemplate(formatTemplate, formatFlags);
            }
        }

        /// <summary>
        /// Creates an enumerator that pins a string for the duration of iteration.
        /// </summary>
        public RegExMatchEnumerator(IRegEx regex, string data, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            : this(regex, GCHandle.Alloc(data, GCHandleType.Pinned), 0, (uint)(data.Length * sizeof(char)), RegExEncoding.RegExEncoding_utf16le, matchFlags, formatTemplate, formatFlags)
        {
        }

        /// <summary>
        /// Creates an enumerator that pins a byte array for the duration of iteration.
        /// </summary>
        public RegExMatchEnumerator(IRegEx regex, byte[] data, RegExEncoding encoding, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            : this(regex, GCHandle.Alloc(data, GCHandleType.Pinned), 0, (uint)data.Length, encoding, matchFlags, formatTemplate, formatFlags)
        {
        }

        /// <summary>
        /// Creates an enumerator over already-stable data.
        /// </summary>
        public RegExMatchEnumerator(IRegEx regex, nuint data, nuint size, RegExEncoding encoding, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            : this(regex, default, data, size, encoding, matchFlags, formatTemplate, formatFlags)
        {
        }

        /// <summary>
        /// Creates an enumerator over already-stable data.
        /// </summary>
        public unsafe RegExMatchEnumerator(IRegEx regex, void* data, nuint size, RegExEncoding encoding, RegExMatchFlags matchFlags, string? formatTemplate = null, RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
            : this(regex, default, (nuint)data, size, encoding, matchFlags, formatTemplate, formatFlags)
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

    internal ref struct PinnedBytes
    {
        private nuint data;
        private nuint size;

        public PinnedBytes(nuint data, nuint size)
        {
            if (unchecked(data + size) < data)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Size overflows address space.");
            }

            this.data = data;
            this.size = size;
        }

        public PinnedBytes(long data, long size)
            : this(unchecked((nuint)data), checked((nuint)size)) { }

        public PinnedBytes(IntPtr data, int size)
            : this(unchecked((nuint)(nint)data), checked((nuint)size)) { }

        public unsafe PinnedBytes(void* data, long size)
            : this(unchecked((nuint)data), checked((nuint)size)) { }

        public unsafe PinnedBytes(void* data, int size)
            : this(unchecked((nuint)data), checked((nuint)size)) { }

        public static implicit operator RegExBytes(PinnedBytes pinned) =>
            new RegExBytes { data = unchecked((long)pinned.data), size = unchecked((long)pinned.size) };

        public static implicit operator PinnedBytes(RegExBytes regex) =>
            new PinnedBytes(unchecked((nuint)regex.data), checked((nuint)regex.size));

        public nuint Data => data;
        public unsafe byte* DataPtr => (byte*)data;
        public nuint Size => size;
        public int SizeInt => checked((int)size);

        public byte this[nuint index]
        {
            get
            {
                if (index >= size)
                {
                    throw new IndexOutOfRangeException();
                }

                unsafe
                {
                    return *(byte*)(data + index);
                }
            }
        }

        public PinnedBytes this[nuint begin, nuint end]
        {
            get
            {
                if (begin > end || end > size)
                {
                    throw new ArgumentOutOfRangeException(nameof(end), "Invalid range.");
                }

                return new PinnedBytes(unchecked(data + begin), end - begin);
            }
        }

        public void CopyTo(byte[] dest)
        {
            if (dest == null)
            {
                throw new ArgumentNullException(nameof(dest));
            }

            if ((ulong)dest.LongLength < size)
            {
                throw new ArgumentException("Destination array is too small.", nameof(dest));
            }

            unsafe
            {
                fixed (byte* pDest = dest)
                {
                    Buffer.MemoryCopy((void*)data, pDest, dest.Length, (long)size);
                }
            }
        }

        public PinnedBytes Slice(nuint begin, nuint length)
        {
            if (begin > size || length > size - begin)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new PinnedBytes(unchecked(data + begin), length);
        }

        public PinnedBytes First(nuint length)
        {
            if (length > size)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new PinnedBytes(unchecked(data), length);
        }

        public PinnedBytes Last(nuint length)
        {
            if (length > size)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new PinnedBytes(unchecked(data + (size - length)), length);
        }

        public byte[] ToArray()
        {
            byte[] dest = new byte[checked((int)size)];

            unsafe
            {
                fixed (byte* pDest = dest)
                {
                    Buffer.MemoryCopy((void*)data, pDest, dest.Length, (long)size);
                }
            }

            return dest;
        }

        public string ToString(RegExEncoding encoding)
        {
            unsafe
            {
                switch (encoding)
                {
                    case RegExEncoding.RegExEncoding_latin1:
                        return RegEx.EncodingLatin1.GetString(this.DataPtr, this.SizeInt);
                    case RegExEncoding.RegExEncoding_utf8:
                        return Encoding.UTF8.GetString(this.DataPtr, this.SizeInt);
                    case RegExEncoding.RegExEncoding_utf16le:
                        return Marshal.PtrToStringUni((nint)this.data, (int)(this.size / sizeof(char)));
                    case RegExEncoding.RegExEncoding_utf16be:
                        return Encoding.BigEndianUnicode.GetString(this.DataPtr, this.SizeInt);
                    case RegExEncoding.RegExEncoding_none:
                    default:
                        throw new NotSupportedException($"Unsupported regex encoding: {encoding}");
                }
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
