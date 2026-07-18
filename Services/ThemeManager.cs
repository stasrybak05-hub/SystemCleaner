using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Керування темами інтерфейсу.
    /// Підтримує 6 тем: Dark, Light, Sea, Black, Gray, Sunset.
    /// Налаштування зберігаються через SettingsManager.
    /// </summary>
    public static class ThemeManager
    {
        public enum ThemeType
        {
            Dark,
            Light,
            Sea,
            Black,
            Gray,
            Sunset
        }

        private static readonly Dictionary<ThemeType, Uri> ThemeUris = new()
        {
            { ThemeType.Dark,   new Uri("Themes/DarkTheme.xaml", UriKind.Relative) },
            { ThemeType.Light,  new Uri("Themes/LightTheme.xaml", UriKind.Relative) },
            { ThemeType.Sea,    new Uri("Themes/SeaTheme.xaml", UriKind.Relative) },
            { ThemeType.Black,  new Uri("Themes/BlackTheme.xaml", UriKind.Relative) },
            { ThemeType.Gray,   new Uri("Themes/GrayTheme.xaml", UriKind.Relative) },
            { ThemeType.Sunset, new Uri("Themes/SunsetTheme.xaml", UriKind.Relative) },
        };

        public static ThemeType CurrentTheme { get; private set; } = ThemeType.Dark;

        /// <summary>Ініціалізація: читає тему з SettingsManager і застосовує її.</summary>
        public static void Initialize(Application app)
        {
            // Читаємо збережену тему з SettingsManager
            if (Enum.TryParse<ThemeType>(SettingsManager.Theme, out var savedTheme))
                CurrentTheme = savedTheme;
            else
                CurrentTheme = ThemeType.Dark;

            ApplyTheme(app, CurrentTheme);
        }

        /// <summary>Застосовує тему і оновлює SettingsManager (у пам'яті).</summary>
        public static void Apply(Application app, ThemeType theme)
        {
            CurrentTheme = theme;
            ApplyTheme(app, theme);

            // Оновлюємо SettingsManager у пам'яті (збереження на диск буде при виході)
            SettingsManager.Theme = theme.ToString();
        }

        private static void ApplyTheme(Application app, ThemeType theme)
        {
            if (!ThemeUris.TryGetValue(theme, out var uri)) return;

            var themeDict = new ResourceDictionary { Source = uri };

            // Знаходимо і видаляємо СТАРУ тему (не чіпаємо SharedStyles і локалізацію)
            var oldTheme = app.Resources.MergedDictionaries
                .Cast<ResourceDictionary>()
                .FirstOrDefault(d => d.Source != null &&
                                     d.Source.OriginalString.Contains("Theme.xaml") &&
                                     !d.Source.OriginalString.Contains("SharedStyles"));

            if (oldTheme != null)
            {
                var oldIndex = app.Resources.MergedDictionaries.IndexOf(oldTheme);
                app.Resources.MergedDictionaries.Remove(oldTheme);
                app.Resources.MergedDictionaries.Insert(oldIndex, themeDict);
            }
            else
            {
                // Перший запуск — вставляємо після SharedStyles
                var sharedIndex = -1;
                for (int i = 0; i < app.Resources.MergedDictionaries.Count; i++)
                {
                    var src = app.Resources.MergedDictionaries[i].Source?.OriginalString;
                    if (src != null && src.Contains("SharedStyles"))
                    {
                        sharedIndex = i;
                        break;
                    }
                }
                app.Resources.MergedDictionaries.Insert(sharedIndex + 1, themeDict);
            }
        }
    }
}