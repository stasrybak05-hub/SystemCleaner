using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SystemCleaner.Services;

namespace SystemCleaner
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Перехоплюємо ВСІ необроблені помилки
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            try
            {
                // 1. Завантажуємо ВСІ налаштування ОДИН раз
                SettingsManager.LoadSettings();

                // 2. Ініціалізуємо тему і мову (читають з SettingsManager)
                ThemeManager.Initialize(this);
                LangManager.Initialize(this);

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                ShowError("Помилка ініціалізації", ex);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Зберігаємо ВСІ налаштування при виході
            SettingsManager.SaveSettings();
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ShowError("Помилка UI потоку", e.Exception);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                ShowError("Критична помилка", ex);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            ShowError("Помилка в фоновому потоці", e.Exception);
            e.SetObserved();
        }

        private void ShowError(string title, Exception ex)
        {
            var msg = $"{title}\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";

            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SystemCleaner", "error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"\n[{DateTime.Now}]\n{msg}\n\n");
            }
            catch { }

            try
            {
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }
    }
}