namespace UnicodeRegEx
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// A forward cursor that tracks the 1-based line number at a byte offset within a pinned buffer,
    /// without decoding the whole input to a string. Bind it to the input once, then call
    /// <see cref="AdvanceTo"/> with successive (non-decreasing) byte offsets — for example the start
    /// of each match — and read <see cref="LineNumber"/>. Line breaks are LF, CR, or CRLF; a CR that
    /// straddles two <see cref="AdvanceTo"/> calls is resolved on the first call.
    /// </summary>
    /// <remarks>
    /// This is a <see langword="ref"/> struct because it holds a <see cref="RegExPinnedBytes"/>; the
    /// underlying memory must stay valid for the counter's lifetime.
    /// </remarks>
    public ref struct RegExLineCounter
    {
        private readonly RegExPinnedBytes input;
        private readonly ushort cr;
        private readonly ushort lf;
        private readonly bool utf16;
        private bool lastCharWasCarriageReturn;
        private nuint offset;
        private nuint lineNumber;

        // The byte offset at which the current line begins (just past the most recent line break), used to
        // derive the column. Column is measured in code units from the line start.
        private nuint lineStartOffset;

        /// <summary>
        /// Creates a counter positioned at offset 0 (<see cref="LineNumber"/> 0) over <paramref name="input"/>.
        /// </summary>
        public RegExLineCounter(RegExPinnedBytes input, int codePage)
        {
            this.input = input;

            switch (codePage)
            {
                case 0: // CP_ACP
                case 1: // CP_OEMCP
                case 2: // CP_MACCP
                case 3: // CP_THREAD_ACP
                    throw new ArgumentOutOfRangeException(nameof(codePage));

                case 12000: // UTF-32LE
                case 12001: // UTF-32BE
                    throw new NotSupportedException(nameof(codePage));

                case RegExCodePage.Utf16LE:
                    cr = 0x000D;
                    lf = 0x000A;
                    utf16 = true;
                    break;

                case RegExCodePage.Utf16BE:
                    cr = 0x0D00;
                    lf = 0x0A00;
                    utf16 = true;
                    break;

                case 37: // IBM EBCDIC - U.S./Canada
                case 500: // IBM EBCDIC - International
                case 870: // IBM EBCDIC - Multilingual/ROECE (Latin-2)
                case 875: // IBM EBCDIC - Modern Greek
                case 1026: // IBM EBCDIC - Turkish (Latin-5)
                case 1140: // IBM EBCDIC - U.S./Canada (37 + Euro)
                case 1141: // IBM EBCDIC - Germany (20273 + Euro)
                case 1142: // IBM EBCDIC - Denmark/Norway (20277 + Euro)
                case 1143: // IBM EBCDIC - Finland/Sweden (20278 + Euro)
                case 1144: // IBM EBCDIC - Italy (20280 + Euro)
                case 1145: // IBM EBCDIC - Latin America/Spain (20284 + Euro)
                case 1146: // IBM EBCDIC - United Kingdom (20285 + Euro)
                case 1148: // IBM EBCDIC - International (500 + Euro)
                case 1149: // IBM EBCDIC - Icelandic (20871 + Euro)
                case 20273: // IBM EBCDIC - Germany
                case 20277: // IBM EBCDIC - Denmark/Norway
                case 20278: // IBM EBCDIC - Finland/Sweden
                case 20280: // IBM EBCDIC - Italy
                case 20284: // IBM EBCDIC - Latin America/Spain
                case 20285: // IBM EBCDIC - United Kingdom
                case 20290: // IBM EBCDIC - Japanese Katakana Extended
                case 20297: // IBM EBCDIC - France
                case 20420: // IBM EBCDIC - Arabic
                case 20423: // IBM EBCDIC - Greek
                case 20424: // IBM EBCDIC - Hebrew
                case 20833: // IBM EBCDIC - Korean Extended
                case 20838: // IBM EBCDIC - Thai
                case 20871: // IBM EBCDIC - Icelandic
                case 20880: // IBM EBCDIC - Cyrillic (Russian)
                case 20905: // IBM EBCDIC - Turkish
                case 21025: // IBM EBCDIC - Cyrillic (Serbian, Bulgarian)
                    cr = 0x0D;
                    lf = 0x25;
                    utf16 = false;
                    break;

                case 1047: // IBM EBCDIC - Latin-1/Open System
                case 20924: // IBM EBCDIC - Latin-1/Open System (1047 + Euro)
                    cr = 0x0D;
                    lf = 0x15;
                    utf16 = false;
                    break;

                default: // ASCII-compatible
                    cr = 0x0D;
                    lf = 0x0A;
                    utf16 = false;
                    break;
            }

            lastCharWasCarriageReturn = false;
            offset = 0;
            lineNumber = 0;
            lineStartOffset = 0;
        }

        /// <summary>
        /// The 1-based line number at the current offset. 0 means no input has been consumed yet;
        /// the first consumed code unit puts the cursor on line 1.
        /// </summary>
        public nuint LineNumber => lineNumber;

        /// <summary>
        /// The 1-based column at the current offset, measured in code units from the start of the current
        /// line (a UTF-16 code unit is 2 bytes; all other code pages are single-byte). The first code unit
        /// of a line is column 1. On a freshly-constructed counter (before any <see cref="AdvanceTo"/>) this
        /// is 1 while <see cref="LineNumber"/> is 0.
        /// </summary>
        public nuint Column
        {
            get
            {
                var bytePos = offset - lineStartOffset;
                if (utf16)
                {
                    return bytePos / 2 + 1;
                }
                else
                {
                    return bytePos + 1;
                }
            }
        }

        /// <summary>The byte offset the cursor has advanced to.</summary>
        public nuint Offset => offset;

        /// <summary>
        /// Advances the cursor to <paramref name="target"/>, counting line breaks in the bytes between
        /// the current offset and <paramref name="target"/>. <paramref name="target"/> must be at or
        /// after the current offset and within the input. For UTF-16 inputs it should fall on a code-unit
        /// (even byte) boundary — match offsets always do.
        /// </summary>
        public void AdvanceTo(nuint target)
        {
            Debug.Assert(cr != 0); // Not initialized.
            Debug.Assert(offset <= target);

            if (target > input.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(target), "Target must be within the input and at or after the current offset.");
            }

            // Any advance puts the cursor onto line 1 (the input starts on line 1); subsequent line breaks
            // move it forward. AdvanceTo is always called with a real position, so the reported line/column
            // are 1-based from the first call. LineNumber stays 0 only before the first AdvanceTo.
            if (lineNumber == 0)
            {
                lineNumber = 1;
            }

            unsafe
            {
                if (utf16)
                {
                    var data = (ushort*)input.DataPtr;
                    var end = target / sizeof(ushort);
                    var pos = offset / sizeof(ushort);
                    for (; pos < end; pos++)
                    {
                        CountUnit(data[pos], (pos + 1) * sizeof(ushort));
                    }

                    offset = pos * sizeof(ushort);
                }
                else
                {
                    var data = input.DataPtr;
                    var end = target;
                    var pos = offset;
                    for (; pos < end; pos++)
                    {
                        CountUnit(data[pos], pos + 1);
                    }

                    offset = pos;
                }
            }
        }

        // Consumes one code unit that ends at byte offset unitEndOffset (the offset just past it). On a line
        // break, lineStartOffset moves to that end offset so the next unit is column 1 of the new line.
        private void CountUnit(ushort unit, nuint unitEndOffset)
        {
            if (unit == cr)
            {
                // A CR is a line break on sight; the flag only suppresses a following LF (CRLF).
                lineNumber++;
                lineStartOffset = unitEndOffset;
                lastCharWasCarriageReturn = true;
            }
            else if (unit == lf)
            {
                // Count the LF unless it is the LF half of a CR-LF pair already counted by the CR; either
                // way the next line starts just past the LF.
                if (!lastCharWasCarriageReturn)
                {
                    lineNumber++;
                }

                lineStartOffset = unitEndOffset;
                lastCharWasCarriageReturn = false;
            }
            else
            {
                lastCharWasCarriageReturn = false;
            }
        }
    }
}
