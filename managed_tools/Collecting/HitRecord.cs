namespace UnicodeRegEx.Tools.Collecting
{
    using UnicodeRegEx;
    using UnicodeRegEx.Tools.Engine;

    /// <summary>
    /// A storable snapshot of a single match, copied out of the (ref struct, call-lifetime) <see cref="SearchHit"/>
    /// during a search so it can outlive the run and be browsed later — the model a find/replace UI binds to.
    /// <para>
    /// The match and its surrounding context are kept as raw byte blobs; the corresponding strings are decoded
    /// on demand from <see cref="File"/>'s code page. <see cref="PreMatchBytes"/> and <see cref="PostMatchBytes"/>
    /// are a bounded window of the file bytes immediately before and after the match (clamped at the file's
    /// start/end, so they are shorter for a match near either edge). Besides display, the byte context also
    /// serves as a staleness guard when a later apply re-verifies that these exact bytes still surround the
    /// match offset.
    /// </para>
    /// </summary>
    public sealed class HitRecord
    {
        public HitRecord(
            SearchFile file,
            nuint matchFileOffset,
            nuint lineNumber,
            nuint columnNumber,
            byte[] preMatchBytes,
            byte[] matchBytes,
            byte[] postMatchBytes,
            byte[] replacementBytes)
        {
            File = file;
            MatchFileOffset = matchFileOffset;
            LineNumber = lineNumber;
            ColumnNumber = columnNumber;
            PreMatchBytes = preMatchBytes;
            MatchBytes = matchBytes;
            PostMatchBytes = postMatchBytes;
            ReplacementBytes = replacementBytes;
        }

        /// <summary>The file this hit is in (its path and code page).</summary>
        public SearchFile File { get; }

        /// <summary>The byte offset of the match within the file. Always a valid in-memory offset (the whole file is mapped).</summary>
        public nuint MatchFileOffset { get; }

        /// <summary>The 1-based line number of the match, or 0 when the run did not track line numbers.</summary>
        public nuint LineNumber { get; }

        /// <summary>The 1-based column (in code units) of the match, or 0 when the run did not track line numbers.</summary>
        public nuint ColumnNumber { get; }

        /// <summary>File bytes immediately before the match (up to a bounded window; shorter near the file start).</summary>
        public byte[] PreMatchBytes { get; }

        /// <summary>The matched bytes.</summary>
        public byte[] MatchBytes { get; }

        /// <summary>File bytes immediately after the match (up to a bounded window; shorter near the file end).</summary>
        public byte[] PostMatchBytes { get; }

        /// <summary>The replacement bytes this match formats to under the run's template, in the file's code page.</summary>
        public byte[] ReplacementBytes { get; }

        /// <summary>The context before the match, decoded with the file's code page.</summary>
        public string PreMatchText => Decode(PreMatchBytes);

        /// <summary>The matched text, decoded with the file's code page.</summary>
        public string MatchText => Decode(MatchBytes);

        /// <summary>The context after the match, decoded with the file's code page.</summary>
        public string PostMatchText => Decode(PostMatchBytes);

        /// <summary>The replacement, decoded with the file's code page.</summary>
        public string ReplacementText => Decode(ReplacementBytes);

        private string Decode(byte[] bytes) => RegExEncoding.FromCodePage(File.CodePage).GetString(bytes);
    }
}
