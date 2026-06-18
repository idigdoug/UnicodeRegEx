namespace UnicodeRegEx
{
    /// <summary>
    /// A re-enumerable view of the matches in an input buffer. Each call to
    /// <see cref="GetEnumerator"/> creates a fresh native cursor that scans from the
    /// beginning, so this may be enumerated more than once (each enumeration re-runs
    /// the search). The returned <see cref="RegExMatchEnumerator"/> owns its cursor;
    /// <c>foreach</c> disposes it automatically.
    ///
    /// The input must remain valid (pinned) for the duration of every enumeration.
    /// </summary>
    public readonly ref struct RegExMatchEnumerable
    {
        private readonly Interop.IRegEx regex;
        private readonly RegExPinnedBytes input;
        private readonly int inputCodePage;
        private readonly RegExEnumerateOptions options;

        internal RegExMatchEnumerable(
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
        public RegExMatchEnumerator GetEnumerator()
        {
            var bytes = new Interop.RegExBytes { data = (nint)input.Data, size = (nint)input.Size };
            var cursor = regex.EnumerateMatches(bytes, (uint)inputCodePage, (long)options.StartByteOffset, (Interop.RegExMatchFlags)options.MatchFlags);
            if (options.FormatTemplate != null)
            {
                cursor.SetFormatTemplate(options.FormatTemplate, (Interop.RegExFormatFlags)options.FormatFlags);
            }

            return new RegExMatchEnumerator(cursor, input);
        }
    }
}
