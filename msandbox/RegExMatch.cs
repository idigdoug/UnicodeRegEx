namespace msandbox
{
    /// <summary>
    /// Represents the result of a successful regex match.
    /// Obtained from a RegExMatchResult (via .Match) or from RegExMatchEnumerator.Current.
    /// </summary>
    internal readonly ref struct RegExMatch
    {
        private readonly RepStrRegEx.IRegExMatchResults inner;
        private readonly RegExPinnedBytes input;

        internal RegExMatch(RepStrRegEx.IRegExMatchResults inner, RegExPinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        /// <summary>
        /// Returns the input span that the regex ran against.
        /// </summary>
        public RegExPinnedBytes Input => input;

        /// <summary>
        /// Returns the encoding of the input.
        /// </summary>
        public RegExEncoding InputEncoding => (RegExEncoding)inner.InputEncoding;

        /// <summary>
        /// Returns the number of capture groups in this match.
        /// Should always return at least 1 (submatch 0 is "the whole match").
        /// </summary>
        public int SubMatchCount => checked((int)inner.SubMatchCount);

        /// <summary>
        /// Returns the span of the input that a particular capture group matched.
        /// Use subMatchIndex 0 for the whole match, 1 for the first capture group, etc.
        /// Throws if subMatchIndex is out of range (based on SubMatchCount).
        /// </summary>
        public RegExSubMatch GetSubMatch(int subMatchIndex)
        {
            var subMatch = inner.GetSubMatch(checked((uint)subMatchIndex));
            return new RegExSubMatch(
                checked((nuint)subMatch.offset),
                checked((nuint)subMatch.size),
                subMatch.matched != 0);
        }

        /// <summary>
        /// If the specified sub-match participated in the match, returns the span of the input that it matched.
        /// If the specified sub-match did not participate in the match, returns an empty span.
        /// Throws if subMatchIndex is out of range (based on SubMatchCount).
        /// </summary>
        public bool TryGetSubMatchBytes(int subMatchIndex, out RegExPinnedBytes subMatchBytes)
        {
            return GetSubMatch(subMatchIndex).TryGetBytes(input, out subMatchBytes);
        }

        /// <summary>
        /// Sets the format parameters to be used for formatting this match (e.g. in a replacement pattern).
        /// </summary>
        public void SetFormatTemplate(string formatTemplate, RegExFormatFlags formatFlags)
        {
            inner.SetFormatTemplate(formatTemplate, (RepStrRegEx.RegExFormatFlags)formatFlags);
        }

        /// <summary>
        /// Formats this match according to the previously set format template and returns the result.
        /// </summary>
        public string Format()
        {
            return inner.Format();
        }

        /// <summary>
        /// Formats this match according to the previously set format template and returns the result.
        /// </summary>
        public void FormatTo(RepStrRegEx.ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            inner.FormatTo(outputStream, (RepStrRegEx.RegExEncoding)outputEncoding);
        }

        /// <summary>
        /// Converts the specified span of input to a string and returns it.
        /// </summary>
        public string CopyInput(nuint inputOffset, int size)
        {
            return inner.CopyInput((long)inputOffset, checked((uint)size));
        }

        /// <summary>
        /// Converts the specified span of input to a string in the specified encoding and writes it to the specified output stream.
        /// </summary>
        public void CopyInputTo(nuint inputOffset, nuint size, RepStrRegEx.ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            inner.CopyInputTo((long)inputOffset, (long)size, outputStream, (RepStrRegEx.RegExEncoding)outputEncoding);
        }
    }
}
