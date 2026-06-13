namespace msandbox
{
    using RepStrRegEx;
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;

    internal sealed class BytesPin : IDisposable
    {
        nuint size;
        GCHandle handle;

        ~BytesPin()
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        public BytesPin(string value)
        {
            this.size = (uint)value.Length * sizeof(char);
            this.handle = GCHandle.Alloc(value, GCHandleType.Pinned);
        }

        public BytesPin(byte[] value)
        {
            this.size = (uint)value.Length;
            this.handle = GCHandle.Alloc(value, GCHandleType.Pinned);
        }

        public static implicit operator PinnedBytes(BytesPin self)
            => new PinnedBytes((nuint)(nint)self.handle.AddrOfPinnedObject(), self.size);

        public void Dispose()
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            GC.SuppressFinalize(this);
        }
    }

    internal ref struct PinnedBytes
    {
        private static Encoding? encodingLatin1; // ISO-8859-1 = GetEncoding(28591)
        private nuint data;
        private nuint size;
        private static Encoding EncodingLatin1
        {
            get
            {
                var enc = encodingLatin1;
                if (enc == null)
                {
                    enc = Encoding.GetEncoding(28591);
                    encodingLatin1 = enc;
                }

                return enc;
            }
        }

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

        public PinnedBytes(nint data, int size)
            : this(unchecked((nuint)(nint)data), checked((nuint)size)) { }

        public unsafe PinnedBytes(void* data, long size)
            : this(unchecked((nuint)data), checked((nuint)size)) { }

        public unsafe PinnedBytes(void* data, int size)
            : this(unchecked((nuint)data), checked((nuint)size)) { }

        public static implicit operator RegExBytes(PinnedBytes pinned) =>
            new RegExBytes { data = unchecked((long)pinned.data), size = unchecked((long)pinned.size) };

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
                        return EncodingLatin1.GetString(this.DataPtr, this.SizeInt);
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

    internal struct RegEx
    {
        private static IRegExLibrary? library;
        private IRegEx inner;

        private static IRegExLibrary Library
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
            RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags.RegExSyntaxFlags_ECMAScript,
            int lcid = 0)
        {
            const int MK_E_SYNTAX = unchecked((int)0x800401E4);

            RegExErrorCode errorCode = RegExErrorCode.RegExErrorCode_unknown;
            try
            {
                return new RegEx(Library.CreateRegEx(pattern, syntaxFlags, (uint)lcid, out errorCode));
            }
            catch (COMException ex) when (ex.HResult == MK_E_SYNTAX)
            {
                throw new RegExException(pattern, syntaxFlags, errorCode, ex.Message);
            }
        }

        private RegEx(IRegEx inner)
        {
            this.inner = inner;
        }

        public string Pattern => inner.Pattern;

        public RegExSyntaxFlags Flags => inner.Flags;

        public uint Lcid => inner.Lcid;

        public RegExMatch Match(
            PinnedBytes input,
            RegExEncoding inputEncoding,
            nuint startByteOffset = 0,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default)
        {
            return new RegExMatch(inner.Match(input, inputEncoding, (long)startByteOffset, matchFlags));
        }

        public RegExMatch Search(
            PinnedBytes input,
            RegExEncoding inputEncoding,
            nuint startByteOffset = 0,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default)
        {
            return new RegExMatch(inner.Search(input, inputEncoding, (long)startByteOffset, matchFlags));
        }

        public RegExMatchEnumerator EnumerateMatches(
            PinnedBytes input,
            RegExEncoding inputEncoding,
            nuint startByteOffset = 0,
            RegExMatchFlags matchFlags = RegExMatchFlags.RegExMatchFlag_default,
            string? formatTemplate = null,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
        {
            var enumerator = inner.EnumerateMatches(input, inputEncoding, (long)startByteOffset, matchFlags);
            if (formatTemplate != null)
            {
                enumerator.SetFormatTemplate(formatTemplate, formatFlags);
            }

            return new RegExMatchEnumerator(enumerator);
        }

        public string Replace(
            PinnedBytes input,
            RegExEncoding inputEncoding,
            nuint startByteOffset,
            RegExMatchFlags matchFlags,
            string formatTemplate,
            RegExFormatFlags formatFlags = RegExFormatFlags.RegExFormatFlag_perl)
        {
            return inner.Replace(input, inputEncoding, (long)startByteOffset, matchFlags, formatTemplate, formatFlags);
        }

        public void ReplaceTo(
            PinnedBytes input,
            RegExEncoding inputEncoding,
            nuint startByteOffset,
            RegExMatchFlags matchFlags,
            string formatTemplate,
            RegExFormatFlags formatFlags,
            ISequentialStream outputStream,
            RegExEncoding outputEncoding)
        {
            inner.ReplaceTo(input, inputEncoding, (long)startByteOffset, matchFlags, formatTemplate, formatFlags, outputStream, outputEncoding);
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

    /// <summary>
    /// Non-owning view over a match result. Obtained from a RegExMatch (via
    /// .Results) or from RegExMatchEnumerator.Current. Does not release the
    /// underlying COM object; the owner (RegExMatch or RegExMatchEnumerator)
    /// does. Only valid while that owner is alive and the input stays pinned.
    /// </summary>
    internal readonly ref struct RegExMatchResults
    {
        private readonly IRegExMatchResults inner;

        public RegExMatchResults(IRegExMatchResults inner)
        {
            this.inner = inner;
        }

        public PinnedBytes Input
        {
            get
            {
                var input = inner.Input;
                return new PinnedBytes(input.data, input.size);
            }
        }

        public RegExEncoding InputEncoding => inner.InputEncoding;

        public int SubMatchCount => (int)inner.SubMatchCount;

        public RegExSubMatch GetSubMatch(int subMatchIndex) => inner.GetSubMatch((uint)subMatchIndex);

        public void SetFormatTemplate(string formatTemplate, RegExFormatFlags formatFlags)
        {
            inner.SetFormatTemplate(formatTemplate, formatFlags);
        }

        public string Format()
        {
            return inner.Format();
        }

        public void FormatTo(ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            inner.FormatTo(outputStream, outputEncoding);
        }

        public string CopyInput(nuint inputOffset, int size)
        {
            return inner.CopyInput((long)inputOffset, checked((uint)size));
        }

        public void CopyInputTo(nuint inputOffset, nuint size, ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            inner.CopyInputTo((long)inputOffset, (long)size, outputStream, outputEncoding);
        }
    }

    /// <summary>
    /// Owns the COM object produced by RegEx.Match / RegEx.Search and releases
    /// it on Dispose. Hand the non-owning RegExMatchResults view (via .Results
    /// or the implicit conversion) to code that just inspects a match, so the
    /// same helper can accept a result whether it came from Match/Search or
    /// from a RegExMatchEnumerator.Current.
    /// </summary>
    internal ref struct RegExMatch
    {
        private readonly IRegExMatchResults inner;

        public RegExMatch(IRegExMatchResults inner)
        {
            this.inner = inner;
        }

        /// <summary>True if Match/Search found a match (non-null result).</summary>
        public bool Success => inner != null;

        /// <summary>
        /// The non-owning view over this match. Only valid while this owner is
        /// alive (not yet Disposed) and while the input remains pinned.
        /// </summary>
        public RegExMatchResults Results => new RegExMatchResults(inner);

        public static implicit operator RegExMatchResults(RegExMatch self) => self.Results;

        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }
    }

    internal ref struct RegExMatchEnumerator
    {
        private readonly IRegExMatchEnumerator inner;

        public RegExMatchEnumerator(IRegExMatchEnumerator inner)
        {
            this.inner = inner;
        }

        public RegExMatchEnumerator GetEnumerator() => this;

        public RegExMatchResults Current => new RegExMatchResults(inner);

        public bool MoveNext() => inner.NextMatch();

        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }
    }

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
}
