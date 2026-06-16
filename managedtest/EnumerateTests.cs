namespace UnicodeRegEx.Tests
{
    using System.Collections.Generic;
    using System.Text;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    [TestClass]
    public class EnumerateTests
    {
        [TestMethod]
        public void EnumerateMatches_FindsAllOccurrences()
        {
            var regex = RegEx.Create("a");

            var matches = regex.EnumerateMatches("banana", default, e =>
            {
                var list = new List<string>();
                foreach (var m in e)
                {
                    list.Add(m.Text);
                }

                return list;
            });

            CollectionAssert.AreEqual(new[] { "a", "a", "a" }, matches);
        }

        [TestMethod]
        public void EnumerateMatches_NoMatches_YieldsNothing()
        {
            var regex = RegEx.Create("z");

            int count = regex.EnumerateMatches("banana", default, e =>
            {
                int n = 0;
                foreach (var _ in e)
                {
                    n++;
                }

                return n;
            });

            Assert.AreEqual(0, count);
        }

        [TestMethod]
        public void EnumerateMatches_CanBeEnumeratedMoreThanOnce()
        {
            var regex = RegEx.Create("a");

            // Each GetEnumerator() (each foreach) creates a fresh native cursor that scans
            // from the beginning, so the same enumerable yields the same matches every time.
            var counts = regex.EnumerateMatches("banana", default, matches =>
            {
                int first = 0;
                foreach (var _ in matches)
                {
                    first++;
                }

                int second = 0;
                foreach (var _ in matches)
                {
                    second++;
                }

                return (first, second);
            });

            Assert.AreEqual(3, counts.first);
            Assert.AreEqual(3, counts.second, "Re-enumerating should scan again from the beginning.");
        }

        [TestMethod]
        public void EnumerateMatches_ManualEnumeratorDispose()
        {
            var regex = RegEx.Create("a");

            // Drive a manually-obtained enumerator to completion and dispose it explicitly;
            // the enumerator owns its cursor, and Dispose is idempotent.
            int count = regex.EnumerateMatches("aaa", default, matches =>
            {
                var e = matches.GetEnumerator();
                int n = 0;
                try
                {
                    while (e.MoveNext())
                    {
                        n++;
                    }
                }
                finally
                {
                    e.Dispose();
                    e.Dispose(); // idempotent
                }

                return n;
            });

            Assert.AreEqual(3, count);
        }

        [TestMethod]
        public void EnumerateMatches_WithFormatTemplate_FormatsEachMatch()
        {
            var regex = RegEx.Create(@"(\w)(\w)");
            var options = new RegExEnumerateOptions { FormatTemplate = "[$2$1]" };

            var formatted = regex.EnumerateMatches("abcd", options, e =>
            {
                var sb = new StringBuilder();
                foreach (var m in e)
                {
                    sb.Append(m.Format());
                }

                return sb.ToString();
            });

            Assert.AreEqual("[ba][dc]", formatted);
        }

        [TestMethod]
        public void EnumerateSegments_CoversWholeInputAlternatingMatches()
        {
            var regex = RegEx.Create("a");

            var segments = regex.EnumerateSegments("banana", default, e =>
            {
                var list = new List<(bool IsMatch, string Text)>();
                foreach (var s in e)
                {
                    list.Add((s.IsMatch, TestHelpers.SegmentText(s)));
                }

                return list;
            });

            var expected = new (bool, string)[]
            {
                (false, "b"),
                (true, "a"),
                (false, "n"),
                (true, "a"),
                (false, "n"),
                (true, "a"),
            };
            CollectionAssert.AreEqual(expected, segments);

            // The concatenation of every segment reconstructs the whole input.
            var sb = new StringBuilder();
            foreach (var (_, text) in segments)
            {
                sb.Append(text);
            }

            Assert.AreEqual("banana", sb.ToString());
        }

        [TestMethod]
        public void EnumerateSegments_MatchAtStartAndEnd()
        {
            var regex = RegEx.Create("a");

            var segments = regex.EnumerateSegments("aba", default, e =>
            {
                var list = new List<(bool IsMatch, string Text)>();
                foreach (var s in e)
                {
                    list.Add((s.IsMatch, TestHelpers.SegmentText(s)));
                }

                return list;
            });

            var expected = new (bool, string)[]
            {
                (true, "a"),
                (false, "b"),
                (true, "a"),
            };
            CollectionAssert.AreEqual(expected, segments);
        }

        [TestMethod]
        public void EnumerateSegments_NoMatch_IsSingleUnmatchedSegment()
        {
            var regex = RegEx.Create("z");

            var segments = regex.EnumerateSegments("abc", default, e =>
            {
                var list = new List<(bool IsMatch, string Text)>();
                foreach (var s in e)
                {
                    list.Add((s.IsMatch, TestHelpers.SegmentText(s)));
                }

                return list;
            });

            Assert.AreEqual(1, segments.Count);
            Assert.IsFalse(segments[0].IsMatch);
            Assert.AreEqual("abc", segments[0].Text);
        }

        [TestMethod]
        public void EnumerateSegments_CanBeEnumeratedMoreThanOnce()
        {
            var regex = RegEx.Create("a");

            var counts = regex.EnumerateSegments("banana", default, segments =>
            {
                int first = 0;
                foreach (var _ in segments)
                {
                    first++;
                }

                int second = 0;
                foreach (var _ in segments)
                {
                    second++;
                }

                return (first, second);
            });

            // "b","a","n","a","n","a" => 6 segments, both times.
            Assert.AreEqual(6, counts.first);
            Assert.AreEqual(6, counts.second, "Re-enumerating should scan again from the beginning.");
        }
    }
}
