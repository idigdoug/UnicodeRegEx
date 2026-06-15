namespace msandbox
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Describes a block of input bytes for a regex operation, together with the
    /// encoding of those bytes. Construction is cheap and does NOT pin: the
    /// managed source (string / char[] / byte[] / SafeBuffer) is captured by
    /// reference and only pinned for the duration of an operation, via
    /// <see cref="Pin"/>. This keeps the pin lifetime owned entirely by the
    /// operation, so there is no disposable pin for a caller to leak.
    ///
    /// Text sources (<see cref="string"/>, <see cref="char"/>[],
    /// <see cref="ArraySegment{T}"/> of char) convert implicitly because their
    /// bytes are unambiguously UTF-16LE. Sources that need an explicit encoding
    /// (byte[], SafeBuffer, PinnedBytes) or a sub-range of a string use
    /// constructors instead.
    /// </summary>
    internal readonly ref struct RegExInput
    {
        private readonly object? value;     // string | char[] | byte[] | SafeBuffer | null (pre-pinned)
        private readonly nuint data;        // PinnedBytes: base pointer. GC-pinned sources: byte offset into the object. SafeBuffer: byte offset within the buffer.
        private readonly nuint size;        // size in bytes of the input region
        private readonly RegExEncoding encoding;
        private readonly PinMethod pinMethod;

        private enum PinMethod
        {
            PinnedBytes,    // value == null; data is the absolute base pointer.
            GCPinned,       // value is string/char[]/byte[]; data is the byte offset into the pinned object.
            SafeBuffer,     // value is a SafeBuffer; data is the byte offset within the buffer.
        }

        // Canonical constructor: stores the already-resolved internal representation.
        private RegExInput(object? value, nuint data, nuint size, RegExEncoding encoding, PinMethod pinMethod)
        {
            this.value = value;
            this.data = data;
            this.size = size;
            this.encoding = encoding;
            this.pinMethod = pinMethod;
        }

        // ---- Text sources

        /// <summary>
        /// Wraps a UTF-16LE string. A pinned .NET string is byte-for-byte UTF-16LE,
        /// so no transcoding occurs when this input is used with a UTF-16LE regex.
        /// </summary>
        public RegExInput(string value)
            : this(
                value ?? throw new ArgumentNullException(nameof(value)),
                0,
                (nuint)value.Length * sizeof(char),
                RegExEncoding.Utf16LE,
                PinMethod.GCPinned)
        {
        }

        /// <summary>
        /// Wraps a sub-range of a UTF-16LE string. <paramref name="charOffset"/> and
        /// <paramref name="charCount"/> are measured in chars.
        /// </summary>
        public RegExInput(string value, int charOffset, int charCount)
            : this(
                ValidateRange(value, charOffset, charCount),
                (nuint)charOffset * sizeof(char),
                (nuint)charCount * sizeof(char),
                RegExEncoding.Utf16LE,
                PinMethod.GCPinned)
        {
        }

        /// <summary>
        /// Wraps a UTF-16LE char array. A pinned char[] is byte-for-byte UTF-16LE.
        /// </summary>
        public RegExInput(char[] value)
            : this(
                value ?? throw new ArgumentNullException(nameof(value)),
                0,
                (nuint)value.Length * sizeof(char),
                RegExEncoding.Utf16LE,
                PinMethod.GCPinned)
        {
        }

        /// <summary>
        /// Wraps a region of a UTF-16LE char array described by an
        /// <see cref="ArraySegment{T}"/>.
        /// </summary>
        public RegExInput(ArraySegment<char> value)
            : this(
                value.Array ?? throw new ArgumentNullException(nameof(value)),
                (nuint)value.Offset * sizeof(char),
                (nuint)value.Count * sizeof(char),
                RegExEncoding.Utf16LE,
                PinMethod.GCPinned)
        {
        }

        // ---- Byte sources

        /// <summary>Wraps a byte array interpreted with the given encoding.</summary>
        public RegExInput(byte[] value, RegExEncoding encoding)
            : this(
                value ?? throw new ArgumentNullException(nameof(value)),
                0,
                (nuint)value.Length,
                encoding,
                PinMethod.GCPinned)
        {
        }

        /// <summary>
        /// Wraps a region of a byte array (described by an
        /// <see cref="ArraySegment{T}"/>) interpreted with the given encoding.
        /// </summary>
        public RegExInput(ArraySegment<byte> value, RegExEncoding encoding)
            : this(
                value.Array ?? throw new ArgumentNullException(nameof(value)),
                (nuint)value.Offset,
                (nuint)value.Count,
                encoding,
                PinMethod.GCPinned)
        {
        }

        // ---- Unmanaged sources

        /// <summary>
        /// Wraps a region of a <see cref="SafeBuffer"/> (e.g. a memory-mapped view's
        /// <c>SafeMemoryMappedViewHandle</c>) interpreted with the given encoding.
        /// <paramref name="byteOffset"/> and <paramref name="byteCount"/> describe
        /// the region within the buffer; the buffer must remain valid for the
        /// duration of any operation that uses this input.
        /// </summary>
        public RegExInput(SafeBuffer buffer, nuint byteOffset, nuint byteCount, RegExEncoding encoding)
            : this(
                buffer ?? throw new ArgumentNullException(nameof(buffer)),
                byteOffset,
                byteCount,
                encoding,
                PinMethod.SafeBuffer)
        {
        }

        /// <summary>
        /// Wraps an already-pinned block of bytes. The caller is responsible for
        /// keeping the memory pinned for the duration of any operation that uses
        /// this input.
        /// </summary>
        public RegExInput(RegExPinnedBytes bytes, RegExEncoding encoding)
            : this(null, bytes.Data, bytes.Size, encoding, PinMethod.PinnedBytes)
        {
        }

        public static implicit operator RegExInput(string value) => new RegExInput(value);
        public static implicit operator RegExInput(char[] value) => new RegExInput(value);
        public static implicit operator RegExInput(ArraySegment<char> value) => new RegExInput(value);

        /// <summary>The encoding of the input bytes.</summary>
        public RegExEncoding Encoding => encoding;

        /// <summary>The size in bytes of the input region.</summary>
        public nuint Size => size;

        /// <summary>
        /// Pins the input (if necessary) and yields the native byte range plus the
        /// encoding. The returned <see cref="PinScope"/> MUST be disposed (in a
        /// finally block, or via using) to release the pin. Intended to be used
        /// only by the RegEx operation implementations.
        /// </summary>
        public PinScope Pin(out RegExPinnedBytes bytes)
        {
            switch (pinMethod)
            {
                case PinMethod.PinnedBytes:
                    // data is the absolute base pointer; nothing to pin or release.
                    bytes = new RegExPinnedBytes(data, size);
                    return default;

                case PinMethod.GCPinned:
                {
                    var handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                    // data is the byte offset into the pinned object (0 for whole object).
                    var ptr = (nuint)(nint)handle.AddrOfPinnedObject() + data;
                    bytes = new RegExPinnedBytes(ptr, size);
                    return new PinScope(handle);
                }

                case PinMethod.SafeBuffer:
                {
                    var buffer = (SafeBuffer)value!;
                    unsafe
                    {
                        byte* basePtr = null;
                        buffer.AcquirePointer(ref basePtr);
                        // data holds the byte offset within the buffer.
                        var ptr = (nuint)basePtr + data;
                        bytes = new RegExPinnedBytes(ptr, size);
                        return new PinScope(buffer);
                    }
                }

                default:
                    throw new InvalidOperationException("Unknown input source pinMethod.");
            }
        }

        private static string ValidateRange(string value, int charOffset, int charCount)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var offset = (uint)charOffset;
            var count = (uint)charCount;
            var valueLength = (uint)value.Length;
            if (offset > valueLength || count > valueLength - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(charCount), "The specified range is outside the bounds of the string.");
            }

            return value;
        }

        /// <summary>
        /// Releases the pin established by <see cref="RegExInput.Pin"/>. A
        /// default-constructed PinScope (for already-pinned input) releases
        /// nothing. Dispose is idempotent.
        /// </summary>
        internal ref struct PinScope
        {
            private GCHandle handle;
            private SafeBuffer? safeBuffer;

            internal PinScope(GCHandle handle)
            {
                this.handle = handle;
                this.safeBuffer = null;
            }

            internal PinScope(SafeBuffer safeBuffer)
            {
                this.handle = default;
                this.safeBuffer = safeBuffer;
            }

            public void Dispose()
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                    handle = default;
                }

                if (safeBuffer != null)
                {
                    safeBuffer.ReleasePointer();
                    safeBuffer = null;
                }
            }
        }
    }
}
