namespace UnicodeRegEx.CommandLine
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public enum ParseStatus
    {
        Success,
        HelpRequested,
        Error,
    }

    public sealed class ParseResult
    {
        public ParseResult(ParseStatus status, List<string> positionals, List<string> errors)
        {
            Status = status;
            Positionals = positionals;
            Errors = errors;
        }

        public ParseStatus Status { get; }

        /// <summary>Arguments that are not options (e.g. the pattern and paths).</summary>
        public List<string> Positionals { get; }

        public List<string> Errors { get; }
    }

    /// <summary>
    /// A small command-line parser over an <see cref="OptionSet"/>. Supports --long, --long value,
    /// --long=value, -s, -s value, -svalue, "--" to end option processing, and -h/--help/-?.
    /// Options may be interleaved with positionals. Application-agnostic.
    /// </summary>
    public static class CommandLineParser
    {
        public static ParseResult Parse(OptionSet optionSet, string[] args)
        {
            var positionals = new List<string>();
            var errors = new List<string>();

            var byLong = new Dictionary<string, CommandLineOption>(StringComparer.Ordinal);
            var byShort = new Dictionary<char, CommandLineOption>();
            foreach (var option in optionSet.Options)
            {
                byLong[option.LongName] = option;
                if (option.ShortName is char shortName)
                {
                    byShort[shortName] = option;
                }
            }

            var endOfOptions = false;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (endOfOptions || arg.Length == 0 || arg[0] != '-' || arg == "-")
                {
                    positionals.Add(arg);
                    continue;
                }

                if (arg == "--")
                {
                    endOfOptions = true;
                    continue;
                }

                if (arg == "--help" || arg == "-h" || arg == "-?")
                {
                    return new ParseResult(ParseStatus.HelpRequested, positionals, errors);
                }

                CommandLineOption? option;
                string? inlineValue;
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = arg.Substring(2);
                    var eq = name.IndexOf('=');
                    if (eq >= 0)
                    {
                        inlineValue = name.Substring(eq + 1);
                        name = name.Substring(0, eq);
                    }
                    else
                    {
                        inlineValue = null;
                    }

                    if (!byLong.TryGetValue(name, out option))
                    {
                        errors.Add($"unknown option --{name}");
                        continue;
                    }
                }
                else
                {
                    var shortName = arg[1];
                    inlineValue = arg.Length > 2 ? arg.Substring(2) : null;
                    if (!byShort.TryGetValue(shortName, out option))
                    {
                        errors.Add($"unknown option -{shortName}");
                        continue;
                    }
                }

                Apply(option, inlineValue, args, ref i, errors);
            }

            var status = errors.Count > 0 ? ParseStatus.Error : ParseStatus.Success;
            return new ParseResult(status, positionals, errors);
        }

        private static void Apply(CommandLineOption option, string? inlineValue, string[] args, ref int i, List<string> errors)
        {
            var value = inlineValue;
            if (option.TakesValue && value == null)
            {
                if (i + 1 < args.Length)
                {
                    value = args[++i];
                }
                else
                {
                    errors.Add($"--{option.LongName} requires a value");
                    return;
                }
            }

            try
            {
                option.Apply(value);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }
    }

    /// <summary>Renders --help text by enumerating an <see cref="OptionSet"/>.</summary>
    public static class HelpFormatter
    {
        private const int LeftColumnWidth = 28;

        public static string Format(string usage, OptionSet optionSet)
        {
            var sb = new StringBuilder();
            sb.AppendLine(usage);
            sb.AppendLine();
            sb.AppendLine("Options:");

            foreach (var option in optionSet.Options)
            {
                sb.AppendLine(FormatOption(option.ShortName, option.LongName, option.ValueName, option.Description, option.DefaultText));
            }

            sb.Append(FormatOption('h', "help", null, "Show this help and exit.", null));
            return sb.ToString();
        }

        private static string FormatOption(char? shortName, string longName, string? valueName, string description, string? defaultText)
        {
            var left = shortName is char c ? $"-{c}, --{longName}" : $"    --{longName}";
            if (valueName != null)
            {
                left += $" <{valueName}>";
            }

            var right = defaultText != null ? $"{description} [default: {defaultText}]" : description;
            return $"  {left.PadRight(LeftColumnWidth)}  {right}";
        }
    }
}
