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

        /// <summary>Returns the <see cref="Encoding"/> for a <see cref="RegExCodePage"/> code page.</summary>
        public static Encoding GetEncoding(int codePage)
        {
            switch (codePage)
            {
                case RegExCodePage.Utf16LE: return Encoding.Unicode;
                case RegExCodePage.Utf16BE: return Encoding.BigEndianUnicode;
                case RegExCodePage.Latin1: return Encoding.GetEncoding(28591);
                case RegExCodePage.Utf8: return Encoding.UTF8;
                default: return Encoding.GetEncoding((int)codePage);
            }
        }

        /// <summary>Encodes <paramref name="text"/> into bytes using the given codePage.</summary>
        public static byte[] Encode(string text, int codePage)
            => GetEncoding(codePage).GetBytes(text);

        /// <summary>Decodes <paramref name="bytes"/> using the given codePage.</summary>
        public static string Decode(byte[] bytes, int codePage)
            => GetEncoding(codePage).GetString(bytes);

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
        public static string ReadAllText(Interop.IRegExMemoryStream stream, int codePage)
            => Decode(ReadAllBytes(stream), codePage);

        /// <summary>Returns the bytes of a segment as text, decoded with the input code page.</summary>
        public static string SegmentText(RegExSegment segment)
            => segment.Bytes.ToString((int)segment.InputCodePage);
    }
}
