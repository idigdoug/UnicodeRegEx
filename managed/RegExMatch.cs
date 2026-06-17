namespace UnicodeRegEx
{
    /// <summary>
    /// Represents the result of a successful regex match.
    /// Obtained from a RegExMatchResult (via .Match) or from RegExMatchEnumerator.Current.
    /// </summary>
    public readonly ref struct RegExMatch
    {
        private readonly Interop.IRegExMatchResults inner;
        private readonly RegExPinnedBytes input;

        internal RegExMatch(Interop.IRegExMatchResults inner, RegExPinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        /// <summary>
        /// Returns the input span that the regex ran against.
        /// </summary>
        public RegExPinnedBytes Input => input;

        /// <summary>
        /// Returns the code page of the input.
        /// </summary>
        public RegExCodePage InputCodePage => (RegExCodePage)inner.InputCodePage;

        /// <summary>
        /// Returns the text of the whole match (sub-match 0), decoded using the input code page.
        /// </summary>
        public string Text => GetSubMatchText(0)!;

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
        /// Returns the text of the specified sub-match, decoded using the input code page, or
        /// <c>null</c> if the sub-match did not participate in the match (e.g. an optional group).
        /// A participating but zero-length sub-match returns the empty string (distinct from <c>null</c>).
        /// Throws if subMatchIndex is out of range (based on SubMatchCount).
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="TryGetSubMatchBytes"/> (which must use the Try pattern because
        /// <see cref="RegExPinnedBytes"/> is a ref struct and cannot be null), the text accessor can
        /// signal "did not participate" with <c>null</c>, so no Try overload is needed.
        /// </remarks>
        public string? GetSubMatchText(int subMatchIndex)
        {
            var subMatch = GetSubMatch(subMatchIndex);
            return subMatch.Matched
                ? CopyInput(subMatch.Begin, checked((int)subMatch.Size))
                : null;
        }

        /// <summary>
        /// Sets the format parameters to be used for formatting this match (e.g. in a replacement pattern).
        /// </summary>
        public void SetFormatTemplate(string formatTemplate, RegExFormatFlags formatFlags)
        {
            inner.SetFormatTemplate(formatTemplate, (Interop.RegExFormatFlags)formatFlags);
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
        public void FormatTo(Interop.ISequentialStream outputStream, RegExCodePage outputCodePage)
        {
            inner.FormatTo(outputStream, (Interop.RegExCodePage)outputCodePage);
        }

        /// <summary>
        /// Converts the specified span of input to a string and returns it.
        /// </summary>
        public string CopyInput(nuint inputOffset, int size)
        {
            return inner.CopyInput((long)inputOffset, checked((uint)size));
        }

        /// <summary>
        /// Converts the specified span of input to a string in the specified code page and writes it to the specified output stream.
        /// </summary>
        public void CopyInputTo(nuint inputOffset, nuint size, Interop.ISequentialStream outputStream, RegExCodePage outputCodePage)
        {
            inner.CopyInputTo((long)inputOffset, (long)size, outputStream, (Interop.RegExCodePage)outputCodePage);
        }
    }
}
