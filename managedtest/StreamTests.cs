namespace UnicodeRegEx.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    /// <summary>
    /// Focused on the managed stream wrapper surface and the interop boundary,
    /// not on re-validating native stream I/O (that is covered by the native tests).
    /// </summary>
    [TestClass]
    public class StreamTests
    {
        // ---- RegExInterfaceWrapper<T> (pure managed wrapper)

        [TestMethod]
        public void MemoryStreamWrapper_ValueUsableThenIdempotentDispose()
        {
            var wrapper = RegEx.CreateMemoryStream();

            // Value is non-null and usable before disposal.
            Assert.IsNotNull(wrapper.Value);
            wrapper.Value.Reserve(16);

            // Dispose is idempotent (no throw on the second call).
            wrapper.Dispose();
            wrapper.Dispose();

            // Accessing Value after disposal throws rather than returning a (poisoned) null.
            TestHelpers.AssertThrows<ObjectDisposedException>(() => { var _ = wrapper.Value; });
        }

        [TestMethod]
        public void CreateMemoryStream_NegativeCapacity_Throws()
        {
            // The wrapper converts the capacity with checked((uint)initialCapacity).
            TestHelpers.AssertThrows<OverflowException>(() => RegEx.CreateMemoryStream(-1));
        }

        // ---- Memory stream interop (light)

        [TestMethod]
        public void MemoryStream_Reset_ClearsBuffer()
        {
            var regex = RegEx.Create("a");
            using var stream = RegEx.CreateMemoryStream();

            regex.ReplaceTo("banana", stream.Value, RegExCodePage.Utf16LE, "X");
            Assert.AreNotEqual(0, TestHelpers.ReadAllBytes(stream.Value).Length);

            stream.Value.Reset();
            Assert.AreEqual(0, TestHelpers.ReadAllBytes(stream.Value).Length);
        }

        [TestMethod]
        public void MemoryStream_Reserve_DoesNotChangeLogicalSize()
        {
            using var stream = RegEx.CreateMemoryStream();

            stream.Value.Reserve(4096);

            // Reserve grows capacity only; the logical (buffered) size stays 0.
            Assert.AreEqual(0, TestHelpers.ReadAllBytes(stream.Value).Length);
        }

        // ---- File stream interop + LinkCancellation extension (managed)

        [TestMethod]
        public void FileStream_ExposesPath()
        {
            var finalPath = Path.Combine(Path.GetTempPath(), $"urx_{Guid.NewGuid():N}.tmp");

            using var stream = RegEx.CreateReplacementFileStream(finalPath);

            Assert.IsFalse(string.IsNullOrEmpty(stream.Value.Path));
            Assert.AreEqual(
                RegExStreamCancelStatus.Running,
                (RegExStreamCancelStatus)stream.Value.CancelStatus);
        }

        [TestMethod]
        public void FileStream_LinkCancellation_CancelsOnToken()
        {
            var finalPath = Path.Combine(Path.GetTempPath(), $"urx_{Guid.NewGuid():N}.tmp");

            using var stream = RegEx.CreateReplacementFileStream(finalPath);
            var file = stream.Value;

            using (var cts = new CancellationTokenSource())
            using (file.LinkCancellation(cts.Token))
            {
                // Cancelling the token invokes file.Cancel() through the managed extension.
                cts.Cancel();

                Assert.IsTrue(file.WaitForCancelled(5000));
            }

            Assert.AreEqual(
                RegExStreamCancelStatus.Cancelled,
                (RegExStreamCancelStatus)file.CancelStatus);
        }
    }
}
