namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;

    /// <summary>Options for <see cref="RegEx"/> match and search operations.</summary>
    public struct RegExMatchOptions
    {
        /// <summary>Byte offset within the input to start at. Bytes before it are not searched.</summary>
        public nuint StartByteOffset;

        /// <summary>Flags controlling match behavior.</summary>
        public RegExMatchFlags MatchFlags;
    }

    /// <summary>Options for <see cref="RegEx"/> replace operations.</summary>
    public struct RegExReplaceOptions
    {
        /// <summary>Byte offset within the input to start at. Bytes before it are copied to the output unchanged.</summary>
        public nuint StartByteOffset;

        /// <summary>Flags controlling match behavior.</summary>
        public RegExMatchFlags MatchFlags;

        /// <summary>Flags controlling how the format template is applied.</summary>
        public RegExFormatFlags FormatFlags;
    }

    /// <summary>Options for <see cref="RegEx"/> enumeration operations.</summary>
    public struct RegExEnumerateOptions
    {
        /// <summary>Byte offset within the input to start at. Bytes before it are not searched.</summary>
        public nuint StartByteOffset;

        /// <summary>Flags controlling match behavior.</summary>
        public RegExMatchFlags MatchFlags;

        /// <summary>Flags controlling how <see cref="FormatTemplate"/> is applied.</summary>
        public RegExFormatFlags FormatFlags;

        /// <summary>Optional format template to preset on the enumerator for use by Format().</summary>
        public string? FormatTemplate;
    }

    /// <summary>
    /// Base for the wrapper stream types. Owns the underlying COM object and exposes it as an
    /// <see cref="Interop.ISequentialStream"/> (the only capability used today: sequential writes
    /// driven by <c>FormatTo</c>/<c>CopyInputTo</c>). Releases the object on <see cref="Dispose"/>.
    /// Derived types add the verbs specific to their interface; the broader <c>IStream</c> surface
    /// (seek, stat, clone) is intentionally not wrapped yet.
    /// </summary>
    public abstract class RegExSequentialStream : IDisposable
    {
        private Interop.ISequentialStream? inner;

        private protected RegExSequentialStream(Interop.ISequentialStream inner)
        {
            this.inner = inner;
        }

        /// <summary>
        /// The underlying stream as an <see cref="Interop.ISequentialStream"/>. Throws
        /// <see cref="ObjectDisposedException"/> if this object has been disposed.
        /// </summary>
        public Interop.ISequentialStream SequentialStream =>
            inner ?? throw new ObjectDisposedException(GetType().Name);

        /// <summary>
        /// Writes the bytes of <paramref name="bytes"/> to the stream verbatim (no encoding conversion).
        /// A default or empty segment writes nothing. The interop is done here so callers never touch the
        /// <see cref="Interop.ISequentialStream"/> type directly (which avoids embedded-interop-type issues
        /// in other assemblies).
        /// </summary>
        public void Write(ArraySegment<byte> bytes)
        {
            if (bytes.Count == 0)
            {
                return;
            }

            // RemoteWrite takes a ref to the first byte; passing ref of the segment's element pins it for
            // the duration of the call, so the offset is honored with no copy.
            SequentialStream.RemoteWrite(ref bytes.Array![bytes.Offset], (uint)bytes.Count, out _);
        }

        /// <summary>
        /// If this object has not yet been disposed, calls FinalReleaseComObject.
        /// </summary>
        public void Dispose()
        {
            var old = Interlocked.Exchange(ref inner, null);
            if (old != null)
            {
                Marshal.FinalReleaseComObject(old);
            }
        }
    }

    /// <summary>
    /// Owns an <see cref="Interop.IRegExMemoryStream"/> and releases it on <see cref="RegExSequentialStream.Dispose"/>.
    /// </summary>
    public sealed class RegExMemoryStream : RegExSequentialStream
    {
        internal RegExMemoryStream(Interop.IRegExMemoryStream inner)
            : base(inner)
        {
        }

        /// <summary>
        /// The underlying stream. Throws <see cref="ObjectDisposedException"/> if this object has been disposed.
        /// </summary>
        public Interop.IRegExMemoryStream Value => (Interop.IRegExMemoryStream)SequentialStream;

        /// <summary>
        /// Returns the bytes currently in the stream. Throws <see cref="ObjectDisposedException"/> if this object has been disposed.
        /// </summary>
        public RegExPinnedBytes Buffer
        {
            get
            {
                var buffer = Value.Buffer;
                return new RegExPinnedBytes(unchecked((nuint)buffer.data), checked((nuint)buffer.size));
            }
        }

        /// <summary>
        /// Discards the buffered contents and resets the stream position to 0, retaining capacity for reuse.
        /// </summary>
        public void Reset() => Value.Reset();

        /// <summary>
        /// Pre-grows the backing buffer so writes up to <paramref name="capacity"/> bytes do not reallocate.
        /// Does not change the stream position or logical size.
        /// </summary>
        public void Reserve(long capacity) => Value.Reserve(capacity);
    }

    /// <summary>
    /// Owns an <see cref="Interop.IRegExFileStream"/> and releases it on <see cref="RegExSequentialStream.Dispose"/>.
    /// </summary>
    public sealed class RegExFileStream : RegExSequentialStream
    {
        internal RegExFileStream(Interop.IRegExFileStream inner)
            : base(inner)
        {
        }

        /// <summary>
        /// The underlying stream. Throws <see cref="ObjectDisposedException"/> if this object has been disposed.
        /// </summary>
        public Interop.IRegExFileStream Value => (Interop.IRegExFileStream)SequentialStream;

        /// <summary>The full path of the underlying file.</summary>
        public string Path => Value.Path;

        /// <summary>Whether I/O has been cancelled and whether the cancellation has completed.</summary>
        public RegExStreamCancelStatus CancelStatus => (RegExStreamCancelStatus)Value.CancelStatus;

        /// <summary>Flushes buffered writes to disk. Throws if the stream has been cancelled.</summary>
        public void Flush() => Value.Flush();

        /// <summary>
        /// Non-blocking. Marks the stream as cancelling and attempts to abort in-progress I/O. After
        /// this, all I/O on the stream fails. See <see cref="WaitForCancelled"/> to await completion.
        /// </summary>
        public void Cancel() => Value.Cancel();

        /// <summary>
        /// Waits up to <paramref name="timeoutMs"/> (use <see cref="Timeout.Infinite"/> for no limit)
        /// for cancellation to complete. <see cref="Cancel"/> must have been called first. Returns
        /// true if the stream reached the cancelled state within the timeout, false on timeout.
        /// </summary>
        public bool WaitForCancelled(int timeoutMs) => Value.WaitForCancelled(unchecked((uint)timeoutMs));

        /// <summary>
        /// Renames the file to <paramref name="destinationPath"/>, committing a replacement stream.
        /// Clears delete-on-close before the rename. Throws if the stream has been cancelled or the
        /// rename fails (e.g. the destination exists without <see cref="RegExFileMoveFlags.ReplaceExisting"/>).
        /// </summary>
        public void MoveTo(string destinationPath, RegExFileMoveFlags flags) =>
            Value.MoveTo(destinationPath, (Interop.RegExFileMoveFlags)flags);

        /// <summary>
        /// Cancels this stream when <paramref name="token"/> is cancelled. Dispose the returned
        /// registration to unlink.
        /// </summary>
        public IDisposable LinkCancellation(CancellationToken token) =>
            // If already cancelled, cancel immediately; Register handles this synchronously.
            token.Register(static s => ((RegExFileStream)s!).Cancel(), this);
    }

    /// <summary>
    /// Encoding helpers.
    /// </summary>
    public sealed class RegExEncoding
    {
        private static Encoding? latin1;

        /// <summary>
        /// Cached instance of Encoding.GetEncoding(28591).
        /// </summary>
        public static Encoding Latin1 => latin1 ??= Encoding.GetEncoding(RegExCodePage.Latin1);

        /// <summary>
        /// Returns an <see cref="Encoding"/> for the given code page, resolving well-known code pages
        /// (those defined in <see cref="RegExCodePage"/>) to a cached instance. Other code pages are
        /// resolved by calling <see cref="Encoding.GetEncoding(int)"/>.
        /// </summary>
        public static Encoding FromCodePage(int codePage)
        {
            switch (codePage)
            {
                case RegExCodePage.Utf8:
                    return Encoding.UTF8;
                case RegExCodePage.Utf16LE:
                    return Encoding.Unicode;
                case RegExCodePage.Utf16BE:
                    return Encoding.BigEndianUnicode;
                case RegExCodePage.Latin1:
                    return Latin1;
                default:
                    return Encoding.GetEncoding(codePage);
            }
        }
    }
}
