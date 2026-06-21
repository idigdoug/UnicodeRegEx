namespace UnicodeRegEx
{
    using System;

    /// <summary>Flags selecting the regex syntax flavor and options used when compiling a pattern.</summary>
    [Flags]
    public enum RegExSyntaxFlags
    {
        /// <summary>Perl/ECMAScript syntax group (the default). Mutually exclusive with the other syntax groups.</summary>
        PerlSyntaxGroup = 0,

        /// <summary>POSIX basic syntax group. Mutually exclusive with the other syntax groups.</summary>
        BasicSyntaxGroup = 1,

        /// <summary>Literal syntax group: the whole pattern is treated as a literal string. Mutually exclusive with the other syntax groups.</summary>
        Literal = 2,

        /// <summary>Bit mask covering the syntax group (<see cref="PerlSyntaxGroup"/>, <see cref="BasicSyntaxGroup"/>, <see cref="Literal"/>).</summary>
        SyntaxGroupMask = 3,

        /// <summary>Match without regard to case.</summary>
        ICase = 1048576,

        /// <summary>Don't mark sub-expressions; only the overall match is reported.</summary>
        NoSubs = 4194304,

        /// <summary>Optimize matching speed (no effect in this implementation).</summary>
        Optimize = 0,

        /// <summary>Use locale-specific collation in character ranges such as [a-b].</summary>
        Collate = 2097152,

        /// <summary>Normal (Perl/ECMAScript) syntax. Equivalent to <see cref="ECMAScript"/>.</summary>
        Normal = 0,

        /// <summary>Perl syntax. Equivalent to <see cref="ECMAScript"/>.</summary>
        Perl = 0,

        /// <summary>ECMAScript (Perl-compatible) syntax: the default flavor.</summary>
        ECMAScript = 0,

        /// <summary>POSIX basic regular expression syntax (as used by sed).</summary>
        Basic = 2162689,

        /// <summary>POSIX extended regular expression syntax.</summary>
        Extended = 2163456,

        /// <summary>POSIX awk regular expression syntax.</summary>
        Awk = 2097920,

        /// <summary>POSIX grep syntax: basic syntax in which newline also acts as an alternation operator.</summary>
        Grep = 2293761,

        /// <summary>POSIX egrep syntax: extended syntax in which newline also acts as an alternation operator.</summary>
        Egrep = 2294528
    }

    /// <summary>Flags controlling how a match or search is performed.</summary>
    [Flags]
    public enum RegExMatchFlags
    {
        /// <summary>Default matching using the normal ECMAScript rules.</summary>
        Default = 0,

        /// <summary>"^" should not match at the start of the input (the start is not treated as the beginning of a line).</summary>
        NotBol = 1,

        /// <summary>"$" should not match at the end of the input (the end is not treated as the end of a line).</summary>
        NotEol = 2,

        /// <summary>"\b" and "\&lt;" should not match at the start of the input (the start is not treated as a word boundary).</summary>
        NotBow = 0x10,

        /// <summary>"\b" and "\&gt;" should not match at the end of the input (the end is not treated as a word boundary).</summary>
        NotEow = 0x20,

        /// <summary>Any match is acceptable: still leftmost, but not necessarily the best match at that position. Faster when you only care whether there is a match.</summary>
        Any = 0x400,

        /// <summary>The expression may not match an empty sequence.</summary>
        NotNull = 0x800,

        /// <summary>The match must begin at the start of the search range (anchored).</summary>
        Continuous = 0x1000
    }

    /// <summary>Flags controlling how a replacement format template is interpreted and applied.</summary>
    [Flags]
    public enum RegExFormatFlags
    {
        /// <summary>Perl/ECMAScript replacement rules (the default).</summary>
        Perl = 0,

        /// <summary>Unix sed replacement rules.</summary>
        Sed = 0x1000000,

        /// <summary>Enable all Boost replacement syntax extensions, including conditional (?n:true:false) replacements.</summary>
        BoostExtensions = 0x2000000,

        /// <summary>Don't copy the unmatched portions of the input to the output.</summary>
        NoCopy = 0x4000000,

        /// <summary>Replace only the first match.</summary>
        FirstOnly = 0x8000000
    }

    /// <summary>Error codes reported when a pattern fails to compile.</summary>
    public enum RegExErrorCode
    {
        /// <summary>No error.</summary>
        Ok,

        /// <summary>No match (not used during pattern compilation).</summary>
        NoMatch,

        /// <summary>Other unspecified error.</summary>
        BadPattern,

        /// <summary>An invalid collating element was specified in a [[.name.]] block.</summary>
        Collate,

        /// <summary>An invalid character class name was specified in a [[:name:]] block.</summary>
        Ctype,

        /// <summary>An invalid or trailing escape was encountered.</summary>
        Escape,

        /// <summary>A back-reference to a non-existent marked sub-expression was encountered.</summary>
        Backref,

        /// <summary>An invalid character set [...] was encountered.</summary>
        Brack,

        /// <summary>Mismatched '(' and ')'.</summary>
        Paren,

        /// <summary>Mismatched '{' and '}'.</summary>
        Brace,

        /// <summary>Invalid contents of a {...} block.</summary>
        BadBrace,

        /// <summary>A character range was invalid, for example [d-a].</summary>
        Range,

        /// <summary>Out of memory.</summary>
        Space,

        /// <summary>An attempt to repeat something that can not be repeated, for example a*+.</summary>
        BadRepeat,

        /// <summary>Unexpected end of expression (not used).</summary>
        End,

        /// <summary>The expression was too large.</summary>
        Size,

        /// <summary>Unbalanced ')' (not used).</summary>
        RightParen,

        /// <summary>An empty expression was encountered.</summary>
        Empty,

        /// <summary>The expression became too complex to handle.</summary>
        Complexity,

        /// <summary>Out of program stack space.</summary>
        Stack,

        /// <summary>An invalid Perl-specific extension (?...) was encountered.</summary>
        PerlExtension,

        /// <summary>An unknown error.</summary>
        Unknown,

        /// <summary>A memory allocation failed.</summary>
        BadAlloc
    }

    /// <summary>Text codePage (code page) of an input or output byte buffer.</summary>
    public static class RegExCodePage
    {
        /// <summary>
        /// The system default ANSI code page (CP_ACP, value 0). A placeholder that must be resolved
        /// to a concrete code page (e.g. via GetACP) before use; the engine does not accept it.
        /// </summary>
        public const int SystemDefault = 0;

        /// <summary>UTF-16 little-endian (Windows code page 1200).</summary>
        public const int Utf16LE = 1200;

        /// <summary>UTF-16 big-endian (Windows code page 1201).</summary>
        public const int Utf16BE = 1201;

        /// <summary>ISO-8859-1 / Latin-1 (Windows code page 28591).</summary>
        public const int Latin1 = 28591;

        /// <summary>UTF-8 (Windows code page 65001).</summary>
        public const int Utf8 = 65001;
    }

    /// <summary>Position of a match or segment enumeration.</summary>
    public enum RegExEnumerationState
    {
        /// <summary>Enumeration has not started; positioned before the first match or segment.</summary>
        NotStarted,

        /// <summary>Positioned on a valid match or segment.</summary>
        Enumerating,

        /// <summary>Enumeration is complete; positioned after the last match or segment.</summary>
        Finished
    }

    /// <summary>Cancellation state of a file stream.</summary>
    public enum RegExStreamCancelStatus
    {
        /// <summary>No cancel has been requested.</summary>
        Running,

        /// <summary>Cancel requested; an I/O operation may still be in progress.</summary>
        Cancelling,

        /// <summary>Cancel completed; no I/O is in progress and all future I/O fails with E_ABORT.</summary>
        Cancelled
    }

    /// <summary>Creation disposition and modifier flags for opening a file stream.</summary>
    [Flags]
    public enum RegExFileStreamFlags
    {
        /// <summary>Open an existing file for read/write.</summary>
        OpenExisting = 0,

        /// <summary>Create a new file, failing if it already exists (CREATE_NEW).</summary>
        CreateNew = 1,

        /// <summary>Truncate the file if it exists, otherwise create it (CREATE_ALWAYS).</summary>
        CreateAlways = 2,

        /// <summary>Open the file if it exists, otherwise create it (OPEN_ALWAYS).</summary>
        OpenOrCreate = 3,

        /// <summary>Delete the file when the last handle is closed (FILE_FLAG_DELETE_ON_CLOSE). MoveTo clears this before renaming.</summary>
        DeleteOnClose = 256,

        /// <summary>Hint that access will be sequential (FILE_FLAG_SEQUENTIAL_SCAN).</summary>
        Sequential = 512,

        /// <summary>Bypass write caching (FILE_FLAG_WRITE_THROUGH).</summary>
        WriteThrough = 1024
    }

    /// <summary>Flags controlling a file move/rename.</summary>
    [Flags]
    public enum RegExFileMoveFlags
    {
        /// <summary>Default move behavior; fail if the destination already exists.</summary>
        Default,

        /// <summary>Allow the move to replace an existing file at the destination.</summary>
        ReplaceExisting
    }
}
