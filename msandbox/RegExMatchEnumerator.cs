namespace msandbox
{
    using System.Runtime.InteropServices;

    internal readonly ref struct RegExMatchEnumerator
    {
        private readonly RepStrRegEx.IRegExMatchEnumerator inner;
        private readonly PinnedBytes input;

        internal RegExMatchEnumerator(RepStrRegEx.IRegExMatchEnumerator inner, PinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        public RegExEnumerationState State => (RegExEnumerationState)inner.State;

        public RegExMatchEnumerator GetEnumerator() => this;

        public RegExMatchResults Current => new RegExMatchResults(inner, input);

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
