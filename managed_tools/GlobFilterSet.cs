namespace UnicodeRegEx.Tools
{
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Compiled form of an ordered list of <see cref="GlobFilter"/>s (used for both file names and
    /// directory names). Evaluates grep's rule: the last matching filter wins, and if none match, the
    /// caller-supplied default applies. Contiguous same-kind filters are collapsed into a single regex
    /// segment, so a run of includes (or excludes) costs one <see cref="Regex"/> and one match test
    /// rather than several; this is order-preserving (within a run only "did the run match" and its
    /// shared kind matter), so the collapse is behaviorally identical to evaluating each filter
    /// individually.
    /// </summary>
    public sealed class GlobFilterSet
    {
        private readonly struct Segment
        {
            public Segment(FilterKind kind, Regex regex)
            {
                Kind = kind;
                Regex = regex;
            }

            public FilterKind Kind { get; }

            public Regex Regex { get; }
        }

        private readonly Segment[] segments;
        private readonly bool defaultInclude;

        private GlobFilterSet(Segment[] segments, bool defaultInclude)
        {
            this.segments = segments;
            this.defaultInclude = defaultInclude;
        }

        /// <summary>
        /// Compiles the filters into a set, or returns null when the list is empty (meaning "no
        /// filtering" — every name is included). <paramref name="defaultIncludeWhenNoMatch"/> is the
        /// verdict for a name that matches no filter: file-name filters pass grep's rule (included unless
        /// the first filter is an include), while directory filters pass true so an unmatched directory
        /// is always descended.
        /// </summary>
        public static GlobFilterSet? Compile(IReadOnlyList<GlobFilter> filters, bool defaultIncludeWhenNoMatch)
        {
            if (filters == null || filters.Count == 0)
            {
                return null;
            }

            var segments = new List<Segment>();
            var run = new List<string>();
            var runKind = filters[0].Kind;

            foreach (var filter in filters)
            {
                if (filter.Kind != runKind && run.Count != 0)
                {
                    segments.Add(BuildSegment(runKind, run));
                    run.Clear();
                }

                runKind = filter.Kind;
                run.Add(GlobToRegex.TranslateGlobToAlternative(filter.Glob));
            }

            if (run.Count != 0)
            {
                segments.Add(BuildSegment(runKind, run));
            }

            return new GlobFilterSet(segments.ToArray(), defaultIncludeWhenNoMatch);
        }

        /// <summary>
        /// Returns true if <paramref name="name"/> (a file or directory name) should be included: the
        /// kind of the last segment whose regex matches, or the default verdict when nothing matches.
        /// </summary>
        public bool ShouldInclude(string name)
        {
            var included = defaultInclude;
            foreach (var segment in segments)
            {
                if (segment.Regex.IsMatch(name))
                {
                    included = segment.Kind == FilterKind.Include;
                }
            }

            return included;
        }

        private static Segment BuildSegment(FilterKind kind, List<string> alternatives)
        {
            var combined = "^(?:" + string.Join("|", alternatives) + ")$";
            var regex = new Regex(combined, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return new Segment(kind, regex);
        }
    }
}
