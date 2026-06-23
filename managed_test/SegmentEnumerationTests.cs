namespace UnicodeRegEx.Tests
{
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    [TestClass]
    public class SegmentEnumerationTests
    {
        // Enumerates the input into a flat list of (isMatch, text) segments.
        private static List<(bool IsMatch, string Text)> Segments(string pattern, string input)
        {
            var regex = RegEx.Create(pattern);
            return regex.EnumerateSegments(input, default, segments =>
            {
                var list = new List<(bool, string)>();
                foreach (var segment in segments)
                {
                    list.Add((segment.IsMatch, TestHelpers.SegmentText(segment)));
                }

                return list;
            });
        }

        // Reconstructing the input from all segment texts (in order) must reproduce it exactly.
        private static string Reassemble(List<(bool IsMatch, string Text)> segments)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var segment in segments)
            {
                sb.Append(segment.Text);
            }

            return sb.ToString();
        }

        [TestMethod]
        public void Segments_MatchInMiddle_PartitionsAroundMatch()
        {
            var segments = Segments("a", "bab");

            CollectionAssert.AreEqual(
                new List<(bool, string)> { (false, "b"), (true, "a"), (false, "b") },
                segments);
        }

        [TestMethod]
        public void Segments_LeadingMatch_StartsWithMatch()
        {
            var segments = Segments("a", "ab");

            CollectionAssert.AreEqual(
                new List<(bool, string)> { (true, "a"), (false, "b") },
                segments);
        }

        [TestMethod]
        public void Segments_TrailingMatch_EndsWithMatch()
        {
            var segments = Segments("a", "ba");

            CollectionAssert.AreEqual(
                new List<(bool, string)> { (false, "b"), (true, "a") },
                segments);
        }

        [TestMethod]
        public void Segments_AdjacentMatches_ProduceConsecutiveMatchSegments()
        {
            var segments = Segments("a", "aa");

            CollectionAssert.AreEqual(
                new List<(bool, string)> { (true, "a"), (true, "a") },
                segments);
        }

        [TestMethod]
        public void Segments_WholeInputMatches_IsASingleMatchSegment()
        {
            var segments = Segments("abc", "abc");

            CollectionAssert.AreEqual(
                new List<(bool, string)> { (true, "abc") },
                segments);
        }

        [TestMethod]
        public void Segments_NoMatch_IsASingleUnmatchedSegment()
        {
            var segments = Segments("z", "abc");

            CollectionAssert.AreEqual(
                new List<(bool, string)> { (false, "abc") },
                segments);
        }

        [TestMethod]
        public void Segments_MultipleMatches_AlternateCorrectly()
        {
            var segments = Segments("a", "banana");

            CollectionAssert.AreEqual(
                new List<(bool, string)>
                {
                    (false, "b"), (true, "a"), (false, "n"),
                    (true, "a"), (false, "n"), (true, "a"),
                },
                segments);
        }

        [TestMethod]
        public void Segments_Reassembled_ReproducesInput()
        {
            const string input = "the quick brown fox";
            var segments = Segments("o", input);

            Assert.AreEqual(input, Reassemble(segments));
        }

        [TestMethod]
        public void Segments_IsMatchFlag_DistinguishesMatchedFromUnmatched()
        {
            var segments = Segments("\\d+", "ab12cd34");

            CollectionAssert.AreEqual(
                new List<(bool, string)>
                {
                    (false, "ab"), (true, "12"), (false, "cd"), (true, "34"),
                },
                segments);
        }
    }
}
