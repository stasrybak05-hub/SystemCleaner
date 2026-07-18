using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Керування локалізацією інтерфейсу.
    /// Підтримує українську, англійську та російську мови.
    /// Налаштування зберігаються через SettingsManager.
    /// </summary>
    public static class LangManager
    {
        public enum Language
        {
            Ukrainian,
            English,
            Russian
        }

        private static readonly Dictionary<Language, Uri> LangUris = new()
        {
            { Language.Ukrainian, new Uri("Localization/Strings.uk.xaml", UriKind.Relative) },
            { Language.English,   new Uri("Localization/Strings.en.xaml", UriKind.Relative) },
            { Language.Russian,   new Uri("Localization/Strings.ru.xaml", UriKind.Relative) },
        };

        public static Language CurrentLanguage { get; private set; } = Language.Ukrainian;

        /// <summary>Ініціалізація: читає мову з SettingsManager і застосовує її.</summary>
        public static void Initialize(Application app)
        {
            if (Enum.TryParse<Language>(SettingsManager.Language, out var savedLang))
                CurrentLanguage = savedLang;
            else
                CurrentLanguage = Language.Ukrainian;

            ApplyLanguage(app, CurrentLanguage);
        }

        /// <summary>Застосовує мову і оновлює SettingsManager (у пам'яті).</summary>
        public static void Apply(Application app, Language lang)
        {
            CurrentLanguage = lang;
            ApplyLanguage(app, lang);

            SettingsManager.Language = lang.ToString();
        }

        private static void ApplyLanguage(Application app, Language lang)
        {
            if (!LangUris.TryGetValue(lang, out var uri)) return;

            var dict = new ResourceDictionary { Source = uri };

            var toRemove = app.Resources.MergedDictionaries
                .Cast<ResourceDictionary>()
                .Where(d => d.Source != null && d.Source.OriginalString.Contains("Strings."))
                .ToList();

            foreach (var d in toRemove)
                app.Resources.MergedDictionaries.Remove(d);

            app.Resources.MergedDictionaries.Add(dict);
        }
    }
}