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
                    list.Add(TestHelpers.WholeMatchText(m));
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
        public void EnumerateMatches_State_TransitionsToFinished()
        {
            var regex = RegEx.Create("a");

            // Drive MoveNext() manually rather than via foreach: a foreach over the
            // ref struct enumerator disposes (releases) the underlying COM object at
            // the end of the loop, after which State could not be read.
            var finalState = regex.EnumerateMatches("aaa", default, e =>
            {
                Assert.AreEqual(RegExEnumerationState.NotStarted, e.State);
                while (e.MoveNext())
                {
                    Assert.AreEqual(RegExEnumerationState.Enumerating, e.State);
                }

                return e.State;
            });

            Assert.AreEqual(RegExEnumerationState.Finished, finalState);
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
    }
}
