namespace UnicodeRegEx
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// The outcome of a single <see cref="RegEx.Match(RegExPinnedBytes, RegExCodePage, RegExMatchOptions)"/> or
    /// <see cref="RegEx.Search(RegExPinnedBytes, RegExCodePage, RegExMatchOptions)"/> call. Must be disposed.
    /// </summary>
    public readonly ref struct RegExMatchResult
    {
        private readonly Interop.IRegExMatchResults? inner;
        private readonly RegExPinnedBytes input;

        internal RegExMatchResult(Interop.IRegExMatchResults? inner, RegExPinnedBytes input)
        {
            this.inner = inner;
            this.input = input;
        }

        /// <summary>
        /// True if Match/Search found a match, false if regex did not match.
        /// </summary>
        public bool IsMatch => inner != null;

        /// <summary>
        /// If IsMatch is true, the details of the match.
        /// If IsMatch is false, this will throw an exception.
        /// </summary>
        public RegExMatch Match =>
            inner != null
            ? new RegExMatch(inner, input)
            : throw new InvalidOperationException("No match available.");

        /// <summary>Releases the underlying native match object.</summary>
        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }
    }
}
