using Frame.Localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Tests.EditMode
{
    public sealed class LocalizationModuleTests
    {
        [Test]
        public void LocalizationService_RegistersSpreadsheetTablesTranslatesFallbackAndResetsOnShutdown()
        {
            LocalizedTextTable table = CreateTable("key,en,zh\nmenu.start,Start,Start-ZH\nmenu.quit,Quit,Quit-ZH");

            try
            {
                using (FrameTestFixture fixture = new FrameTestFixture())
                {
                    LocalizationService service = fixture.Initialize(new LocalizationService());
                    service.AddTable(table);
                    service.SetLocale("en");

                    Assert.AreEqual("en", service.CurrentLocale);
                    Assert.AreEqual("Start", service.Translate("menu.start"));
                    Assert.IsTrue(service.TryTranslate("menu.quit", out string translated));
                    Assert.AreEqual("Quit", translated);

                    service.SetLocale("zh");
                    Assert.AreEqual("Start-ZH", service.Translate("menu.start"));
                    Assert.AreEqual("Fallback", service.Translate("missing", "Fallback"));
                    Assert.AreEqual("missing", service.Translate("missing"));
                    CollectionAssert.Contains(service.MissingKeys, "missing");

                    service.Shutdown();
                    Assert.AreEqual("en", service.CurrentLocale);
                    Assert.AreEqual("en", service.FallbackLocale);
                    Assert.AreEqual(0, service.MissingKeys.Count);
                }
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void LocalizationService_LatestRegisteredTableOverridesEarlierValues()
        {
            LocalizedTextTable baseTable = CreateTable("key,en,zh\nmenu.start,Start,Start-ZH");
            LocalizedTextTable overrideTable = CreateTable("key,en,zh\nmenu.start,Play,Play-ZH");

            try
            {
                using (FrameTestFixture fixture = new FrameTestFixture())
                {
                    LocalizationService service = fixture.Initialize(new LocalizationService());
                    service.AddTable(baseTable);
                    service.AddTable(overrideTable);

                    Assert.AreEqual("Play", service.Translate("menu.start"));

                    service.SetLocale("zh");
                    Assert.AreEqual("Play-ZH", service.Translate("menu.start"));
                }
            }
            finally
            {
                Object.DestroyImmediate(baseTable);
                Object.DestroyImmediate(overrideTable);
            }
        }

        [Test]
        public void LocalizationService_SupportsFallbackFormattingMissingKeysAndTableRemoval()
        {
            LocalizedTextTable table = CreateTable("key,en,zh\nscore,Score {0},Score-ZH {0}\nfallback.only,Fallback Only,");

            try
            {
                using (FrameTestFixture fixture = new FrameTestFixture())
                {
                    LocalizationService service = fixture.Initialize(new LocalizationService());
                    service.AddTable(table);
                    service.SetLocale("fr");
                    service.FallbackLocale = "en";

                    Assert.IsTrue(service.TryTranslate("fallback.only", out string fallbackValue));
                    Assert.AreEqual("Fallback Only", fallbackValue);
                    Assert.AreEqual("Score 7", service.Translate("score", null, 7));
                    Assert.AreEqual(0, service.MissingKeys.Count);

                    Assert.AreEqual("Missing 9", service.Translate("missing.format", "Missing {0}", 9));
                    CollectionAssert.Contains(service.MissingKeys, "missing.format");

                    service.ClearMissingKeys();
                    Assert.AreEqual(0, service.MissingKeys.Count);

                    Assert.IsTrue(service.RemoveTable(table));
                    Assert.AreEqual("score", service.Translate("score"));
                    CollectionAssert.Contains(service.MissingKeys, "score");

                    service.ClearTables();
                    service.ClearMissingKeys();
                    Assert.IsFalse(service.TryTranslate("fallback.only", out fallbackValue));
                    Assert.AreEqual(0, service.MissingKeys.Count);
                }
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void LocalizedTextTable_ImportsSpreadsheetCsvWithLocalesDuplicateKeysAndQuotedValues()
        {
            LocalizedTextTable table = CreateTable(
                "key,en,zh\n" +
                "hello,Hello,Hello-ZH\n" +
                "quote,\"Hello, Player\",\"Quote-ZH\"\n" +
                "hello,Hi,Hi-ZH\n" +
                ",Ignored,Ignored-ZH");

            try
            {
                Assert.AreEqual("en", table.Locale);
                Assert.AreEqual(2, table.Locales.Count);
                Assert.IsTrue(table.ContainsLocale("zh"));

                Assert.IsTrue(table.TryGet("en", "hello", out string value));
                Assert.AreEqual("Hi", value);

                Assert.IsTrue(table.TryGet("zh", "hello", out value));
                Assert.AreEqual("Hi-ZH", value);

                Assert.IsTrue(table.TryGet("en", "quote", out value));
                Assert.AreEqual("Hello, Player", value);

                Assert.IsFalse(table.TryGet("en", "", out value));
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void LocalizedTextTable_SetValueBuildsSpreadsheetTableInCode()
        {
            LocalizedTextTable table = ScriptableObject.CreateInstance<LocalizedTextTable>();
            try
            {
                table.SetValue("menu.start", "en", "Start");
                table.SetValue("menu.start", "zh", "Start-ZH");

                Assert.IsTrue(table.TryGet("en", "menu.start", out string value));
                Assert.AreEqual("Start", value);

                Assert.IsTrue(table.TryGet("zh", "menu.start", out value));
                Assert.AreEqual("Start-ZH", value);
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void LocalizedTextTable_CanReadCsvTextAssetSource()
        {
            LocalizedTextTable table = ScriptableObject.CreateInstance<LocalizedTextTable>();
            TextAsset source = new TextAsset("key,en,zh\nmenu.start,Start,Start-ZH");
            try
            {
                table.SetSource(source);

                Assert.IsTrue(table.TryGet("en", "menu.start", out string value));
                Assert.AreEqual("Start", value);

                Assert.IsTrue(table.TryGet("zh", "menu.start", out value));
                Assert.AreEqual("Start-ZH", value);
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void LocalizationService_SetLocaleRaisesChangedEventOnlyWhenLocaleChanges()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                LocalizationService service = fixture.Initialize(new LocalizationService());
                int changedCount = 0;
                string changedLocale = null;

                service.LocaleChanged += locale =>
                {
                    changedCount++;
                    changedLocale = locale;
                };

                service.SetLocale("en");
                Assert.AreEqual(0, changedCount);

                service.SetLocale("zh");
                Assert.AreEqual(1, changedCount);
                Assert.AreEqual("zh", changedLocale);

                service.SetLocale("zh");
                Assert.AreEqual(1, changedCount);
            }
        }

        [Test]
        public void LocalizedText_RefreshesWhenBoundLocaleChanges()
        {
            LocalizedTextTable table = CreateTable("key,en,zh\nmenu.start,Start,Start-ZH");
            GameObject go = new GameObject("LocalizedText");

            try
            {
                using (FrameTestFixture fixture = new FrameTestFixture())
                {
                    LocalizationService service = fixture.Initialize(new LocalizationService());
                    service.AddTable(table);

                    Text text = go.AddComponent<Text>();
                    LocalizedText localizedText = go.AddComponent<LocalizedText>();
                    localizedText.SetKey("menu.start");
                    localizedText.Bind(service);

                    Assert.AreEqual("Start", text.text);

                    service.SetLocale("zh");
                    Assert.AreEqual("Start-ZH", text.text);

                    localizedText.SetKey("missing");
                    Assert.AreEqual("missing", text.text);

                    localizedText.SetFallback("Fallback");
                    Assert.AreEqual("Fallback", text.text);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(table);
            }
        }

        private static LocalizedTextTable CreateTable(string csv)
        {
            LocalizedTextTable table = ScriptableObject.CreateInstance<LocalizedTextTable>();
            table.ImportCsv(csv);
            return table;
        }
    }
}
