namespace msandbox
{
    using System;
    using System.Runtime.InteropServices;

    internal readonly ref struct RegExMatchResult
    {
        private readonly RepStrRegEx.IRegExMatchResults? inner;
        private readonly RegExPinnedBytes input;

        internal RegExMatchResult(RepStrRegEx.IRegExMatchResults? inner, RegExPinnedBytes input)
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

        public void Dispose()
        {
            if (inner != null)
            {
                Marshal.FinalReleaseComObject(inner);
            }
        }
    }
}
