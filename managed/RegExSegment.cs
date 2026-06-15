namespace UnicodeRegEx
{
    using System;

    public readonly ref struct RegExSegment
    {
        private readonly Interop.IRegExMatchResults inner;
        private readonly RegExPinnedBytes input;
        private readonly nuint begin;
        private readonly nuint end;
        private readonly bool isMatch;

        internal RegExSegment(
            Interop.IRegExMatchResults inner,
            RegExPinnedBytes input,
            nuint begin,
            nuint end,
            bool isMatch)
        {
            this.inner = inner;
            this.input = input;
            this.begin = begin;
            this.end = end;
            this.isMatch = isMatch;
        }

        /// <summary>
        /// Returns the input span that the regex ran against.
        /// </summary>
        public RegExPinnedBytes Input => input;

        /// <summary>
        /// Returns the encoding of the input.
        /// </summary>
        public RegExEncoding InputEncoding => (RegExEncoding)inner.InputEncoding;

        public RegExPinnedBytes Bytes => input[begin, end];

        /// <summary>
        /// True if this segment is a matched region, false if this segment is an unmatched region.
        /// </summary>
        public bool IsMatch => isMatch;

        public nuint Begin => begin;
        public nuint End => end;

        /// <summary>
        /// If IsMatch is true, the details of the match.
        /// If IsMatch is false, this will throw an exception.
        /// </summary>
        public RegExMatch Match =>
            isMatch
            ? new RegExMatch(inner, input)
            : throw new InvalidOperationException("No match available.");

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
        public void CopyInputTo(nuint inputOffset, nuint size, Interop.ISequentialStream outputStream, RegExEncoding outputEncoding)
        {
            inner.CopyInputTo((long)inputOffset, (long)size, outputStream, (Interop.RegExEncoding)outputEncoding);
        }
    }
}
