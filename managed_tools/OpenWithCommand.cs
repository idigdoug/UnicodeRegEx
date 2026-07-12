namespace UnicodeRegEx.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;

    /// <summary>
    /// Expands an <see cref="OpenWithTool"/> command-line template for a specific hit and launches it.
    /// The template supports these tokens (case-sensitive):
    /// <list type="bullet">
    /// <item><description><c>$F</c> — the file path.</description></item>
    /// <item><description><c>$L</c> — the 1-based line number.</description></item>
    /// <item><description><c>$C</c> — the 1-based column.</description></item>
    /// <item><description><c>$$</c> — a literal <c>$</c>.</description></item>
    /// </list>
    /// A <c>$</c> followed by anything else is left as-is. After substitution the command line is split
    /// quote-aware (a <c>"..."</c> run is one argument, so a path with spaces stays intact) into the
    /// executable and its arguments, then run with <see cref="Process.Start(ProcessStartInfo)"/>.
    /// </summary>
    public static class OpenWithCommand
    {
        /// <summary>The tools a fresh install starts with, so the context menu is never empty.</summary>
        public static IReadOnlyList<OpenWithTool> DefaultTools() => new[]
        {
            new OpenWithTool("Notepad", "notepad.exe \"$F\""),
        };

        /// <summary>
        /// Substitutes the <c>$F</c>/<c>$L</c>/<c>$C</c>/<c>$$</c> tokens in <paramref name="template"/> in a
        /// single left-to-right pass (so a substituted <c>$</c> can never re-trigger a token).
        /// </summary>
        public static string Substitute(string template, string file, ulong line, ulong column)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var result = new StringBuilder(template.Length + 16);
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if (c != '$' || i + 1 >= template.Length)
                {
                    result.Append(c);
                    continue;
                }

                var next = template[i + 1];
                switch (next)
                {
                    case 'F':
                        result.Append(file);
                        i++;
                        break;
                    case 'L':
                        result.Append(line.ToString());
                        i++;
                        break;
                    case 'C':
                        result.Append(column.ToString());
                        i++;
                        break;
                    case '$':
                        result.Append('$');
                        i++;
                        break;
                    default:
                        // Unknown token: keep the '$' literally and let the next char be handled normally.
                        result.Append('$');
                        break;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Splits a command line quote-aware into its tokens: whitespace separates tokens except inside a
        /// <c>"..."</c> run, and a <c>""</c> is an empty quoted token. Returns an empty list for a blank line.
        /// The first token is the executable; the rest are arguments.
        /// </summary>
        public static List<string> SplitArguments(string commandLine)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(commandLine))
            {
                return tokens;
            }

            var current = new StringBuilder();
            var inQuotes = false;
            var hasToken = false;

            foreach (var c in commandLine)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    hasToken = true; // a quote starts a token even if it is empty ("")
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (hasToken)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }

            if (hasToken)
            {
                tokens.Add(current.ToString());
            }

            return tokens;
        }

        /// <summary>
        /// Substitutes <paramref name="tool"/>'s command line for the given hit and launches it. Returns the
        /// started <see cref="Process"/> (which the caller may ignore). Throws if the command line has no
        /// executable token or the process fails to start.
        /// </summary>
        public static Process Launch(OpenWithTool tool, string file, ulong line, ulong column)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            var expanded = Substitute(tool.CommandLine, file, line, column);
            var tokens = SplitArguments(expanded);
            if (tokens.Count == 0)
            {
                throw new InvalidOperationException("The command line is empty.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = tokens[0],
                UseShellExecute = false,
            };

            var arguments = new StringBuilder();
            for (var i = 1; i < tokens.Count; i++)
            {
                if (arguments.Length > 0)
                {
                    arguments.Append(' ');
                }

                AppendArgument(arguments, tokens[i]);
            }

            startInfo.Arguments = arguments.ToString();
            return Process.Start(startInfo)!;
        }

        // Appends one argument to a Windows command line, quoting/escaping per the CommandLineToArgvW rules
        // so a round-trip through the child process recovers the exact token. (ProcessStartInfo.ArgumentList,
        // which would do this for us, is not available on this target framework.)
        private static void AppendArgument(StringBuilder builder, string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(QuoteRequiredChars) < 0)
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');
            for (var i = 0; i < argument.Length; i++)
            {
                var backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    i++;
                    backslashes++;
                }

                if (i == argument.Length)
                {
                    // Escape all trailing backslashes so they do not escape the closing quote.
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (argument[i] == '"')
                {
                    // Escape the backslashes preceding the quote, then the quote itself.
                    builder.Append('\\', (backslashes * 2) + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(argument[i]);
                }
            }

            builder.Append('"');
        }

        private static readonly char[] QuoteRequiredChars = { ' ', '\t', '\n', '\v', '"' };
    }
}
