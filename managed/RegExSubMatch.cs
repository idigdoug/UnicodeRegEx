namespace UnicodeRegEx
{
    /// <summary>
    /// The location of a capture group within the input, as a byte offset and size.
    /// </summary>
    public readonly struct RegExSubMatch
    {
        /// <summary>The byte offset where this sub-match begins (relative to the input).</summary>
        public readonly nuint Begin;

        /// <summary>The size of this sub-match, in bytes.</summary>
        public readonly nuint Size;

        /// <summary>Whether this sub-match participated in the overall match (e.g. for optional groups).</summary>
        public readonly bool Matched;

        internal RegExSubMatch(nuint begin, nuint size, bool matched)
        {
            this.Begin = begin;
            this.Size = size;
            this.Matched = matched;
        }

        /// <summary>The byte offset where this sub-match ends (relative to the input).</summary>
        public nuint End => checked(Begin + Size);

        /// <summary>Whether this sub-match is zero bytes long.</summary>
        public bool IsEmpty => Size == 0;

        /// <summary>
        /// If Matched, returns inputBytes[Begin..End] (may throw if out of range).
        /// If !Matched, returns default and false.
        /// </summary>
        public bool TryGetBytes(RegExPinnedBytes inputBytes, out RegExPinnedBytes subMatchBytes)
        {
            if (Matched)
            {
                subMatchBytes = inputBytes.Slice(Begin, Size);
                return true;
            }
            else
            {
                subMatchBytes = default;
                return false;
            }
        }
    }
}
