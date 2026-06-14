namespace msandbox
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// Owns the COM object produced by RegEx.Match / RegEx.Search and releases
    /// it on Dispose. Hand the non-owning RegExMatchResults view (via .Results
    /// or the implicit conversion) to code that just inspects a match, so the
    /// same helper can accept a result whether it came from Match/Search or
    /// from a RegExMatchEnumerator.Current.
    /// </summary>
    internal readonly ref struct RegExMatch
    {
        private readonly RepStrRegEx.IRegExMatchResults inner;
        private readonly PinnedBytes input;

        internal RegExMatch(RepStrRegEx.IRegExMatchResults inner, PinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        /// <summary>True if Match/Search found a match (non-null result).</summary>
        public bool Success => inner != null;

        /// <summary>
        /// The non-owning view over this match. Only valid while this owner is
        /// alive (not yet Disposed) and while the input remains pinned.
        /// </summary>
        public RegExMatchResults Results => new RegExMatchResults(inner, input);

        public static implicit operator RegExMatchResults(RegExMatch self) => self.Results;

        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }
    }
}
