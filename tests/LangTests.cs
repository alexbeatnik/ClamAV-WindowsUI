// Tests for the two-language string table (src\Lang.cs).
using System;
using ClamAVUI;

namespace ClamAVUI.Tests
{
    static class LangTests
    {
        public static void TestEnglishLookup()
        {
            using (new LangScope(Lang.Language.English))
                Assert.Equal("Ready.", Lang.T("status.ready"), "English string");
        }

        public static void TestUkrainianLookup()
        {
            using (new LangScope(Lang.Language.Ukrainian))
                Assert.Equal("Готово.", Lang.T("status.ready"), "Ukrainian string");
        }

        public static void TestUnknownKeyReturnsTheKeyItself()
        {
            using (new LangScope(Lang.Language.English))
                Assert.Equal("no.such.key", Lang.T("no.such.key"), "unknown key falls through to itself");
            using (new LangScope(Lang.Language.Ukrainian))
                Assert.Equal("no.such.key", Lang.T("no.such.key"), "same in Ukrainian");
        }

        public static void TestFormatPlaceholdersSurviveTranslation()
        {
            // keys used with string.Format must keep their placeholders in both languages
            string[] formatKeys = { "settings.monitorLabel", "status.progress", "hero.dbFrom", "msg.deleteConfirm" };
            foreach (string key in formatKeys)
            {
                string en, uk;
                using (new LangScope(Lang.Language.English)) en = Lang.T(key);
                using (new LangScope(Lang.Language.Ukrainian)) uk = Lang.T(key);
                Assert.True(en.Contains("{0}"), key + " (en) keeps {0}");
                Assert.True(uk.Contains("{0}"), key + " (uk) keeps {0}");
            }
        }

        public static void TestTimeFormatsAreFormattable()
        {
            // the FormatSpan patterns must be valid string.Format inputs in both languages
            foreach (Lang.Language lang in new Lang.Language[] { Lang.Language.English, Lang.Language.Ukrainian })
                using (new LangScope(lang))
                {
                    string.Format(Lang.T("time.hm"), 1.0, 2.0);
                    string.Format(Lang.T("time.ms"), 1.0, 2.0);
                    string.Format(Lang.T("time.s"), 1.0); // throws FormatException if broken
                }
        }
    }
}
