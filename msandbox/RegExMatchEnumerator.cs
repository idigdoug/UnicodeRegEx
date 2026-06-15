namespace msandbox
{
    using System;
    using System.Runtime.InteropServices;

    internal readonly ref struct RegExMatchEnumerator
    {
        private readonly RepStrRegEx.IRegExMatchEnumerator inner;
        private readonly RegExPinnedBytes input;

        internal RegExMatchEnumerator(RepStrRegEx.IRegExMatchEnumerator inner, RegExPinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        public RegExEnumerationState State => (RegExEnumerationState)inner.State;

        public RegExMatch Current =>
            inner.State == RepStrRegEx.RegExEnumerationState.RegExEnumerationState_enumerating
            ? new RegExMatch(inner, input)
            : throw new InvalidOperationException("Enumeration is before-begin or after-end.");

        public RegExMatchEnumerator GetEnumerator() => this;

        public bool MoveNext() => inner.NextMatch();

        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }
    }
}
