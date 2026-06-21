namespace UnicodeRegEx.Tools.Settings
{
    using System;

    /// <summary>
    /// Classifies a setting by persistence semantics. Required on every <see cref="Setting"/> so the
    /// distinction is a conscious choice, never an accident.
    /// </summary>
    public enum SettingRole
    {
        /// <summary>
        /// A durable user choice (e.g. match options, default code page). Persist-eligible: a
        /// front-end may save it and reload it on next launch. Command-line values for a preference
        /// are an ephemeral, per-invocation override and are NOT written back to the persisted store.
        /// </summary>
        Preference,

        /// <summary>
        /// Transient "what I'm doing right now" / launch intent (e.g. the replace template, apply
        /// switch). Never persisted; this is the role command-line launch arguments populate.
        /// </summary>
        WorkingState,
    }

    /// <summary>
    /// Base description of a single program setting. Each setting owns its parsed value, so a
    /// <see cref="SettingGroup"/> is the single source of truth for defaults, parsing, and generated
    /// --help text. Kept application-agnostic so it can move to a shared core later.
    /// </summary>
    public abstract class Setting
    {
        protected Setting(SettingRole role, string longName, char? shortName, string? valueName, string description)
        {
            Role = role;
            LongName = longName;
            ShortName = shortName;
            ValueName = valueName;
            Description = description;
        }

        /// <summary>Persistence classification (preference vs transient working state).</summary>
        public SettingRole Role { get; }

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
    public sealed class FlagSetting : Setting
    {
        public FlagSetting(SettingRole role, string longName, char? shortName, string description)
            : base(role, longName, shortName, null, description)
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

    /// <summary>A setting that consumes a value parsed into <typeparamref name="T"/>.</summary>
    public sealed class ValueSetting<T> : Setting
    {
        private readonly Func<string, T> parse;
        private readonly Func<T, string> describe;
        private readonly T defaultValue;

        public ValueSetting(
            SettingRole role,
            string longName,
            char? shortName,
            string valueName,
            string description,
            T defaultValue,
            Func<string, T> parse,
            Func<T, string>? describe = null)
            : base(role, longName, shortName, valueName, description)
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
}
