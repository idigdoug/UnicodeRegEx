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

            var byLong = new Dictionary<string, (Setting Setting, CommandLineBinding Binding)>(StringComparer.OrdinalIgnoreCase);
            var byShort = new Dictionary<char, (Setting Setting, CommandLineBinding Binding)>();
            foreach (var option in settingGroup.Settings)
            {
                foreach (var binding in option.CommandLineBindings)
                {
                    if (binding.LongName is string longName)
                    {
                        byLong[longName] = (option, binding);
                    }

                    if (binding.ShortName is char shortName)
                    {
                        byShort[shortName] = (option, binding);
                    }
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

                Setting option;
                CommandLineBinding binding;
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

                    if (!byLong.TryGetValue(name, out var entry))
                    {
                        errors.Add($"unknown option --{name}");
                        continue;
                    }

                    option = entry.Setting;
                    binding = entry.Binding;
                }
                else
                {
                    var shortName = arg[1];
                    inlineValue = arg.Length > 2 ? arg.Substring(2) : null;
                    if (!byShort.TryGetValue(shortName, out var entry))
                    {
                        errors.Add($"unknown option -{shortName}");
                        continue;
                    }

                    option = entry.Setting;
                    binding = entry.Binding;
                }

                Apply(option, binding, inlineValue, args, ref i, errors);
            }

            return new CommandLineParseResult(false, positionals);
        }

        private static void Apply(Setting option, CommandLineBinding binding, string? inlineValue, string[] args, ref int i, List<string> errors)
        {
            string? value;
            if (binding.ImpliedValue != null)
            {
                // A valueless, value-implying token (e.g. a choice flag like -E): the value is fixed by
                // the binding; do not consume the next argument or an inline value.
                value = binding.ImpliedValue;
            }
            else
            {
                value = inlineValue;
                if (option.TakesValue && value == null)
                {
                    if (i + 1 < args.Length)
                    {
                        value = args[++i];
                    }
                    else
                    {
                        // Report the alias the user actually typed (a setting may have several bindings, e.g.
                        // a filter list bound to both --include and --exclude); fall back to the setting's own
                        // name only for a short-only binding.
                        errors.Add($"--{binding.LongName ?? option.LongName} requires a value");
                        return;
                    }
                }
            }

            try
            {
                option.Apply(value, binding);
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

            foreach (var section in settingGroup.GroupedSettings)
            {
                sb.AppendLine();
                sb.AppendLine($"{section.Title}:");
                foreach (var option in section.Settings)
                {
                    AppendOption(sb, option);
                }
            }

            sb.AppendLine();
            sb.AppendLine("General:");
            sb.Append(FormatOption('h', "help", null, "Show this help and exit.", null));
            return sb.ToString();
        }

        private static void AppendOption(StringBuilder sb, Setting option)
        {
            if (option is IChoiceSetting choiceSetting)
            {
                foreach (var choice in choiceSetting.Choices)
                {
                    var isDefault = ReferenceEquals(choice, choiceSetting.DefaultChoice);
                    var note = isDefault ? $"{choice.Description} [default]" : choice.Description;
                    sb.AppendLine(FormatOption(choice.ShortName, choice.LongName, null, note, null));
                }
            }
            else
            {
                // A setting may expose several aliases (e.g. a filter list bound to both --include and
                // --exclude); render one line per binding so every alias is discoverable. The default is
                // shown only on the first line to avoid repetition.
                var first = true;
                foreach (var binding in option.CommandLineBindings)
                {
                    sb.AppendLine(FormatOption(
                        binding.ShortName,
                        binding.LongName,
                        option.ValueName,
                        option.Description,
                        first ? option.DefaultText : null));
                    first = false;
                }
            }
        }

        private static string FormatOption(char? shortName, string? longName, string? valueName, string description, string? defaultText)
        {
            var left = shortName is char c
                ? (longName != null ? $"-{c}, --{longName}" : $"-{c}")
                : $"    --{longName}";
            if (valueName != null)
            {
                left += $" <{valueName}>";
            }

            var right = defaultText != null ? $"{description} [default: {defaultText}]" : description;
            return $"  {left.PadRight(LeftColumnWidth)}  {right}";
        }
    }
}
