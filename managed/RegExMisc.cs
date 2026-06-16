namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;
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

        internal RegExMemoryStream(Interop.IRegExMemoryStream? inner)
        {
            this.inner = inner;
        }

        /// <summary>
        /// If this object has not yet been disposed, returns the underlying stream.
        /// If this object has already been disposed, returns null.
        /// </summary>
        public Interop.IRegExMemoryStream? Value => inner;

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

        internal RegExFileStream(Interop.IRegExFileStream? inner)
        {
            this.inner = inner;
        }

        /// <summary>
        /// If this object has not yet been disposed, returns the underlying stream.
        /// If this object has already been disposed, returns null.
        /// </summary>
        public Interop.IRegExFileStream? Value => inner;

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
}
