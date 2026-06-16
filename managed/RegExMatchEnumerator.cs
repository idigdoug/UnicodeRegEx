namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Iterator over the matches in an input buffer, obtained from
    /// <see cref="RegExMatchEnumerable.GetEnumerator"/>. Owns the native cursor created
    /// for it and releases it on <see cref="Dispose"/>; <c>foreach</c> disposes it
    /// automatically.
    /// </summary>
    public readonly ref struct RegExMatchEnumerator
    {
        private readonly Interop.IRegExMatchEnumerator inner;
        private readonly RegExPinnedBytes input;

        internal RegExMatchEnumerator(Interop.IRegExMatchEnumerator inner, RegExPinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        /// <summary>The current match. Throws if the enumeration is before the first or after the last match.</summary>
        public RegExMatch Current =>
            inner.State == Interop.RegExEnumerationState.RegExEnumerationState_enumerating
            ? new RegExMatch(inner, input)
            : throw new InvalidOperationException("Enumeration is before-begin or after-end.");

        /// <summary>Advances to the next match. Returns false when there are no more matches.</summary>
        public bool MoveNext() => inner.NextMatch();

        /// <summary>Releases the native cursor owned by this enumerator.</summary>
        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }
    }
}
