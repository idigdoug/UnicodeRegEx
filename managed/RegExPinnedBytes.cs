namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;

#pragma warning disable CS0660 // Has operator== but not Equals(object). It's a ref struct so this is normal.
    /// <summary>
    /// A pointer/size pair describing a pinned (or otherwise fixed) block of bytes. The caller is
    /// responsible for keeping the underlying memory valid for the lifetime of this value.
    /// </summary>
    public readonly ref struct RegExPinnedBytes
#pragma warning restore CS0660
    {
        private static Encoding? encodingLatin1; // ISO-8859-1 = GetEncoding(28591)
        private readonly nuint data;
        private readonly nuint size;

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

        /// <summary>Creates a descriptor for <paramref name="size"/> bytes starting at <paramref name="data"/>.</summary>
        public RegExPinnedBytes(nuint data, nuint size)
        {
            if (data + size < data)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Size overflows address space.");
            }

            this.data = data;
            this.size = size;
        }

        /// <summary>Creates a descriptor for <paramref name="size"/> bytes starting at <paramref name="data"/>.</summary>
        public unsafe RegExPinnedBytes(void* data, nuint size)
            : this((nuint)data, size) { }

        /// <summary>
        /// Returns true if this.pointer == other.pointer and this.size == other.size.
        /// NOT BASED ON DATA CONTENT.
        /// </summary>
        public static bool operator ==(RegExPinnedBytes left, RegExPinnedBytes right)
            => left.data == right.data && left.size == right.size;

        /// <summary>
        /// Returns true if this.pointer != other.pointer or this.size != other.size.
        /// NOT BASED ON DATA CONTENT.
        /// </summary>
        public static bool operator !=(RegExPinnedBytes left, RegExPinnedBytes right)
            => !(left == right);

        /// <summary>The base address of the bytes.</summary>
        public nuint Data => data;

        /// <summary>The base address of the bytes as a pointer.</summary>
        public unsafe byte* DataPtr => (byte*)data;

        /// <summary>The size of the block, in bytes.</summary>
        public nuint Size => size;

        /// <summary>The size of the block, in bytes, as an <see cref="int"/>. Throws if the size exceeds <see cref="int.MaxValue"/>.</summary>
        public int SizeInt => checked((int)size);

        /// <summary>Gets the byte at <paramref name="index"/>. Throws if out of range.</summary>
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

        /// <summary>Returns the sub-range <c>[begin, end)</c>. Throws if the range is invalid.</summary>
        public RegExPinnedBytes this[nuint begin, nuint end]
        {
            get
            {
                if (begin > end || end > size)
                {
                    throw new ArgumentOutOfRangeException(nameof(end), "Invalid range.");
                }

                return new RegExPinnedBytes(data + begin, end - begin);
            }
        }

        /// <summary>Copies these bytes into <paramref name="dest"/>.</summary>
        public void CopyTo(byte[] dest)
        {
            unsafe
            {
                fixed (byte* pDest = dest)
                {
                    Buffer.MemoryCopy((void*)data, pDest, dest.LongLength, (long)size);
                }
            }
        }

        /// <summary>Returns <paramref name="length"/> bytes starting at <paramref name="begin"/>. Throws if out of range.</summary>
        public RegExPinnedBytes Slice(nuint begin, nuint length)
        {
            if (begin > size || length > size - begin)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new RegExPinnedBytes(data + begin, length);
        }

        /// <summary>Returns the first <paramref name="length"/> bytes. Throws if out of range.</summary>
        public RegExPinnedBytes First(nuint length)
        {
            if (length > size)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new RegExPinnedBytes(data, length);
        }

        /// <summary>Returns the last <paramref name="length"/> bytes. Throws if out of range.</summary>
        public RegExPinnedBytes Last(nuint length)
        {
            if (length > size)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new RegExPinnedBytes(data + (size - length), length);
        }

        /// <summary>Copies these bytes into a new array.</summary>
        public byte[] ToArray()
        {
            byte[] dest = new byte[size];

            unsafe
            {
                fixed (byte* pDest = dest)
                {
                    Buffer.MemoryCopy((void*)data, pDest, dest.Length, (long)size);
                }
            }

            return dest;
        }

        /// <summary>
        /// Returns true if this.pointer == other.pointer and this.size == other.size.
        /// NOT BASED ON DATA CONTENT.
        /// </summary>
        public bool Equals(RegExPinnedBytes other) => this == other;

        /// <summary>
        /// Returns a hash code based on this.pointer and this.size.
        /// NOT BASED ON DATA CONTENT.
        /// </summary>
        public override int GetHashCode()
        {
            const int Offset = unchecked((int)0x9E3779B9);
            var v1 = data.GetHashCode();
            var v2 = size.GetHashCode();
            return v1 ^ (v2 + Offset + (v1 << 6) + (v1 >> 2));
        }

        /// <summary>
        /// Returns a string of the form "Data=0x12345678, Size=0x42". NOT BASED ON DATA CONTENT.
        /// </summary>
        public override string ToString()
        {
            return $"Data: 0x{data:X}, Size: 0x{size:X}";
        }

        /// <summary>Decodes these bytes to a string using the given <see cref="Encoding"/>.</summary>
        public string ToString(Encoding encoding)
        {
            unsafe
            {
                return encoding.GetString((byte*)data, checked((int)size));
            }
        }

        /// <summary>Decodes these bytes to a string using the given code page.</summary>
        public string ToString(int codepage)
        {
            Encoding encoding;
            switch (codepage)
            {
                case 1200:
                    return Marshal.PtrToStringUni((nint)this.data, checked((int)(this.size / sizeof(char))));
                case 1201:
                    encoding = Encoding.BigEndianUnicode;
                    break;
                case 28591:
                    encoding = EncodingLatin1;
                    break;
                case 65001:
                    encoding = Encoding.UTF8;
                    break;
                default:
                    encoding = Encoding.GetEncoding(codepage);
                    break;
            }

            return ToString(encoding);
        }
    }
}
