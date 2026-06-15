namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value.

    internal static class RegExExtensions
    {
        public static IDisposable LinkCancellation(this Interop.IRegExFileStream self, CancellationToken token)
        {
            // If already cancelled, cancel immediately; Register handles this synchronously.
            return token.Register(static s => ((Interop.IRegExFileStream)s!).Cancel(), self);
        }
    }

    internal struct RegExMatchOptions
    {
        public nuint StartByteOffset;
        public RegExMatchFlags MatchFlags;
    }

    internal struct RegExReplaceOptions
    {
        public nuint StartByteOffset;
        public RegExMatchFlags MatchFlags;
        public RegExFormatFlags FormatFlags;
    }

    internal struct RegExEnumerateOptions
    {
        public nuint StartByteOffset;
        public RegExMatchFlags MatchFlags;
        public RegExFormatFlags FormatFlags;
        public string? FormatTemplate;
    }

    /// <summary>
    /// Wrapper to simplify calling Dispose on a COM object.
    /// </summary>
    internal sealed class RegExInterfaceWrapper<T> : IDisposable where T : class
    {
        private T? inner;

        public RegExInterfaceWrapper(T? inner)
        {
            this.inner = inner;
        }

        /// <summary>
        /// If this object has not yet been disposed, returns it.
        /// If this object has already been disposed, returns null.
        /// </summary>
        public T? Value => inner;

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
