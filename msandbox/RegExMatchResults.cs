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
        private readonly RepStrRegEx.IRegExMatchResults? inner;
        private readonly PinnedBytes input;

        internal RegExMatchResults(RepStrRegEx.IRegExMatchResults? inner, PinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        public PinnedBytes Input => input;

        /// <summary>
        /// True if the Match/Search found a match. False for no match.
        /// If false, the rest of the methods will throw an exception if called.
        /// </summary>
        public bool Success => inner != null;

        public RegExEncoding InputEncoding
            => inner == null ? RegExEncoding.None : (RegExEncoding)inner.InputEncoding;

        public int SubMatchCount
            => inner == null ? 0 : (int)inner.SubMatchCount;

        public RegExSubMatch GetSubMatch(int subMatchIndex)
        {
            // If inner is null, SubMatchCount returned 0. If they called us anyway,
            // they will get a NullReferenceException here.
            var subMatch = inner!.GetSubMatch((uint)subMatchIndex);
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
            // They should check Success before calling this. If they didn't, we'll get a NullReferenceException here.
            inner!.SetFormatTemplate(formatTemplate, (RepStrRegEx.RegExFormatFlags)formatFlags);
        }

        public string Format()
        {
            // They should check Success before calling this. If they didn't, we'll get a NullReferenceException here.
            return inner!.Format();
        }

        public void FormatTo(RepStrRegEx.ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            // They should check Success before calling this. If they didn't, we'll get a NullReferenceException here.
            inner!.FormatTo(outputStream, (RepStrRegEx.RegExEncoding)outputEncoding);
        }

        public string CopyInput(nuint inputOffset, int size)
        {
            // They should check Success before calling this. If they didn't, we'll get a NullReferenceException here.
            return inner!.CopyInput((long)inputOffset, checked((uint)size));
        }

        public void CopyInputTo(nuint inputOffset, nuint size, RepStrRegEx.ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            // They should check Success before calling this. If they didn't, we'll get a NullReferenceException here.
            inner!.CopyInputTo((long)inputOffset, (long)size, outputStream, (RepStrRegEx.RegExEncoding)outputEncoding);
        }
    }
}
