namespace UnicodeRegEx.Tests
{
    using System;
    using System.IO.MemoryMappedFiles;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    [TestClass]
    public class InputAndPinnedBytesTests
    {
        // ---- RegExInput validation

        [TestMethod]
        public void RegExInput_StringRange_InvalidOffset_Throws()
        {
            TestHelpers.AssertThrows<ArgumentOutOfRangeException>(
                () => new RegExInput("abc", 4, 0));
        }

        [TestMethod]
        public void RegExInput_StringRange_InvalidCount_Throws()
        {
            TestHelpers.AssertThrows<ArgumentOutOfRangeException>(
                () => new RegExInput("abc", 1, 3));
        }

        [TestMethod]
        public void RegExInput_StringRange_ValidSubrange_SizeInBytes()
        {
            var input = new RegExInput("abcdef", 1, 3);

            Assert.AreEqual((nuint)6, input.Size); // 3 chars * 2 bytes
            Assert.AreEqual(RegExEncoding.Utf16LE, input.Encoding);
        }

        [TestMethod]
        public void RegExInput_SafeBuffer_InvalidRange_Throws()
        {
            using var mmf = MemoryMappedFile.CreateNew(null, 16);
            using var accessor = mmf.CreateViewAccessor(0, 16);
            var handle = accessor.SafeMemoryMappedViewHandle;

            TestHelpers.AssertThrows<ArgumentOutOfRangeException>(
                () => new RegExInput(handle, 8, (nuint)handle.ByteLength, RegExEncoding.Latin1));
        }

        [TestMethod]
        public void RegExInput_ByteArray_UsesGivenEncoding()
        {
            var bytes = TestHelpers.Encode("hi", RegExEncoding.Utf8);
            var input = new RegExInput(bytes, RegExEncoding.Utf8);

            Assert.AreEqual(RegExEncoding.Utf8, input.Encoding);
            Assert.AreEqual((nuint)2, input.Size);
        }

        [TestMethod]
        public void RegExInput_ImplicitFromString_Works()
        {
            var regex = RegEx.Create("b");

            // Exercises the implicit string -> RegExInput conversion.
            var text = regex.Search("abc", default, "<none>", m => TestHelpers.WholeMatchText(m));

            Assert.AreEqual("b", text);
        }

        // ---- RegExPinnedBytes

        [TestMethod]
        public unsafe void RegExPinnedBytes_IndexerAndSlice()
        {
            var data = new byte[] { 10, 20, 30, 40, 50 };
            fixed (byte* p = data)
            {
                var bytes = new RegExPinnedBytes(p, (nuint)data.Length);

                Assert.AreEqual((byte)10, bytes[(nuint)0]);
                Assert.AreEqual((byte)50, bytes[(nuint)4]);
                Assert.AreEqual(5, bytes.SizeInt);

                var slice = bytes.Slice(1, 3);
                CollectionAssert.AreEqual(new byte[] { 20, 30, 40 }, slice.ToArray());

                CollectionAssert.AreEqual(new byte[] { 10, 20 }, bytes.First(2).ToArray());
                CollectionAssert.AreEqual(new byte[] { 40, 50 }, bytes.Last(2).ToArray());
            }
        }

        [TestMethod]
        public unsafe void RegExPinnedBytes_IndexOutOfRange_Throws()
        {
            var data = new byte[] { 1, 2, 3 };
            fixed (byte* p = data)
            {
                var bytes = new RegExPinnedBytes(p, (nuint)data.Length);

                // Cannot capture a ref struct in a lambda, so check manually.
                bool threw = false;
                try
                {
                    var _ = bytes[(nuint)3];
                }
                catch (IndexOutOfRangeException)
                {
                    threw = true;
                }

                Assert.IsTrue(threw, "Expected IndexOutOfRangeException.");
            }
        }

        [TestMethod]
        public unsafe void RegExPinnedBytes_SliceOutOfRange_Throws()
        {
            var data = new byte[] { 1, 2, 3 };
            fixed (byte* p = data)
            {
                var bytes = new RegExPinnedBytes(p, (nuint)data.Length);

                bool threw = false;
                try
                {
                    var _ = bytes.Slice(1, 3);
                }
                catch (ArgumentOutOfRangeException)
                {
                    threw = true;
                }

                Assert.IsTrue(threw, "Expected ArgumentOutOfRangeException.");
            }
        }

        [TestMethod]
        public unsafe void RegExPinnedBytes_CopyToAndToString()
        {
            var data = TestHelpers.Encode("hello", RegExEncoding.Latin1);
            fixed (byte* p = data)
            {
                var bytes = new RegExPinnedBytes(p, (nuint)data.Length);

                var dest = new byte[data.Length];
                bytes.CopyTo(dest);
                CollectionAssert.AreEqual(data, dest);

                Assert.AreEqual("hello", bytes.ToString(28591));
            }
        }

        [TestMethod]
        public unsafe void RegExPinnedBytes_EqualityIsByPointerAndSize()
        {
            var data = new byte[] { 1, 2, 3, 4 };
            fixed (byte* p = data)
            {
                var a = new RegExPinnedBytes(p, 4);
                var b = new RegExPinnedBytes(p, 4);
                var c = new RegExPinnedBytes(p, 2);

                Assert.IsTrue(a == b);
                Assert.IsFalse(a == c);
                Assert.IsTrue(a != c);
                Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            }
        }
    }

    [TestClass]
    public class RegExExceptionTests
    {
        [TestMethod]
        public void Message_IncludesErrorCodeAndPattern()
        {
            var ex = new RegExException("a(", RegExSyntaxFlags.ECMAScript, RegExErrorCode.Paren, "mismatched paren");

            Assert.AreEqual("a(", ex.Pattern);
            Assert.AreEqual(RegExSyntaxFlags.ECMAScript, ex.SyntaxFlags);
            Assert.AreEqual(RegExErrorCode.Paren, ex.ErrorCode);
            Assert.AreEqual("mismatched paren", ex.NativeMessage);
            StringAssert.Contains(ex.Message, "a(");
            StringAssert.Contains(ex.Message, "Paren");
        }

        [TestMethod]
        public void NativeMessage_DefaultsToErrorCode_WhenNull()
        {
            var ex = new RegExException("a(", RegExSyntaxFlags.ECMAScript, RegExErrorCode.Paren, null);

            Assert.AreEqual(RegExErrorCode.Paren.ToString(), ex.NativeMessage);
        }
    }
}
