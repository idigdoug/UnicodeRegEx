namespace UnicodeRegEx.Tools
{
    using System;
    using UnicodeRegEx;

    /// <summary>
    /// The individual steps of <see cref="EncodingDetector"/>, each independently enable-able. All are
    /// on by default (<see cref="EncodingDetectionOptions.Default"/>); an "advanced" configuration can
    /// turn specific steps off to handle special cases.
    /// </summary>
    [Flags]
    public enum EncodingDetectionSteps
    {
        /// <summary>No detection steps; detection falls straight through to the default code page and reports not-binary.</summary>
        None = 0,

        /// <summary>Recognize a UTF-8/UTF-16LE/UTF-16BE byte-order mark.</summary>
        Bom = 1 << 0,

        /// <summary>Detect UTF-16 (LE/BE) from its NUL-parity signature when there is no BOM.</summary>
        Utf16Heuristic = 1 << 1,

        /// <summary>Detect UTF-8 from strict validation plus positive multibyte evidence when there is no BOM.</summary>
        Utf8Heuristic = 1 << 2,

        /// <summary>Treat a non-UTF-16 file as binary if it contains a NUL byte.</summary>
        BinaryNul = 1 << 3,

        /// <summary>Treat a non-UTF-16 file as binary if it has a high ratio of non-text control bytes.</summary>
        BinaryControlRatio = 1 << 4,

        /// <summary>All detection steps.</summary>
        All = Bom | Utf16Heuristic | Utf8Heuristic | BinaryNul | BinaryControlRatio,
    }

    /// <summary>
    /// Configuration for <see cref="EncodingDetector.Detect"/>. Currently selects which steps run; this
    /// is a struct so it can grow (e.g. to carry thresholds) without changing the detector's signature.
    /// Use <see cref="Default"/> for the standard, all-steps-on behavior.
    /// </summary>
    public readonly struct EncodingDetectionOptions
    {
        public EncodingDetectionOptions(EncodingDetectionSteps steps)
        {
            Steps = steps;
        }

        /// <summary>The detection steps to run.</summary>
        public EncodingDetectionSteps Steps { get; }

        /// <summary>The standard options: every detection step enabled.</summary>
        public static EncodingDetectionOptions Default => new EncodingDetectionOptions(EncodingDetectionSteps.All);

        internal bool Has(EncodingDetectionSteps step) => (Steps & step) != 0;
    }

    /// <summary>
    /// The facts a <see cref="EncodingDetector"/> derived from a file's leading bytes: the code page
    /// to interpret the file with, and whether the file looks like non-text (binary) content. This is
    /// purely a verdict — what to *do* about a binary file (skip, error, search anyway) is a policy the
    /// caller applies, not part of detection.
    /// </summary>
    public readonly struct EncodingDetectionResult
    {
        public EncodingDetectionResult(int codePage, bool looksBinary)
        {
            CodePage = codePage;
            LooksBinary = looksBinary;
        }

        /// <summary>The code page the file should be decoded with.</summary>
        public int CodePage { get; }

        /// <summary>True if the file appears to be non-text content.</summary>
        public bool LooksBinary { get; }
    }

    /// <summary>
    /// Determines a file's text encoding and whether it looks binary, from its leading bytes. Pure and
    /// front-end-neutral: it reads bytes and returns an <see cref="EncodingDetectionResult"/>, allocating
    /// nothing, so it is cheap to run on every file (including across threads).
    /// </summary>
    /// <remarks>
    /// Detection runs an ordered set of steps (see <see cref="EncodingDetectionSteps"/>) over the first
    /// ~8&#160;KB: a UTF-8/UTF-16LE/UTF-16BE byte-order mark (dispositive); then a UTF-16 NUL-parity check
    /// (ASCII-heavy UTF-16 has a NUL in every other byte, at a consistent parity); then strict UTF-8
    /// validation requiring positive evidence (a well-formed multibyte sequence and no invalid one);
    /// otherwise the caller's default code page. A non-UTF-16 file is then judged binary if it contains a
    /// NUL or a high ratio of non-text control bytes. Each step can be disabled via
    /// <see cref="EncodingDetectionOptions"/>; the default enables all of them. A Length heuristic is
    /// planned (which is why <c>fileLength</c> is already taken). Thresholds are fixed for now.
    /// </remarks>
    public static class EncodingDetector
    {
        // Bytes of the file head scanned by the heuristics (kept at the original 8000, not 8192).
        private const int ScanWindow = 8000;

        // UTF-16 (NUL-parity) thresholds: enough NULs to be meaningful, and a strong parity skew so a
        // genuinely binary file (NULs at both parities) is not mistaken for ASCII-heavy UTF-16 text.
        private const int Utf16MinNulCount = 8;
        private const double Utf16DominantParityFraction = 0.90;

        // UTF-8: assert only with positive evidence — at least one well-formed multibyte sequence and
        // zero invalid sequences (a sequence truncated by the scan window is treated as incomplete).
        private const int Utf8MinMultiByteSequences = 1;

        // Binary (control ratio): fraction of "non-text" control bytes above which the sample looks
        // binary. Text whitespace (tab/LF/CR/FF) is excluded from the count.
        private const double BinaryControlByteFraction = 0.30;

        /// <summary>
        /// Detects the encoding and binary verdict for a file whose leading bytes are
        /// <paramref name="leadingBytes"/> (typically the whole memory-mapped view).
        /// <paramref name="defaultCodePage"/> is used when no encoding
        /// heuristic applies.
        /// </summary>
        public static unsafe EncodingDetectionResult Detect(
            RegExPinnedBytes leadingBytes, int defaultCodePage, EncodingDetectionOptions options)
        {
            var data = leadingBytes.DataPtr;
            var available = (long)leadingBytes.Size;
            var sample = (int)Math.Min(available, ScanWindow);

            var codePage = DetectCodePage(data, available, sample, defaultCodePage, options);

            // The binary check does not apply to UTF-16 (its embedded NULs are expected text bytes).
            var looksBinary =
                codePage != RegExCodePage.Utf16LE &&
                codePage != RegExCodePage.Utf16BE &&
                LooksBinary(data, sample, options);

            return new EncodingDetectionResult(codePage, looksBinary);
        }

        // Ordered encoding heuristics: a byte-order mark is dispositive; otherwise the UTF-16 NUL-parity
        // signature is more specific than UTF-8 validity, so it is checked first; then strict UTF-8;
        // then the caller's default. Each step is gated by its bit in <paramref name="options"/>.
        private static unsafe int DetectCodePage(byte* data, long length, int sample, int defaultCodePage, EncodingDetectionOptions options)
        {
            if (options.Has(EncodingDetectionSteps.Bom))
            {
                if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                {
                    return RegExCodePage.Utf8;
                }

                if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                {
                    return RegExCodePage.Utf16LE;
                }

                if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                {
                    return RegExCodePage.Utf16BE;
                }
            }

            if (options.Has(EncodingDetectionSteps.Utf16Heuristic) && TryDetectUtf16(data, sample, out var utf16CodePage))
            {
                return utf16CodePage;
            }

            if (options.Has(EncodingDetectionSteps.Utf8Heuristic) && IsLikelyUtf8(data, sample, truncated: sample < length))
            {
                return RegExCodePage.Utf8;
            }

            return defaultCodePage;
        }

        // ASCII-heavy UTF-16 holds a NUL in every other byte: at odd offsets for little-endian text,
        // at even offsets for big-endian. Require a meaningful number of NULs and a strong skew toward
        // one parity (the other near-empty), which a binary file with scattered NULs will not show.
        private static unsafe bool TryDetectUtf16(byte* data, int sample, out int codePage)
        {
            codePage = 0;

            var nulEven = 0;
            var nulOdd = 0;
            for (var i = 0; i < sample; i++)
            {
                if (data[i] == 0)
                {
                    if ((i & 1) == 0)
                    {
                        nulEven++;
                    }
                    else
                    {
                        nulOdd++;
                    }
                }
            }

            var total = nulEven + nulOdd;
            if (total < Utf16MinNulCount)
            {
                return false;
            }

            if (nulOdd >= total * Utf16DominantParityFraction)
            {
                codePage = RegExCodePage.Utf16LE;
                return true;
            }

            if (nulEven >= total * Utf16DominantParityFraction)
            {
                codePage = RegExCodePage.Utf16BE;
                return true;
            }

            return false;
        }

        // Strict UTF-8 validation over the sample. Returns true only with positive evidence: at least
        // one well-formed multibyte sequence and no invalid sequence. A multibyte sequence that runs
        // past the sample is incomplete (ignored) only when <paramref name="truncated"/> is true (the
        // scan window cut it off); if the sample is the whole file, a sequence that runs off the end is
        // a malformed trailing sequence and makes the content invalid.
        private static unsafe bool IsLikelyUtf8(byte* data, int sample, bool truncated)
        {
            var multiByteSequences = 0;

            var i = 0;
            while (i < sample)
            {
                var b = data[i];
                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                int extra;       // continuation bytes expected after the lead byte
                int min;         // smallest code point this length may legally encode (overlong guard)
                int codePoint;
                if (b >= 0xC2 && b <= 0xDF)
                {
                    extra = 1;
                    min = 0x80;
                    codePoint = b & 0x1F;
                }
                else if (b >= 0xE0 && b <= 0xEF)
                {
                    extra = 2;
                    min = 0x800;
                    codePoint = b & 0x0F;
                }
                else if (b >= 0xF0 && b <= 0xF4)
                {
                    extra = 3;
                    min = 0x10000;
                    codePoint = b & 0x07;
                }
                else
                {
                    // 0x80-0xBF stray continuation, 0xC0/0xC1 overlong leads, 0xF5-0xFF out of range.
                    return false;
                }

                if (i + extra >= sample)
                {
                    // The sequence runs past the sample. If the scan window cut it off, it is merely
                    // incomplete (ignore it). If this is the true end of the file, the file ends with a
                    // malformed (truncated) sequence, which makes the content invalid.
                    return truncated && multiByteSequences >= Utf8MinMultiByteSequences;
                }

                for (var k = 1; k <= extra; k++)
                {
                    var c = data[i + k];
                    if (c < 0x80 || c > 0xBF)
                    {
                        return false;
                    }

                    codePoint = (codePoint << 6) | (c & 0x3F);
                }

                // Reject overlong encodings, UTF-16 surrogates, and code points above U+10FFFF.
                if (codePoint < min || (codePoint >= 0xD800 && codePoint <= 0xDFFF) || codePoint > 0x10FFFF)
                {
                    return false;
                }

                multiByteSequences++;
                i += extra + 1;
            }

            return multiByteSequences >= Utf8MinMultiByteSequences;
        }

        // Binary verdict: a bare NUL byte (the primary, near-certain signal) or a high proportion of
        // non-text control bytes (catches NUL-free binaries). Each check is gated by its own bit.
        private static unsafe bool LooksBinary(byte* data, int sample, EncodingDetectionOptions options)
        {
            if (options.Has(EncodingDetectionSteps.BinaryNul) && HasNul(data, sample))
            {
                return true;
            }

            if (options.Has(EncodingDetectionSteps.BinaryControlRatio) && HasHighControlByteRatio(data, sample))
            {
                return true;
            }

            return false;
        }

        private static unsafe bool HasNul(byte* data, int sample)
        {
            for (var i = 0; i < sample; i++)
            {
                if (data[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static unsafe bool HasHighControlByteRatio(byte* data, int sample)
        {
            if (sample == 0)
            {
                return false;
            }

            var control = 0;
            for (var i = 0; i < sample; i++)
            {
                if (IsNonTextControl(data[i]))
                {
                    control++;
                }
            }

            return control > sample * BinaryControlByteFraction;
        }

        // Control bytes that do not appear in normal text: C0 controls except the common text
        // whitespace (tab 0x09, LF 0x0A, FF 0x0C, CR 0x0D).
        private static bool IsNonTextControl(byte b) =>
            (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0C && b != 0x0D);
    }
}
