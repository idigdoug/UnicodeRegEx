namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;

#pragma warning disable CS0660 // Has operator== but not Equals(object). It's a ref struct so this is normal.
    public ref struct RegExPinnedBytes
#pragma warning restore CS0660
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

        public RegExPinnedBytes(nuint data, nuint size)
        {
            if (data + size < data)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Size overflows address space.");
            }

            this.data = data;
            this.size = size;
        }

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

        public RegExPinnedBytes Slice(nuint begin, nuint length)
        {
            if (begin > size || length > size - begin)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new RegExPinnedBytes(data + begin, length);
        }

        public RegExPinnedBytes First(nuint length)
        {
            if (length > size)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new RegExPinnedBytes(data, length);
        }

        public RegExPinnedBytes Last(nuint length)
        {
            if (length > size)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds bounds of data.");
            }

            return new RegExPinnedBytes(data + (size - length), length);
        }

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

        public override string ToString()
        {
            return $"Data: 0x{data:X}, Size: 0x{size:X}";
        }

        public string ToString(Encoding encoding)
        {
            unsafe
            {
                return encoding.GetString((byte*)data, checked((int)size));
            }
        }

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
