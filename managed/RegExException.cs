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

        /// <summary>The pattern that failed to compile.</summary>
        public string Pattern => pattern;

        /// <summary>The syntax flags the pattern was compiled with.</summary>
        public RegExSyntaxFlags SyntaxFlags => syntaxFlags;

        /// <summary>The error code describing why compilation failed.</summary>
        public RegExErrorCode ErrorCode => errorCode;

        /// <summary>The error message from the native regex engine.</summary>
        public string NativeMessage => nativeMessage;

        /// <summary>Creates a <see cref="RegExException"/> describing a pattern compilation failure.</summary>
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
