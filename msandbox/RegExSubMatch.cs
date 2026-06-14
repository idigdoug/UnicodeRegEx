namespace msandbox
{
    internal readonly struct RegExSubMatch
    {
        public readonly nuint Begin;
        public readonly nuint Size;
        public readonly bool Matched;

        public RegExSubMatch(nuint begin, nuint size, bool matched)
        {
            this.Begin = begin;
            this.Size = size;
            this.Matched = matched;
        }

        public nuint End => checked(Begin + Size);
        public bool IsEmpty => Size == 0;

        /// <summary>
        /// If Matched, returns inputBytes[Begin..End] (may throw if out of range).
        /// If !Matched, returns default and false.
        /// </summary>
        public bool TryGetBytes(PinnedBytes inputBytes, out PinnedBytes subMatchBytes)
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
