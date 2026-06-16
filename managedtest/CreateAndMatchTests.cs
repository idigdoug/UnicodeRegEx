namespace UnicodeRegEx.Tests
{
    using System;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    [TestClass]
    public class CreateAndMatchTests
    {
        [TestMethod]
        public void Create_ExposesPatternFlagsAndLcid()
        {
            var regex = RegEx.Create("ab.c", RegExSyntaxFlags.ECMAScript, 0);

            Assert.AreEqual("ab.c", regex.Pattern);
            Assert.AreEqual(0u, regex.Lcid);
            // ECMAScript == Normal == 0; the syntax group is Perl.
            Assert.AreEqual(RegExSyntaxFlags.PerlSyntaxGroup, regex.Flags & RegExSyntaxFlags.SyntaxGroupMask);
        }

        [TestMethod]
        public void Create_InvalidPattern_ThrowsRegExException()
        {
            try
            {
                RegEx.Create("(unterminated");
                Assert.Fail("Expected RegExException for an invalid pattern.");
            }
            catch (RegExException ex)
            {
                Assert.AreEqual("(unterminated", ex.Pattern);
                Assert.AreEqual(RegExErrorCode.Paren, ex.ErrorCode);
                Assert.IsFalse(string.IsNullOrEmpty(ex.NativeMessage));
            }
        }

        [TestMethod]
        public void Dispose_IsSupportedAndIdempotent()
        {
            string text;
            using (var regex = RegEx.Create("a.c"))
            {
                text = regex.Search("xxabcxx", default, "<none>", m => m.Text);
            }

            Assert.AreEqual("abc", text);

            // Dispose again (outside the using) is a no-op, not a throw.
            var regex2 = RegEx.Create("a.c");
            regex2.Dispose();
            regex2.Dispose();
        }

        [TestMethod]
        public void Match_WholeString_Matches()
        {
            var regex = RegEx.Create("a.c");

            var text = regex.Match("abc", default, "<none>", m => m.Text);

            Assert.AreEqual("abc", text);
        }

        [TestMethod]
        public void Match_IsAnchoredToWholeString()
        {
            var regex = RegEx.Create("abc");

            // Match requires the whole input to match, so a substring match fails.
            var text = regex.Match("xabcx", default, "<none>", m => m.Text);

            Assert.AreEqual("<none>", text);
        }

        [TestMethod]
        public void Match_ActionOverload_InvokedOnlyWhenMatched()
        {
            var regex = RegEx.Create("abc");

            int matched = 0;
            string captured = string.Empty;
            regex.Match("abc", default, m => { matched++; captured = m.Text; });
            Assert.AreEqual(1, matched);
            Assert.AreEqual("abc", captured);

            matched = 0;
            regex.Match("abcd", default, m => matched++);
            Assert.AreEqual(0, matched);
        }

        [TestMethod]
        public void Match_CaptureGroups_AreReported()
        {
            var regex = RegEx.Create(@"(\d+)-(\d+)-(\d+)");

            var groups = regex.Match("2023-01-02", default, Array.Empty<string>(), m =>
            {
                var arr = new string[m.SubMatchCount];
                for (int i = 0; i < m.SubMatchCount; i++)
                {
                    arr[i] = m.GetSubMatchText(i)!;
                }

                return arr;
            });

            CollectionAssert.AreEqual(new[] { "2023-01-02", "2023", "01", "02" }, groups);
        }

        [TestMethod]
        public void Search_FindsSubstring()
        {
            var regex = RegEx.Create("abc");

            var text = regex.Search("xxabcxx", default, "<none>", m => m.Text);

            Assert.AreEqual("abc", text);
        }

        [TestMethod]
        public void Search_NoMatch_ReturnsFallback()
        {
            var regex = RegEx.Create("zzz");

            var text = regex.Search("xxabcxx", default, "<none>", m => m.Text);

            Assert.AreEqual("<none>", text);
        }

        [TestMethod]
        public void Search_RespectsStartByteOffset_Utf16()
        {
            var regex = RegEx.Create("ab");

            // Two "ab" occurrences; starting after the first (4 bytes = 2 UTF-16 chars)
            // finds the second one.
            var options = new RegExMatchOptions { StartByteOffset = 4 };
            var found = regex.Search("abab", options, false, m =>
            {
                var sub = m.GetSubMatch(0);
                Assert.AreEqual((nuint)4, sub.Begin);
                return true;
            });

            Assert.IsTrue(found);
        }

        [TestMethod]
        public void Search_Utf8Bytes_Matches()
        {
            var regex = RegEx.Create("caf\u00e9"); // café

            var bytes = TestHelpers.Encode("a caf\u00e9 here", RegExEncoding.Utf8);
            var text = regex.Search(new RegExInput(bytes, RegExEncoding.Utf8), default, "<none>",
                m => m.Text);

            Assert.AreEqual("caf\u00e9", text);
        }

        [TestMethod]
        public void Search_Latin1Bytes_Matches()
        {
            var regex = RegEx.Create("[0-9]+");

            var bytes = TestHelpers.Encode("abc12345xyz", RegExEncoding.Latin1);
            var text = regex.Search(new RegExInput(bytes, RegExEncoding.Latin1), default, "<none>",
                m => m.Text);

            Assert.AreEqual("12345", text);
        }

        [TestMethod]
        public void Create_ICase_MatchesRegardlessOfCase()
        {
            var regex = RegEx.Create("abc", RegExSyntaxFlags.ECMAScript | RegExSyntaxFlags.ICase);

            var text = regex.Search("XXABCXX", default, "<none>", m => m.Text);

            Assert.AreEqual("ABC", text);
        }

        [TestMethod]
        public void Text_ReturnsWholeMatch()
        {
            var regex = RegEx.Create("a.c");

            var text = regex.Search("xxabcxx", default, "<none>", m => m.Text);

            Assert.AreEqual("abc", text);
        }

        [TestMethod]
        public void GetSubMatchText_ReturnsCaptureGroupText()
        {
            var regex = RegEx.Create(@"(\w+)@(\w+)");

            var parts = regex.Search("user@host", default, Array.Empty<string?>(), m =>
                new[] { m.GetSubMatchText(0), m.GetSubMatchText(1), m.GetSubMatchText(2) });

            CollectionAssert.AreEqual(new[] { "user@host", "user", "host" }, parts);
        }

        [TestMethod]
        public void GetSubMatchText_NonParticipatingGroup_ReturnsNull()
        {
            // Group 2 "(b)?" does not participate when matching "a".
            var regex = RegEx.Create("(a)(b)?");

            (string? g1, string? g2) result = regex.Search(
                "a",
                default,
                (g1: "none", g2: "none"),
                m => (g1: m.GetSubMatchText(1), g2: m.GetSubMatchText(2)));

            Assert.AreEqual("a", result.g1);
            Assert.IsNull(result.g2, "Non-participating optional group should yield null.");
        }

        [TestMethod]
        public void GetSubMatchText_ParticipatingEmptyGroup_ReturnsEmptyString()
        {
            // Group 1 "(a*)" participates but matches empty at the start of "bbb".
            var regex = RegEx.Create("(a*)");

            var g1 = regex.Search("bbb", default, "none", m => m.GetSubMatchText(1));

            Assert.AreEqual(string.Empty, g1, "Participating zero-length group should yield empty string, not null.");
        }
    }
}
