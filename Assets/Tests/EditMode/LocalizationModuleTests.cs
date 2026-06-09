using System.Collections.Generic;
using System.Reflection;
using Frame.Localization;
using NUnit.Framework;
using UnityEngine;

namespace Frame.Tests.EditMode
{
    public sealed class LocalizationModuleTests
    {
        [Test]
        public void LocalizationService_RegistersTablesTranslatesFallbackAndResetsOnShutdown()
        {
            LocalizedTextTable table = CreateTable("en", new Dictionary<string, string>
            {
                { "menu.start", "Start" },
                { "menu.quit", "Quit" }
            });

            try
            {
                using (FrameTestFixture fixture = new FrameTestFixture())
                {
                    LocalizationService service = fixture.Initialize(new LocalizationService());
                    service.AddTable(table);
                    service.SetLocale("en");

                    Assert.AreEqual("en", service.CurrentLocale);
                    Assert.AreEqual("Start", service.Translate("menu.start"));
                    Assert.AreEqual("Fallback", service.Translate("missing", "Fallback"));
                    Assert.AreEqual("missing", service.Translate("missing"));

                    service.Shutdown();
                    Assert.AreEqual("en", service.CurrentLocale);
                }
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void LocalizedTextTable_TryGetUsesLatestDuplicateKeyAndRejectsEmptyKey()
        {
            LocalizedTextTable table = CreateTable("zh", new Dictionary<string, string>
            {
                { "hello", "你好" }
            });

            try
            {
                Assert.AreEqual("zh", table.Locale);
                Assert.IsTrue(table.TryGet("hello", out string value));
                Assert.AreEqual("你好", value);
                Assert.IsFalse(table.TryGet("", out value));
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        private static LocalizedTextTable CreateTable(string locale, Dictionary<string, string> values)
        {
            LocalizedTextTable table = ScriptableObject.CreateInstance<LocalizedTextTable>();
            FieldInfo localeField = typeof(LocalizedTextTable).GetField("locale", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo entriesField = typeof(LocalizedTextTable).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic);
            localeField.SetValue(table, locale);

            object entries = entriesField.GetValue(table);
            System.Type listType = entries.GetType();
            System.Type entryType = listType.GetGenericArguments()[0];
            MethodInfo addMethod = listType.GetMethod("Add");
            FieldInfo keyField = entryType.GetField("Key");
            FieldInfo valueField = entryType.GetField("Value");

            foreach (KeyValuePair<string, string> pair in values)
            {
                object entry = System.Activator.CreateInstance(entryType);
                keyField.SetValue(entry, pair.Key);
                valueField.SetValue(entry, pair.Value);
                addMethod.Invoke(entries, new[] { entry });
            }

            return table;
        }
    }
}
