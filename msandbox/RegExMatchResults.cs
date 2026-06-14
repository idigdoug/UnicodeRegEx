namespace msandbox
{
    /// <summary>
    /// Non-owning view over a match result. Obtained from a RegExMatch (via
    /// .Results) or from RegExMatchEnumerator.Current. Does not release the
    /// underlying COM object; the owner (RegExMatch or RegExMatchEnumerator)
    /// does. Only valid while that owner is alive and the input stays pinned.
    /// </summary>
    internal readonly ref struct RegExMatchResults
    {
        private readonly RepStrRegEx.IRegExMatchResults inner;
        private readonly PinnedBytes input;

        internal RegExMatchResults(RepStrRegEx.IRegExMatchResults inner, PinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        public PinnedBytes Input => input;

        public RegExEncoding InputEncoding => (RegExEncoding)inner.InputEncoding;

        public int SubMatchCount => (int)inner.SubMatchCount;

        public RegExSubMatch GetSubMatch(int subMatchIndex)
        {
            var subMatch = inner.GetSubMatch((uint)subMatchIndex);
            return new RegExSubMatch(
                checked((nuint)subMatch.offset),
                checked((nuint)subMatch.size),
                subMatch.matched != 0);
        }

        public bool TryGetSubMatchBytes(int subMatchIndex, out PinnedBytes subMatchBytes)
        {
            return GetSubMatch(subMatchIndex).TryGetBytes(input, out subMatchBytes);
        }

        public void SetFormatTemplate(string formatTemplate, RegExFormatFlags formatFlags)
        {
            inner.SetFormatTemplate(formatTemplate, (RepStrRegEx.RegExFormatFlags)formatFlags);
        }

        public string Format()
        {
            return inner.Format();
        }

        public void FormatTo(RepStrRegEx.ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            inner.FormatTo(outputStream, (RepStrRegEx.RegExEncoding)outputEncoding);
        }

        public string CopyInput(nuint inputOffset, int size)
        {
            return inner.CopyInput((long)inputOffset, checked((uint)size));
        }

        public void CopyInputTo(nuint inputOffset, nuint size, RepStrRegEx.ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            inner.CopyInputTo((long)inputOffset, (long)size, outputStream, (RepStrRegEx.RegExEncoding)outputEncoding);
        }
    }
}
