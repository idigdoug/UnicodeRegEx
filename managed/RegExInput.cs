namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Describes a block of input bytes for a regex operation, together with the
    /// encoding of those bytes.
    ///
    /// Text sources (<see cref="string"/>, <see cref="char"/>[],
    /// <see cref="ArraySegment{T}"/> of char) convert implicitly.
    /// </summary>
    public readonly ref struct RegExInput
    {
        private readonly object? value; // string | char[] | byte[] | SafeBuffer | null (pre-pinned)
        private readonly nuint data;    // PinnedBytes: base pointer. GC-pinned sources: byte offset into the object. SafeBuffer: byte offset within the buffer.
        private readonly nuint size;    // size in bytes of the input region
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

        /// <summary>Wraps an entire UTF-16 string as input.</summary>
        public RegExInput(string value)
            : this(
                value ?? throw new ArgumentNullException(nameof(value)),
                0,
                (nuint)value.Length * sizeof(char),
                RegExEncoding.Utf16LE,
                PinMethod.GCPinned)
        {
        }

        /// <summary>Wraps a <paramref name="charCount"/>-char range of a UTF-16 string starting at <paramref name="charOffset"/>.</summary>
        public RegExInput(string value, int charOffset, int charCount)
            : this(
                value ?? throw new ArgumentNullException(nameof(value)),
                (nuint)charOffset * sizeof(char),
                (nuint)charCount * sizeof(char),
                RegExEncoding.Utf16LE,
                PinMethod.GCPinned)
        {
            var valueLength = (uint)value.Length;
            var offset = (uint)charOffset;
            var count = (uint)charCount;

            if (valueLength < offset)
            {
                throw new ArgumentOutOfRangeException(nameof(charOffset));
            }

            if (valueLength - offset < count)
            {
                throw new ArgumentOutOfRangeException(nameof(charCount));
            }
        }

        /// <summary>Wraps an entire UTF-16 character array as input.</summary>
        public RegExInput(char[] value)
            : this(
                value ?? throw new ArgumentNullException(nameof(value)),
                0,
                (nuint)value.Length * sizeof(char),
                RegExEncoding.Utf16LE,
                PinMethod.GCPinned)
        {
        }

        /// <summary>Wraps a segment of a UTF-16 character array as input.</summary>
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

        /// <summary>Wraps an entire byte array, interpreted with the given encoding, as input.</summary>
        public RegExInput(byte[] value, RegExEncoding encoding)
            : this(
                value ?? throw new ArgumentNullException(nameof(value)),
                0,
                (nuint)value.Length,
                encoding,
                PinMethod.GCPinned)
        {
        }

        /// <summary>Wraps a segment of a byte array, interpreted with the given encoding, as input.</summary>
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
        /// </summary>
        public RegExInput(SafeBuffer buffer, nuint byteOffset, nuint byteCount, RegExEncoding encoding)
            : this(
                buffer ?? throw new ArgumentNullException(nameof(buffer)),
                byteOffset,
                byteCount,
                encoding,
                PinMethod.SafeBuffer)
        {
            var size = buffer.ByteLength;

            if (size < byteOffset)
            {
                throw new ArgumentOutOfRangeException(nameof(byteOffset));
            }

            if (size - byteOffset < byteCount)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }
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

        /// <summary>Wraps a string as input.</summary>
        public static implicit operator RegExInput(string value) => new RegExInput(value);

        /// <summary>Wraps a character array as input.</summary>
        public static implicit operator RegExInput(char[] value) => new RegExInput(value);

        /// <summary>Wraps a character array segment as input.</summary>
        public static implicit operator RegExInput(ArraySegment<char> value) => new RegExInput(value);

        /// <summary>The encoding of the input bytes.</summary>
        public RegExEncoding Encoding => encoding;

        /// <summary>The size (in bytes) of the input region.</summary>
        public nuint Size => size;

        /// <summary>
        /// Pins the input (if necessary) and yields the native byte range plus the
        /// encoding. The returned <see cref="PinScope"/> MUST be disposed (in a
        /// finally block, or via using) to release the pin.
        /// </summary>
        internal RegExPinnedBytes Pin(ref PinScope pinScope)
        {
            switch (pinMethod)
            {
                case PinMethod.PinnedBytes:
                    // data is the absolute base pointer; nothing to pin or release.
                    pinScope = default;
                    return new RegExPinnedBytes(data, size);

                case PinMethod.GCPinned:
                {
                    GCHandle handle = default;
                    try
                    {
                        handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                    }
                    finally
                    {
                        if (handle.IsAllocated)
                        {
                            pinScope = new PinScope(handle);
                        }
                    }

                    var ptr = (nuint)(nint)handle.AddrOfPinnedObject() + data;
                    return new RegExPinnedBytes(ptr, size);
                }

                case PinMethod.SafeBuffer:
                {
                    var buffer = (SafeBuffer)value!;
                    unsafe
                    {
                        byte* basePtr = null;
                        try
                        {
                            buffer.AcquirePointer(ref basePtr);
                        }
                        finally
                        {
                            if (basePtr != null)
                            {
                                pinScope = new PinScope(buffer);
                            }
                        }

                        var ptr = (nuint)basePtr + data;
                        return new RegExPinnedBytes(ptr, size);
                    }
                }

                default:
                    throw new InvalidOperationException("Unknown input source pinMethod.");
            }
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
