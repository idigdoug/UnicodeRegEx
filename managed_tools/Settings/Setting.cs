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
    /// Groups a setting into a titled section for presentation — help sections and GUI property-page
    /// groups. Required on every <see cref="Setting"/>. The enum order is the section order; the title
    /// comes from <see cref="SettingCategories.DisplayName"/>.
    /// </summary>
    public enum SettingCategory
    {
        /// <summary>How the pattern is matched (syntax flavor, case sensitivity, match options).</summary>
        Matching,

        /// <summary>What is written back (the replacement template and apply switch).</summary>
        Replacement,

        /// <summary>Which files are searched (globs, recursion, binary handling).</summary>
        Files,

        /// <summary>How file bytes are decoded (default code page, detection).</summary>
        Encoding,
    }

    /// <summary>Helpers for <see cref="SettingCategory"/>.</summary>
    public static class SettingCategories
    {
        /// <summary>The human-readable section title shown in help and on the property page.</summary>
        public static string DisplayName(SettingCategory category)
        {
            switch (category)
            {
                case SettingCategory.Matching: return "Matching";
                case SettingCategory.Replacement: return "Replacement";
                case SettingCategory.Files: return "Files";
                case SettingCategory.Encoding: return "Encoding";
                default: return category.ToString();
            }
        }
    }

    /// <summary>
    /// A hint for how a property-page dialog should present a <see cref="Setting"/>'s value. Deliberately
    /// small — a single dialog consumes it — so the dialog can pick a control without inspecting the value's
    /// runtime type (which is ambiguous, e.g. an integer code page vs a plain integer).
    /// </summary>
    public enum EditorKind
    {
        /// <summary>A boolean on/off control (a checkbox).</summary>
        Toggle,

        /// <summary>A pick-one control over a fixed set (a dropdown); see <see cref="ChoiceSetting{T}.Choices"/>.</summary>
        Choice,

        /// <summary>Free text.</summary>
        Text,

        /// <summary>An integer.</summary>
        Integer,

        /// <summary>An ordered list; not shown on the property page (edited elsewhere). See <see cref="Setting.GetValue"/>.</summary>
        List,
    }

    /// <summary>
    /// Base description of a single program setting. Each setting owns its parsed value, so a
    /// <see cref="SettingGroup"/> is the single source of truth for defaults, parsing, and generated
    /// --help text. Kept application-agnostic so it can move to a shared core later.
    /// </summary>
    public abstract class Setting
    {
        protected Setting(SettingRole role, SettingCategory category, string longName, char? shortName, string? valueName, string description)
        {
            Role = role;
            Category = category;
            LongName = longName;
            ShortName = shortName;
            ValueName = valueName;
            Description = description;
        }

        /// <summary>Persistence classification (preference vs transient working state).</summary>
        public SettingRole Role { get; }

        /// <summary>The section this setting is grouped under in help and on the property page.</summary>
        public SettingCategory Category { get; }

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

        // PROPERTY-PAGE SURFACE
        //
        // A small, typed, observable surface a single property-page dialog binds to. The dialog decides
        // which settings to show (it shows Preference settings); nothing here gates visibility. String-based
        // Apply/DefaultText above remain the CLI/config path and are unchanged.

        /// <summary>Raised whenever the setting's value changes (via <see cref="TrySetValue"/> or <see cref="Apply"/>).</summary>
        public event EventHandler? ValueChanged;

        /// <summary>How a property-page dialog should present this setting's value.</summary>
        public abstract EditorKind EditorKind { get; }

        /// <summary>The current value, boxed. See <see cref="TrySetValue"/> to change it.</summary>
        public abstract object? GetValue();

        /// <summary>
        /// Attempts to set the value from <paramref name="value"/> (the setting's own type, or a string that
        /// is parsed). Returns false and sets <paramref name="error"/> if the value is invalid, leaving the
        /// current value unchanged; otherwise assigns it (raising <see cref="ValueChanged"/> if it differs)
        /// and returns true. Does not throw for invalid input.
        /// </summary>
        public abstract bool TrySetValue(object? value, out string? error);

        /// <summary>The default value (what <see cref="Reset"/> restores), boxed.</summary>
        public abstract object? DefaultValue { get; }

        /// <summary>True when the current value equals <see cref="DefaultValue"/>.</summary>
        public bool IsDefault => Equals(GetValue(), DefaultValue);

        /// <summary>Restores the value to <see cref="DefaultValue"/>.</summary>
        public void Reset() => TrySetValue(DefaultValue, out _);

        /// <summary>Raises <see cref="ValueChanged"/>.</summary>
        protected void RaiseValueChanged() => ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A boolean flag: absent leaves the default (false), present sets it true.</summary>
    public sealed class FlagSetting : Setting
    {
        public FlagSetting(SettingRole role, SettingCategory category, string longName, char? shortName, string description)
            : base(role, category, longName, shortName, null, description)
        {
        }

        public bool Value { get; private set; }

        public override string DefaultText => "off";

        public override EditorKind EditorKind => EditorKind.Toggle;

        public override object? GetValue() => Value;

        public override object? DefaultValue => false;

        public override bool TrySetValue(object? value, out string? error)
        {
            bool parsed;
            switch (value)
            {
                case bool b:
                    parsed = b;
                    break;
                case string s:
                    try
                    {
                        parsed = ParseBool(s);
                    }
                    catch (FormatException ex)
                    {
                        error = ex.Message;
                        return false;
                    }

                    break;
                default:
                    error = $"'{value}' is not a valid true/false value for {LongName}";
                    return false;
            }

            error = null;
            SetValue(parsed);
            return true;
        }

        // A bare flag (null value, e.g. command-line "-i") sets true; an explicit value
        // (e.g. config "false" or "--ignore-case=false") is parsed as a boolean.
        public override void Apply(string? value, CommandLineBinding binding) => SetValue(value == null || ParseBool(value));

        private void SetValue(bool value)
        {
            if (Value == value)
            {
                return;
            }

            Value = value;
            RaiseValueChanged();
        }

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
        private readonly EditorKind editorKind;

        public ValueSetting(
            SettingRole role,
            SettingCategory category,
            string longName,
            char? shortName,
            string valueName,
            string description,
            T defaultValue,
            EditorKind editorKind,
            Func<string, T> parse,
            Func<T, string>? describe = null)
            : base(role, category, longName, shortName, valueName, description)
        {
            this.parse = parse ?? throw new ArgumentNullException(nameof(parse));
            this.describe = describe ?? DescribeDefault;
            this.defaultValue = defaultValue;
            this.editorKind = editorKind;
            Value = defaultValue;
        }

        public T Value { get; private set; }

        public override string DefaultText => describe(defaultValue);

        public override EditorKind EditorKind => editorKind;

        public override object? GetValue() => Value;

        public override object? DefaultValue => defaultValue;

        public override bool TrySetValue(object? value, out string? error)
        {
            T parsed;
            switch (value)
            {
                case T typed:
                    parsed = typed;
                    break;
                case string s:
                    try
                    {
                        parsed = parse(s);
                    }
                    catch (FormatException ex)
                    {
                        error = ex.Message;
                        return false;
                    }

                    break;
                case null when default(T) is null:
                    // A reference/nullable T accepts null.
                    parsed = default!;
                    break;
                default:
                    error = $"'{value}' is not a valid value for {LongName}";
                    return false;
            }

            error = null;
            SetValue(parsed);
            return true;
        }

        public override void Apply(string? value, CommandLineBinding binding)
        {
            if (value == null)
            {
                throw new FormatException($"--{LongName} requires a value");
            }

            SetValue(parse(value));
        }

        private void SetValue(T value)
        {
            if (EqualityComparer<T>.Default.Equals(Value, value))
            {
                return;
            }

            Value = value;
            RaiseValueChanged();
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
            SettingCategory category,
            string longName,
            string description,
            T defaultValue,
            IReadOnlyList<Choice<T>> choices)
            : base(role, category, longName, null, null, description)
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

        public override EditorKind EditorKind => EditorKind.Choice;

        public override object? GetValue() => Value;

        public override object? DefaultValue => defaultChoice.Value;

        public override bool TrySetValue(object? value, out string? error)
        {
            // Accept the choice value directly (must be one of the choices) or its canonical name string.
            if (value is T typed)
            {
                foreach (var choice in choices)
                {
                    if (EqualityComparer<T>.Default.Equals(choice.Value, typed))
                    {
                        error = null;
                        SetValue(choice.Value);
                        return true;
                    }
                }

                error = $"'{value}' is not one of the choices for {LongName}";
                return false;
            }

            if (value is string name)
            {
                foreach (var choice in choices)
                {
                    if (string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        error = null;
                        SetValue(choice.Value);
                        return true;
                    }
                }
            }

            error = $"'{value}' is not a valid value for {LongName}";
            return false;
        }

        // Applies a choice by its canonical name (from config or a flag's implied value).
        public override void Apply(string? value, CommandLineBinding binding)
        {
            foreach (var choice in choices)
            {
                if (string.Equals(choice.Name, value, StringComparison.OrdinalIgnoreCase))
                {
                    SetValue(choice.Value);
                    return;
                }
            }

            throw new FormatException($"'{value}' is not a valid value for {LongName}");
        }

        private void SetValue(T value)
        {
            if (EqualityComparer<T>.Default.Equals(Value, value))
            {
                return;
            }

            Value = value;
            RaiseValueChanged();
        }
    }
}
