namespace UnicodeRegEx
{
    using System;

    [Flags]
    public enum RegExSyntaxFlags
    {
        PerlSyntaxGroup = 0,
        BasicSyntaxGroup = 1,
        Literal = 2,
        SyntaxGroupMask = 3,
        ICase = 1048576,
        NoSubs = 4194304,
        Optimize = 0,
        Collate = 2097152,
        Normal = 0,
        Perl = 0,
        ECMAScript = 0,
        Basic = 2162689,
        Extended = 2163456,
        Awk = 2097920,
        Grep = 2293761,
        Egrep = 2294528
    }

    [Flags]
    public enum RegExMatchFlags
    {
        Default = 0,
        NotBol = 1,
        NotEol = 2,
        NotBow = 0x10,
        NotEow = 0x20,
        Any = 0x400,
        NotNull = 0x800,
        Continuous = 0x1000
    }

    [Flags]
    public enum RegExFormatFlags
    {
        Perl = 0,
        Sed = 0x1000000,
        BoostExtensions = 0x2000000,
        NoCopy = 0x4000000,
        FirstOnly = 0x8000000
    }

    public enum RegExErrorCode
    {
        Ok,
        NoMatch,
        BadPattern,
        Collate,
        Ctype,
        Escape,
        Backref,
        Brack,
        Paren,
        Brace,
        BadBrace,
        Range,
        Space,
        BadRepeat,
        End,
        Size,
        RightParen,
        Empty,
        Complexity,
        Stack,
        PerlExtension,
        Unknown,
        BadAlloc
    }

    public enum RegExEncoding
    {
        None = 0,
        Utf16LE = 1200,
        Utf16BE = 1201,
        Latin1 = 28591,
        Utf8 = 65001
    }

    public enum RegExEnumerationState
    {
        NotStarted,
        Enumerating,
        Finished
    }

    public enum RegExStreamCancelStatus
    {
        Running,
        Cancelling,
        Cancelled
    }

    [Flags]
    public enum RegExFileStreamFlags
    {
        OpenExisting = 0,
        CreateNew = 1,
        CreateAlways = 2,
        OpenOrCreate = 3,
        DeleteOnClose = 256,
        Sequential = 512,
        WriteThrough = 1024
    }

    [Flags]
    public enum RegExFileMoveFlags
    {
        Default,
        ReplaceExisting
    }
}
