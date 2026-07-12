namespace UnicodeRegEx
{
    /// <summary>
    /// A re-enumerable view of the input as a sequence of <see cref="RegExSegment"/>s
    /// alternating between unmatched text and matches, covering the whole input. Each call
    /// to <see cref="GetEnumerator"/> creates a fresh native cursor that scans from the
    /// beginning, so this may be enumerated more than once (each enumeration re-runs the
    /// search). The returned <see cref="RegExSegmentEnumerator"/> owns its cursor;
    /// <c>foreach</c> disposes it automatically.
    ///
    /// The input must remain valid (pinned) for the duration of every enumeration.
    /// </summary>
    public readonly ref struct RegExSegmentEnumerable
    {
        private readonly Interop.IRegEx regex;
        private readonly RegExPinnedBytes input;
        private readonly int inputCodePage;
        private readonly RegExEnumerateOptions options;

        internal RegExSegmentEnumerable(
            Interop.IRegEx regex,
            RegExPinnedBytes input,
            int inputCodePage,
            RegExEnumerateOptions options)
        {
            this.regex = regex;
            this.input = input;
            this.inputCodePage = inputCodePage;
            this.options = options;
        }

        /// <summary>
        /// Creates a fresh enumerator (and native cursor) that scans from the beginning.
        /// Enables <c>foreach</c>.
        /// </summary>
        public RegExSegmentEnumerator GetEnumerator()
        {
            var bytes = new Interop.RegExBytes { data = (nint)input.Data, size = (nint)input.Size };
            var cursor = regex.EnumerateMatches(bytes, (uint)inputCodePage, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags);
            if (options.FormatTemplate != null)
            {
                cursor.SetFormatTemplate(options.FormatTemplate, (Interop.RegExFormatFlags)options.FormatFlags);
            }

            return new RegExSegmentEnumerator(cursor, input);
        }

        /// <summary>
        /// The pinned input bytes being enumerated. Valid for as long as this enumerable is (the input stays
        /// pinned for the enumeration). Useful for building a <see cref="RegExLineCounter"/> over the same
        /// bytes the segments index into.
        /// </summary>
        public RegExPinnedBytes Input => input;

        /// <summary>The code page of the input being enumerated.</summary>
        public int InputCodePage => inputCodePage;
    }
}
