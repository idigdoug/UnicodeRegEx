namespace UnicodeRegEx
{
    using System;

    /// <summary>
    /// Exception thrown by RegEx.Create when pattern parsing fails.
    /// </summary>
    public class RegExException : Exception
    {
        private readonly string pattern;
        private readonly RegExSyntaxFlags syntaxFlags;
        private readonly RegExErrorCode errorCode;
        private readonly string nativeMessage;

        public string Pattern => pattern;
        public RegExSyntaxFlags SyntaxFlags => syntaxFlags;
        public RegExErrorCode ErrorCode => errorCode;
        public string NativeMessage => nativeMessage;

        public RegExException(string pattern, RegExSyntaxFlags syntaxFlags, RegExErrorCode errorCode, string? nativeMessage)
            : base(FormatMessage(pattern, errorCode, nativeMessage))
        {
            this.pattern = pattern;
            this.syntaxFlags = syntaxFlags;
            this.errorCode = errorCode;
            this.nativeMessage = nativeMessage ?? errorCode.ToString();
        }

        private static string FormatMessage(string pattern, RegExErrorCode errorCode, string? nativeMessage)
        {
            if (nativeMessage == null)
            {
                return $"Failed to compile regex ({errorCode}): {pattern}";
            }
            else
            {
                return $"Failed to compile regex ({errorCode}, {nativeMessage}): {pattern}";
            }
        }
    }
}
