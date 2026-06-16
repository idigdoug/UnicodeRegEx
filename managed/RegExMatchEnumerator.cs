namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Enumerates the matches in an input buffer. Supports <c>foreach</c>; must be disposed.
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

        /// <summary>The current position of the enumeration (not-started, enumerating, or finished).</summary>
        public RegExEnumerationState State => (RegExEnumerationState)inner.State;

        /// <summary>The current match. Throws if the enumeration is before the first or after the last match.</summary>
        public RegExMatch Current =>
            inner.State == Interop.RegExEnumerationState.RegExEnumerationState_enumerating
            ? new RegExMatch(inner, input)
            : throw new InvalidOperationException("Enumeration is before-begin or after-end.");

        /// <summary>Returns this enumerator (enables <c>foreach</c>).</summary>
        public RegExMatchEnumerator GetEnumerator() => this;

        /// <summary>Advances to the next match. Returns false when there are no more matches.</summary>
        public bool MoveNext() => inner.NextMatch();

        /// <summary>Releases the underlying native enumerator.</summary>
        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }
    }
}
