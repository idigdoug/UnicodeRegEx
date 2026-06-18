namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;

    /// <summary>Extension helpers for the regex wrapper types.</summary>
    public static class RegExExtensions
    {
        /// <summary>
        /// Cancels <paramref name="self"/> when <paramref name="token"/> is cancelled. Dispose the returned
        /// registration to unlink.
        /// </summary>
        public static IDisposable LinkCancellation(this Interop.IRegExFileStream self, CancellationToken token)
        {
            // If already cancelled, cancel immediately; Register handles this synchronously.
            return token.Register(static s => ((Interop.IRegExFileStream)s!).Cancel(), self);
        }
    }

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
    /// Owns an <see cref="Interop.IRegExMemoryStream"/> and releases it on <see cref="Dispose"/>.
    /// </summary>
    public sealed class RegExMemoryStream : IDisposable
    {
        private Interop.IRegExMemoryStream? inner;

        internal RegExMemoryStream(Interop.IRegExMemoryStream inner)
        {
            this.inner = inner;
        }

        /// <summary>
        /// The underlying stream. Throws <see cref="ObjectDisposedException"/> if this object has been disposed.
        /// </summary>
        public Interop.IRegExMemoryStream Value =>
            inner ?? throw new ObjectDisposedException(nameof(RegExMemoryStream));

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
    /// Owns an <see cref="Interop.IRegExFileStream"/> and releases it on <see cref="Dispose"/>.
    /// </summary>
    public sealed class RegExFileStream : IDisposable
    {
        private Interop.IRegExFileStream? inner;

        internal RegExFileStream(Interop.IRegExFileStream inner)
        {
            this.inner = inner;
        }

        /// <summary>
        /// The underlying stream. Throws <see cref="ObjectDisposedException"/> if this object has been disposed.
        /// </summary>
        public Interop.IRegExFileStream Value =>
            inner ?? throw new ObjectDisposedException(nameof(RegExFileStream));

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
