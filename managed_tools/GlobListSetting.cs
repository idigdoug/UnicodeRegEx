namespace UnicodeRegEx.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnicodeRegEx.Tools.Settings;

    /// <summary>
    /// A setting holding an ordered, editable list of <see cref="GlobFilter"/> entries (for file-name or
    /// directory filters). Unlike a scalar setting, applying a value <b>appends</b> rather than replaces, so
    /// several command-line occurrences (e.g. <c>--include a --exclude b</c>) accumulate into one ordered
    /// list, preserving encounter order — which the engine's last-match-wins filtering depends on.
    /// <para>
    /// The kind (include/exclude) of an applied entry comes from the matching binding's
    /// <see cref="CommandLineBinding.Tag"/> when present (so one setting can back both an
    /// <c>--include</c> and an <c>--exclude</c> alias), falling back to <see cref="PrimaryKind"/> for the
    /// binding-less overlay path (config). Values may be a single glob or a semicolon-separated list
    /// (<c>*.cs;*.h</c>); empty entries are ignored.
    /// </para>
    /// <para>
    /// <see cref="DefaultText"/> and <see cref="ToDisplayString"/> render the list as a semicolon string for
    /// MRU persistence and GUI display / help — this is a display/round-trip convenience, not a
    /// command-line grammar (the command line appends one binding at a time).
    /// </para>
    /// </summary>
    public sealed class GlobListSetting : Setting
    {
        private readonly List<GlobFilter> filters = new List<GlobFilter>();
        private readonly IReadOnlyList<CommandLineBinding> bindings;

        /// <summary>
        /// Creates a glob-list setting. <paramref name="bindings"/> are the command-line aliases that feed
        /// the list; each may carry a <see cref="FilterKind"/> in its <see cref="CommandLineBinding.Tag"/>
        /// to mark the kind it appends (e.g. an <c>--include</c> binding tagged Include and an
        /// <c>--exclude</c> binding tagged Exclude). <paramref name="primaryKind"/> is used when no tag is
        /// present (the overlay/config path).
        /// </summary>
        public GlobListSetting(
            SettingRole role,
            SettingCategory category,
            string longName,
            char? shortName,
            string valueName,
            string description,
            FilterKind primaryKind,
            IReadOnlyList<CommandLineBinding>? bindings = null)
            : base(role, category, longName, shortName, valueName, description)
        {
            PrimaryKind = primaryKind;
            this.bindings = bindings ?? new[] { new CommandLineBinding(longName, shortName, null, primaryKind) };
        }

        /// <summary>The kind applied when a binding carries no <see cref="FilterKind"/> tag (config overlay).</summary>
        public FilterKind PrimaryKind { get; }

        /// <summary>The accumulated filters, in the order applied. Editable in place for a front-end.</summary>
        public IList<GlobFilter> Filters => filters;

        public override IEnumerable<CommandLineBinding> CommandLineBindings => bindings;

        // The default is an empty list.
        public override string DefaultText => "(none)";

        // Property-page opt-out: a glob list is WorkingState (never shown on the property page) and is a
        // list, not a scalar. It is edited via Filters (or the primary UI) directly, so the scalar
        // typed-value surface is not supported.
        public override EditorKind EditorKind => EditorKind.List;

        public override object? DefaultValue => null;

        public override object? GetValue() =>
            throw new NotSupportedException($"{nameof(GlobListSetting)} is edited via {nameof(Filters)}, not the scalar value surface.");

        public override bool TrySetValue(object? value, out string? error) =>
            throw new NotSupportedException($"{nameof(GlobListSetting)} is edited via {nameof(Filters)}, not the scalar value surface.");

        public override void Apply(string? value, CommandLineBinding binding)
        {
            if (value == null)
            {
                throw new FormatException($"--{binding.LongName ?? LongName} requires a value");
            }

            var kind = binding.Tag is FilterKind tagged ? tagged : PrimaryKind;
            foreach (var glob in value.Split(';'))
            {
                var trimmed = glob.Trim();
                if (trimmed.Length != 0)
                {
                    filters.Add(new GlobFilter(kind, trimmed));
                }
            }
        }

        /// <summary>
        /// Renders the current filters as a semicolon-separated glob list (globs only, kinds omitted) for
        /// display / MRU persistence. Note this is lossy for mixed include/exclude lists and is a display
        /// convenience, not a parseable command-line form.
        /// </summary>
        public string ToDisplayString()
        {
            var sb = new StringBuilder();
            foreach (var filter in filters)
            {
                if (sb.Length != 0)
                {
                    sb.Append(';');
                }

                sb.Append(filter.Glob);
            }

            return sb.ToString();
        }
    }
}
