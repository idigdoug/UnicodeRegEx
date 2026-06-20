namespace UnicodeRegEx.CommandLine
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    /// <summary>
    /// Base description of a single command-line option. Each option owns its parsed value, so an
    /// <see cref="OptionSet"/> is the single source of truth for defaults, parsing, and generated
    /// --help text. Kept application-agnostic so it can move to a shared core later.
    /// </summary>
    public abstract class CommandLineOption
    {
        protected CommandLineOption(string longName, char? shortName, string? valueName, string description)
        {
            LongName = longName;
            ShortName = shortName;
            ValueName = valueName;
            Description = description;
        }

        /// <summary>The --long-name, without the leading dashes.</summary>
        public string LongName { get; }

        /// <summary>The optional -s short alias.</summary>
        public char? ShortName { get; }

        /// <summary>Placeholder shown in help for the value (e.g. "codepage"); null for flags.</summary>
        public string? ValueName { get; }

        /// <summary>One-line help description.</summary>
        public string Description { get; }

        /// <summary>True if the option consumes a value; false for boolean flags.</summary>
        public bool TakesValue => ValueName != null;

        /// <summary>Human-readable default value, shown in help.</summary>
        public abstract string DefaultText { get; }

        /// <summary>
        /// Applies a parsed override. <paramref name="value"/> is null for flags. Throws
        /// <see cref="FormatException"/> if the value cannot be parsed.
        /// </summary>
        public abstract void Apply(string? value);
    }

    /// <summary>A boolean flag: absent leaves the default (false), present sets it true.</summary>
    public sealed class FlagOption : CommandLineOption
    {
        public FlagOption(string longName, char? shortName, string description)
            : base(longName, shortName, null, description)
        {
        }

        public bool Value { get; private set; }

        public override string DefaultText => "off";

        // A bare flag (null value, e.g. command-line "-i") sets true; an explicit value
        // (e.g. config "false" or "--ignore-case=false") is parsed as a boolean.
        public override void Apply(string? value) => Value = value == null || ParseBool(value);

        private bool ParseBool(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "on":
                case "1":
                    return true;
                case "false":
                case "no":
                case "off":
                case "0":
                    return false;
                default:
                    throw new FormatException($"'{value}' is not a valid true/false value for {LongName}");
            }
        }
    }

    /// <summary>An option that consumes a value parsed into <typeparamref name="T"/>.</summary>
    public sealed class ValueOption<T> : CommandLineOption
    {
        private readonly Func<string, T> parse;
        private readonly Func<T, string> describe;
        private readonly T defaultValue;

        public ValueOption(
            string longName,
            char? shortName,
            string valueName,
            string description,
            T defaultValue,
            Func<string, T> parse,
            Func<T, string>? describe = null)
            : base(longName, shortName, valueName, description)
        {
            this.parse = parse ?? throw new ArgumentNullException(nameof(parse));
            this.describe = describe ?? DescribeDefault;
            this.defaultValue = defaultValue;
            Value = defaultValue;
        }

        public T Value { get; private set; }

        public override string DefaultText => describe(defaultValue);

        public override void Apply(string? value)
        {
            if (value == null)
            {
                throw new FormatException($"--{LongName} requires a value");
            }

            Value = parse(value);
        }

        private static string DescribeDefault(T value) =>
            value is null ? "(none)" : value.ToString() ?? "(none)";
    }

    /// <summary>
    /// A collection of options. Subclasses declare options as public fields; they are discovered
    /// automatically (in declaration order) so adding a setting is a single edit, and every option
    /// is available for both parsing and --help.
    /// </summary>
    public abstract class OptionSet
    {
        private CommandLineOption[]? options;

        public IReadOnlyList<CommandLineOption> Options => options ??= Collect();

        /// <summary>
        /// Applies a set of name-&gt;value settings (from a config file, environment, etc.) onto the
        /// options in this set, matching by <see cref="CommandLineOption.LongName"/>. Run this before
        /// the command line so command-line arguments take precedence. Unknown names and unparseable
        /// values are collected in <paramref name="errors"/> (prefixed with
        /// <paramref name="sourceLabel"/>) rather than thrown.
        /// </summary>
        public void ApplyOverlay(
            IEnumerable<KeyValuePair<string, string?>> settings,
            string sourceLabel,
            List<string> errors)
        {
            var byName = new Dictionary<string, CommandLineOption>(StringComparer.Ordinal);
            foreach (var option in Options)
            {
                byName[option.LongName] = option;
            }

            foreach (var setting in settings)
            {
                if (!byName.TryGetValue(setting.Key, out var option))
                {
                    errors.Add($"{sourceLabel}: unknown setting '{setting.Key}'");
                    continue;
                }

                try
                {
                    option.Apply(setting.Value);
                }
                catch (Exception ex)
                {
                    errors.Add($"{sourceLabel}: {ex.Message}");
                }
            }
        }

        private CommandLineOption[] Collect()
        {
            var result = new List<CommandLineOption>();
            foreach (var field in GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetValue(this) is CommandLineOption option)
                {
                    result.Add(option);
                }
            }

            return result.ToArray();
        }
    }
}
