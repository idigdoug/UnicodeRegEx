namespace UnicodeRegEx.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnicodeRegEx.Tools.Settings;

    public sealed class CommandLineParseResult
    {
        public CommandLineParseResult(bool helpRequested, List<string> positionals)
        {
            HelpRequested = helpRequested;
            Positionals = positionals;
        }

        public bool HelpRequested { get; }

        /// <summary>Arguments that are not options (e.g. the pattern and paths).</summary>
        public List<string> Positionals { get; }
    }

    /// <summary>
    /// A small command-line parser over a <see cref="SettingGroup"/>. Supports --long, --long value,
    /// --long=value, -s, -s value, -svalue, "--" to end option processing, and -h/--help/-?.
    /// Options may be interleaved with positionals. Application-agnostic. Errors (unknown options,
    /// bad values) are appended to the caller's <c>errors</c> list rather than thrown, matching
    /// <see cref="AppConfigSource.Apply"/> so a single list collects problems across all sources.
    /// </summary>
    public static class CommandLine
    {
        public static CommandLineParseResult Parse(string[] args, SettingGroup settingGroup, List<string> errors)
        {
            var positionals = new List<string>();

            var byLong = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);
            var byShort = new Dictionary<char, Setting>();
            foreach (var option in settingGroup.Settings)
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
                    return new CommandLineParseResult(true, positionals);
                }

                Setting? option;
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

            return new CommandLineParseResult(false, positionals);
        }

        private static void Apply(Setting option, string? inlineValue, string[] args, ref int i, List<string> errors)
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

    /// <summary>Renders --help text by enumerating a <see cref="SettingGroup"/>.</summary>
    public static class HelpFormatter
    {
        private const int LeftColumnWidth = 28;

        public static string Format(string usage, SettingGroup settingGroup)
        {
            var sb = new StringBuilder();
            sb.AppendLine(usage);
            sb.AppendLine();
            sb.AppendLine("Options:");

            foreach (var option in settingGroup.Settings)
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
