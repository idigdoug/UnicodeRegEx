namespace UnicodeRegEx.Tests
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    /// <summary>
    /// Shared helpers for encoding test inputs, decoding outputs, reading memory
    /// streams, and extracting matched text from the (ref struct) result types.
    /// </summary>
    internal static class TestHelpers
    {
        /// <summary>
        /// Asserts that <paramref name="action"/> throws an exception of type
        /// <typeparamref name="T"/>. Used instead of Assert.ThrowsException so that
        /// the action body may construct ref struct types.
        /// </summary>
        public static void AssertThrows<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }

            Assert.Fail($"Expected {typeof(T).Name} to be thrown.");
        }

        /// <summary>Returns the <see cref="Encoding"/> for a <see cref="RegExEncoding"/> code page.</summary>
        public static Encoding GetEncoding(RegExEncoding encoding)
        {
            switch (encoding)
            {
                case RegExEncoding.Utf16LE: return Encoding.Unicode;
                case RegExEncoding.Utf16BE: return Encoding.BigEndianUnicode;
                case RegExEncoding.Latin1: return Encoding.GetEncoding(28591);
                case RegExEncoding.Utf8: return Encoding.UTF8;
                default: return Encoding.GetEncoding((int)encoding);
            }
        }

        /// <summary>Encodes <paramref name="text"/> into bytes using the given encoding.</summary>
        public static byte[] Encode(string text, RegExEncoding encoding)
            => GetEncoding(encoding).GetBytes(text);

        /// <summary>Decodes <paramref name="bytes"/> using the given encoding.</summary>
        public static string Decode(byte[] bytes, RegExEncoding encoding)
            => GetEncoding(encoding).GetString(bytes);

        /// <summary>Snapshots the bytes currently buffered in a memory stream.</summary>
        public static byte[] ReadAllBytes(Interop.IRegExMemoryStream stream)
        {
            var buffer = stream.Buffer;
            int size = checked((int)buffer.size);
            var result = new byte[size];
            if (size > 0)
            {
                Marshal.Copy((nint)buffer.data, result, 0, size);
            }

            return result;
        }

        /// <summary>Reads and decodes all buffered bytes from a memory stream.</summary>
        public static string ReadAllText(Interop.IRegExMemoryStream stream, RegExEncoding encoding)
            => Decode(ReadAllBytes(stream), encoding);

        /// <summary>Returns the bytes of a segment as text, decoded with the input encoding.</summary>
        public static string SegmentText(RegExSegment segment)
            => segment.Bytes.ToString((int)segment.InputEncoding);
    }
}
