namespace UnicodeRegEx.Tools.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One way a <see cref="Setting"/> can be named on the command line. Most settings expose a single
    /// binding (their own long/short name); a <see cref="ChoiceSetting{T}"/> exposes one valueless
    /// binding per choice, each carrying the choice's <see cref="ImpliedValue"/> so selecting the flag
    /// applies that value (e.g. <c>-E</c> implies "extended"). Application-agnostic.
    /// </summary>
    public readonly struct CommandLineBinding
    {
        public CommandLineBinding(string? longName, char? shortName, string? impliedValue, object? tag = null)
        {
            LongName = longName;
            ShortName = shortName;
            ImpliedValue = impliedValue;
            Tag = tag;
        }

        /// <summary>The --long-name this binding responds to, without dashes; null if short-only.</summary>
        public string? LongName { get; }

        /// <summary>The -s short alias this binding responds to; null if long-only.</summary>
        public char? ShortName { get; }

        /// <summary>
        /// If non-null, this is a valueless token that applies this exact value to the owning setting
        /// (no separate value is consumed). If null, the binding follows the owning setting's normal
        /// value/flag rules.
        /// </summary>
        public string? ImpliedValue { get; }

        /// <summary>
        /// Opaque per-binding data the owning setting interprets, passed back to
        /// <see cref="Setting.Apply"/> so a setting with several bindings can tell which one fired without
        /// the framework knowing the domain meaning (e.g. a filter-list setting tags its --include binding
        /// vs its --exclude binding). Null for settings that need no such distinction.
        /// </summary>
        public object? Tag { get; }
    }

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

        /// <summary>
        /// The command-line tokens that select this setting. By default a single binding using the
        /// setting's own long/short name with no implied value; <see cref="ChoiceSetting{T}"/> overrides
        /// this to expose one valueless, value-implying binding per choice.
        /// </summary>
        public virtual IEnumerable<CommandLineBinding> CommandLineBindings =>
            new[] { new CommandLineBinding(LongName, ShortName, null) };

        /// <summary>Human-readable default value, shown in help.</summary>
        public abstract string DefaultText { get; }

        /// <summary>
        /// Applies a parsed override. <paramref name="value"/> is null for flags. <paramref name="binding"/>
        /// is the command-line binding that matched (or a synthesized default from an overlay source), so a
        /// multi-binding setting can consult <see cref="CommandLineBinding.Tag"/>; most settings ignore it.
        /// Throws <see cref="FormatException"/> if the value cannot be parsed.
        /// </summary>
        public abstract void Apply(string? value, CommandLineBinding binding);

        /// <summary>The synthesized binding used when applying from a source with no command-line binding (e.g. a config overlay): the setting's own long/short name, no implied value or tag.</summary>
        public CommandLineBinding DefaultBinding => new CommandLineBinding(LongName, ShortName, null);
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
        public override void Apply(string? value, CommandLineBinding binding) => Value = value == null || ParseBool(value);

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

        public override void Apply(string? value, CommandLineBinding binding)
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
    /// One option of a <see cref="ChoiceSetting{T}"/>, without its value type: its canonical name
    /// (config/persistence token and implied command-line value) and its own command-line flags
    /// (e.g. <c>-E</c>/<c>--extended-regexp</c>). The non-generic base lets help rendering enumerate
    /// choices without knowing the value type.
    /// </summary>
    public abstract class Choice
    {
        protected Choice(string name, char? shortName, string? longName, string description)
        {
            Name = name;
            ShortName = shortName;
            LongName = longName;
            Description = description;
        }

        /// <summary>Canonical name: the config/persistence value and the implied command-line value (e.g. "extended").</summary>
        public string Name { get; }

        /// <summary>This choice's -s short flag (e.g. 'E'); null if none.</summary>
        public char? ShortName { get; }

        /// <summary>This choice's --long flag without dashes (e.g. "extended-regexp"); null if none.</summary>
        public string? LongName { get; }

        /// <summary>One-line help description for this choice.</summary>
        public string Description { get; }
    }

    /// <summary>A <see cref="Choice"/> paired with the value selected when it is chosen.</summary>
    public sealed class Choice<T> : Choice
    {
        public Choice(T value, string name, char? shortName, string? longName, string description)
            : base(name, shortName, longName, description)
        {
            Value = value;
        }

        /// <summary>The value selected when this choice is chosen.</summary>
        public T Value { get; }
    }

    /// <summary>
    /// Non-generic view of a <see cref="ChoiceSetting{T}"/> for help rendering, so the formatter need
    /// not know the value type.
    /// </summary>
    public interface IChoiceSetting
    {
        /// <summary>The available choices, in declaration order.</summary>
        IReadOnlyList<Choice> Choices { get; }

        /// <summary>The choice selected when none is given.</summary>
        Choice DefaultChoice { get; }
    }

    /// <summary>
    /// A single-valued choice selected among several mutually-exclusive options. On the command line
    /// each choice is a valueless flag (e.g. <c>-E</c>, <c>--fixed-strings</c>); the last one given
    /// wins. In config (and persistence) it is the canonical key (<see cref="Setting.LongName"/>) with
    /// a choice name as the value (e.g. <c>syntax=extended</c>). A future GUI binds one control to
    /// <see cref="Value"/> and enumerates <see cref="Choices"/> — the same single value, several
    /// presentations. The canonical long name is not itself a command-line token.
    /// </summary>
    public sealed class ChoiceSetting<T> : Setting, IChoiceSetting
    {
        private readonly IReadOnlyList<Choice<T>> choices;
        private readonly Choice<T> defaultChoice;

        public ChoiceSetting(
            SettingRole role,
            string longName,
            string description,
            T defaultValue,
            IReadOnlyList<Choice<T>> choices)
            : base(role, longName, null, null, description)
        {
            if (choices == null || choices.Count == 0)
            {
                throw new ArgumentException("A choice setting needs at least one choice.", nameof(choices));
            }

            this.choices = choices;

            Choice<T>? found = null;
            foreach (var choice in choices)
            {
                if (EqualityComparer<T>.Default.Equals(choice.Value, defaultValue))
                {
                    found = choice;
                    break;
                }
            }

            defaultChoice = found ?? throw new ArgumentException("The default value is not among the choices.", nameof(defaultValue));
            Value = defaultChoice.Value;
        }

        /// <summary>The currently selected value.</summary>
        public T Value { get; private set; }

        /// <summary>The available choices, in declaration order (for help text and GUI binding).</summary>
        public IReadOnlyList<Choice<T>> Choices => choices;

        // Non-generic IChoiceSetting view (IReadOnlyList<out T> covariance makes the list assignable).
        IReadOnlyList<Choice> IChoiceSetting.Choices => choices;

        Choice IChoiceSetting.DefaultChoice => defaultChoice;

        // One valueless, value-implying binding per choice; the canonical long name is not a token.
        public override IEnumerable<CommandLineBinding> CommandLineBindings
        {
            get
            {
                foreach (var choice in choices)
                {
                    yield return new CommandLineBinding(choice.LongName, choice.ShortName, choice.Name);
                }
            }
        }

        public override string DefaultText => defaultChoice.Name;

        // Applies a choice by its canonical name (from config or a flag's implied value).
        public override void Apply(string? value, CommandLineBinding binding)
        {
            foreach (var choice in choices)
            {
                if (string.Equals(choice.Name, value, StringComparison.OrdinalIgnoreCase))
                {
                    Value = choice.Value;
                    return;
                }
            }

            throw new FormatException($"'{value}' is not a valid value for {LongName}");
        }
    }
}
