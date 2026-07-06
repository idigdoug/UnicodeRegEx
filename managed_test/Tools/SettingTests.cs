namespace UnicodeRegEx.Tests.Tools
{
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Settings;

    [TestClass]
    public class SettingTests
    {
        // FLAG SETTING

        [TestMethod]
        public void Flag_EditorKind_IsToggle_AndDefaultsFalse()
        {
            var s = new SearchSettings().IgnoreCase;
            Assert.AreEqual(EditorKind.Toggle, s.EditorKind);
            Assert.AreEqual(false, s.GetValue());
            Assert.AreEqual(false, s.DefaultValue);
            Assert.IsTrue(s.IsDefault);
        }

        [TestMethod]
        public void Flag_TrySetValue_Bool_UpdatesAndRaises()
        {
            var s = new SearchSettings().IgnoreCase;
            var raised = 0;
            s.ValueChanged += (_, _) => raised++;

            Assert.IsTrue(s.TrySetValue(true, out var error));
            Assert.IsNull(error);
            Assert.AreEqual(true, s.GetValue());
            Assert.IsFalse(s.IsDefault);
            Assert.AreEqual(1, raised);
        }

        [TestMethod]
        public void Flag_TrySetValue_SameValue_DoesNotRaise()
        {
            var s = new SearchSettings().IgnoreCase;
            var raised = 0;
            s.ValueChanged += (_, _) => raised++;

            Assert.IsTrue(s.TrySetValue(false, out _)); // already false
            Assert.AreEqual(0, raised);
        }

        [TestMethod]
        public void Flag_TrySetValue_BadType_Fails_LeavesValue()
        {
            var s = new SearchSettings().IgnoreCase;
            Assert.IsFalse(s.TrySetValue(42, out var error));
            Assert.IsNotNull(error);
            Assert.AreEqual(false, s.GetValue());
        }

        [TestMethod]
        public void Flag_Reset_RestoresDefault()
        {
            var s = new SearchSettings().IgnoreCase;
            s.TrySetValue(true, out _);
            s.Reset();
            Assert.AreEqual(false, s.GetValue());
            Assert.IsTrue(s.IsDefault);
        }

        // VALUE SETTING

        [TestMethod]
        public void Value_EditorKind_ComesFromDeclaration()
        {
            var settings = new SearchSettings();
            Assert.AreEqual(EditorKind.Integer, settings.Encoding.EditorKind);
            Assert.AreEqual(EditorKind.Text, settings.Replace.EditorKind);
        }

        [TestMethod]
        public void Value_TrySetValue_Typed_Updates()
        {
            var s = new SearchSettings().Encoding;
            Assert.IsTrue(s.TrySetValue(RegExCodePage.Latin1, out var error));
            Assert.IsNull(error);
            Assert.AreEqual(RegExCodePage.Latin1, s.GetValue());
        }

        [TestMethod]
        public void Value_TrySetValue_String_ParsesThroughSetting()
        {
            var s = new SearchSettings().Encoding;
            Assert.IsTrue(s.TrySetValue("latin1", out _));
            Assert.AreEqual(RegExCodePage.Latin1, s.GetValue());
        }

        [TestMethod]
        public void Value_TrySetValue_BadString_Fails_LeavesValue()
        {
            var s = new SearchSettings().Encoding;
            Assert.IsFalse(s.TrySetValue("not-a-codepage", out var error));
            Assert.IsNotNull(error);
            Assert.AreEqual(RegExCodePage.Latin1, s.GetValue());
        }

        [TestMethod]
        public void Value_Default_And_Reset()
        {
            var s = new SearchSettings().Encoding;
            Assert.AreEqual(RegExCodePage.Latin1, s.DefaultValue);
            Assert.IsTrue(s.IsDefault);
            s.TrySetValue(RegExCodePage.Utf8, out _);
            Assert.IsFalse(s.IsDefault);
            s.Reset();
            Assert.IsTrue(s.IsDefault);
            Assert.AreEqual(RegExCodePage.Latin1, s.GetValue());
        }

        // CHOICE SETTING

        [TestMethod]
        public void Choice_EditorKind_IsChoice_AndDefault()
        {
            var s = new SearchSettings().SyntaxFlavor;
            Assert.AreEqual(EditorKind.Choice, s.EditorKind);
            Assert.AreEqual(RegExSyntaxFlags.Perl, s.GetValue());
            Assert.AreEqual(RegExSyntaxFlags.Perl, s.DefaultValue);
        }

        [TestMethod]
        public void Choice_TrySetValue_ByValue_Updates()
        {
            var s = new SearchSettings().SyntaxFlavor;
            var raised = 0;
            s.ValueChanged += (_, _) => raised++;

            Assert.IsTrue(s.TrySetValue(RegExSyntaxFlags.Basic, out _));
            Assert.AreEqual(RegExSyntaxFlags.Basic, s.GetValue());
            Assert.AreEqual(1, raised);
        }

        [TestMethod]
        public void Choice_TrySetValue_ByName_Updates()
        {
            var s = new SearchSettings().SyntaxFlavor;
            Assert.IsTrue(s.TrySetValue("extended", out _));
            Assert.AreEqual(RegExSyntaxFlags.Extended, s.GetValue());
        }

        [TestMethod]
        public void Choice_TrySetValue_ValueNotAmongChoices_Fails()
        {
            var s = new SearchSettings().SyntaxFlavor;
            // A syntax-flags value that is not one of the declared choices.
            Assert.IsFalse(s.TrySetValue(RegExSyntaxFlags.Awk, out var error));
            Assert.IsNotNull(error);
            Assert.AreEqual(RegExSyntaxFlags.Perl, s.GetValue());
        }

        [TestMethod]
        public void Choice_Apply_RaisesValueChanged()
        {
            var settings = new SearchSettings();
            var s = settings.SyntaxFlavor;
            var raised = 0;
            s.ValueChanged += (_, _) => raised++;

            var errors = new List<string>();
            CommandLine.Parse(new[] { "-F" }, settings, errors);

            Assert.AreEqual(RegExSyntaxFlags.Literal, s.GetValue());
            Assert.AreEqual(1, raised);
        }

        // GLOB LIST SETTING (opt-out)

        [TestMethod]
        public void GlobList_EditorKind_IsList()
        {
            Assert.AreEqual(EditorKind.List, new SearchSettings().FileNameFilters.EditorKind);
        }

        [TestMethod]
        public void GlobList_GetValue_Throws()
        {
            var s = new SearchSettings().FileNameFilters;
            TestHelpers.AssertThrows<System.NotSupportedException>(() => s.GetValue());
        }

        [TestMethod]
        public void GlobList_TrySetValue_Throws()
        {
            var s = new SearchSettings().FileNameFilters;
            TestHelpers.AssertThrows<System.NotSupportedException>(() => s.TrySetValue("x", out _));
        }
    }
}
