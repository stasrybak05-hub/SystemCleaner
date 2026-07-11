using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SystemCleaner.Models;
using SystemCleaner.Services;

namespace SystemCleaner
{
    public partial class MainWindow : Window
    {
        private readonly JunkScanner _scanner = new();
        private readonly JunkCleaner _cleaner = new();
        private readonly SystemCleanerService _sysCleaner = new();
        private readonly StartupManager _startup = new();
        private readonly DiskAnalyzer _diskAnalyzer = new();
        private readonly RestorePointCreator _restorePoint = new();
        private readonly UninstallManager _uninstallManager = new();
        private readonly SystemMonitor _systemMonitor = new();
        
        private List<InstalledProgram> _allPrograms = new();
        private ObservableCollection<ScanCategoryResult> _categories = new();
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                CategoriesList.ItemsSource = _categories;
                LoadDrives();
                _systemMonitor.Updated += stats => Dispatcher.Invoke(() => UpdateMonitorUI(stats));
                
                // Налаштування завантажуються автоматично в App.xaml.cs
                CreateRestorePointCheckBox.IsChecked = SettingsManager.CreateRestorePoint;
            }
            catch (Exception ex)
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SystemCleaner", "crash.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"{DateTime.Now}\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}");
                
                System.Windows.MessageBox.Show(
                    $"Помилка запуску:\n\n{ex.Message}\n\nДеталі збережено в:\n{logPath}",
                    "Критична помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                Environment.Exit(1);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _systemMonitor.Dispose();
            base.OnClosed(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeSelector.SelectionChanged -= ThemeSelector_SelectionChanged;
            ThemeSelector.SelectedIndex = (int)ThemeManager.CurrentTheme;
            ThemeSelector.SelectionChanged += ThemeSelector_SelectionChanged;

            LangSelector.SelectionChanged -= LangSelector_SelectionChanged;
            LangSelector.SelectedIndex = (int)LangManager.CurrentLanguage;
            LangSelector.SelectionChanged += LangSelector_SelectionChanged;
        }

        private void LoadDrives()
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                DriveList.Items.Add(d.Name);
            }
            if (DriveList.Items.Count > 0) DriveList.SelectedIndex = 0;
        }

        // ===== ТЕМА =====
        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeSelector.SelectedIndex < 0) return;
            var theme = (ThemeManager.ThemeType)ThemeSelector.SelectedIndex;
            ThemeManager.Apply(Application.Current, theme);
        }

        // ===== МОВА =====
        private void LangSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LangSelector.SelectedIndex < 0) return;
            var lang = (LangManager.Language)LangSelector.SelectedIndex;
            LangManager.Apply(Application.Current, lang);
        }

        // ===== АНАЛІЗ =====
        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true, (string)FindResource("Status_Scanning"));
            _cts = new CancellationTokenSource();

            try
            {
                ScanProgress.IsIndeterminate = true;
                var results = await _scanner.ScanAsync(ct: _cts.Token);

                _categories.Clear();
                foreach (var r in results) _categories.Add(r);

                UpdateTotals();
                var totalSize = _categories.Sum(c => c.TotalSize);
                GlobalStatus.Text = $"Scan complete. Found {totalSize:N0} bytes.";
            }
            catch (OperationCanceledException)
            {
                GlobalStatus.Text = (string)FindResource("Status_Cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ScanProgress.IsIndeterminate = false;
                SetBusy(false, (string)FindResource("Ready"));
            }
        }

        private void UpdateTotals()
        {
            var totalSize = _categories.Sum(c => c.TotalSize);
            var totalCount = _categories.Sum(c => c.TotalCount);
            TotalSizeText.Text = FormatBytes(totalSize);
            TotalCountText.Text = totalCount.ToString("N0");
        }

        private string FormatBytes(long bytes)
        {
            double s = bytes;
            string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            int i = 0;
            while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
            return $"{s:0.00} {units[i]}";
        }

        private void CategoriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoriesList.SelectedItem is ScanCategoryResult selected)
            {
                ItemsList.ItemsSource = selected.Items;
            }
        }

        // ===== МОНІТОРИНГ СИСТЕМИ =====
        private void BtnStartMonitor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _systemMonitor.Start();
                BtnStartMonitor.IsEnabled = false;
                BtnStopMonitor.IsEnabled = true;
                GlobalStatus.Text = (string)FindResource("Monitor_Running");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося запустити моніторинг: {ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnStopMonitor_Click(object sender, RoutedEventArgs e)
        {
            _systemMonitor.Stop();
            BtnStartMonitor.IsEnabled = true;
            BtnStopMonitor.IsEnabled = false;
            GlobalStatus.Text = (string)FindResource("Monitor_Stopped");
        }

        private void UpdateMonitorUI(SystemStats stats)
        {
            CpuUsageText.Text = $"{stats.CpuUsage:0}%";
            CpuProgress.Value = stats.CpuUsage;
            CpuTempText.Text = stats.CpuTemp > 0 ? $"{stats.CpuTemp:0}°C" : "—";
            CpuNameText.Text = $"{stats.CpuName} ({stats.CpuCores} cores)";

            RamText.Text = stats.RamUsedText;
            RamPercentText.Text = $"{stats.RamPercent:0}%";
            RamProgress.Value = stats.RamPercent;

            GpuTempText.Text = stats.GpuTemp > 0 ? $"{stats.GpuTemp:0}°C" : "—";
            GpuNameText.Text = stats.GpuName;

            DiskText.Text = stats.DiskActivityText;
            NetworkText.Text = stats.NetworkText;

            OsNameText.Text = stats.OsName;
            UptimeText.Text = stats.UptimeText;
            BatteryText.Text = stats.BatteryText;
        }

        // ===== НАЛАШТУВАННЯ =====
        private void CreateRestorePointCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CreateRestorePointCheckBox.IsChecked.HasValue)
            {
                SettingsManager.CreateRestorePoint = CreateRestorePointCheckBox.IsChecked.Value;
            }
        }

        // ===== ОЧИЩЕННЯ =====
        private async void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            var selectedCount = _categories.Sum(c => c.TotalCount);
            if (selectedCount == 0)
            {
                MessageBox.Show("Спочатку виконайте аналіз системи.", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Буде видалено {selectedCount} елементів ({FormatBytes(_categories.Sum(c => c.TotalSize))}).\n\n" +
                "Перед очищенням буде створено точку відновлення системи.\n" +
                "Ви впевнені?",
                "Підтвердження очищення",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            if (SettingsManager.CreateRestorePoint)
            {
                SetBusy(true, (string)FindResource("Status_CreatingRestorePoint"));
                var rpOk = await _restorePoint.CreateAsync("System Cleaner — перед очищенням");
                if (!rpOk)
                {
                    var cont = MessageBox.Show(
                        (string)FindResource("RestorePoint_FailedContinue"),
                        (string)FindResource("Warning"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (cont != MessageBoxResult.Yes) { SetBusy(false, (string)FindResource("Status_Cancelled")); return; }
                }
            }

            SetBusy(true, (string)FindResource("Status_Cleaning"));
            _cts = new CancellationTokenSource();

            try
            {
                var list = _categories.ToList();
                var cleaned = await _cleaner.CleanAsync(list, (done, total, path) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ScanProgress.IsIndeterminate = false;
                        ScanProgress.Maximum = total;
                        ScanProgress.Value = done;
                        StatusText.Text = $"Очищено {done}/{total}";
                        GlobalStatus.Text = path;
                    });
                }, _cts.Token);

                MessageBox.Show($"Очищення завершено. Видалено: {cleaned} елементів.",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);

                BtnScan_Click(sender, e);
            }
            catch (OperationCanceledException)
            {
                GlobalStatus.Text = (string)FindResource("Status_Cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка очищення: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, (string)FindResource("Ready"));
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            SetBusy(false, (string)FindResource("Status_Cancelled"));
        }

        // ===== ДОДАТКОВЕ =====
        private async void ClearEventLogs_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true, "Очищення журналів...");
            await _sysCleaner.ClearEventLogsAsync();
            GlobalStatus.Text = "Журнали Windows очищено.";
            SetBusy(false, (string)FindResource("Ready"));
            MessageBox.Show("Журнали Windows очищено.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void FlushDns_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true, "Очищення DNS...");
            await _sysCleaner.FlushDnsAsync();
            GlobalStatus.Text = "DNS-кеш очищено.";
            SetBusy(false, (string)FindResource("Ready"));
            MessageBox.Show("DNS-кеш очищено.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearRunHistory_Click(object sender, RoutedEventArgs e)
        {
            _sysCleaner.ClearRunHistory();
            GlobalStatus.Text = "Історія запуску очищена.";
            MessageBox.Show("Історія запуску очищена.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearClipboard_Click(object sender, RoutedEventArgs e)
        {
            _sysCleaner.ClearClipboardHistory();
            GlobalStatus.Text = "Історія буфера обміну очищена.";
            MessageBox.Show("Історія буфера обміну очищена.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearSearchCache_Click(object sender, RoutedEventArgs e)
        {
            _sysCleaner.ClearSearchCache();
            GlobalStatus.Text = "Кеш Windows Search очищено.";
            MessageBox.Show("Кеш Windows Search очищено.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ===== АВТОЗАПУСК =====
        private void RefreshStartup_Click(object sender, RoutedEventArgs e)
        {
            StartupList.ItemsSource = _startup.GetStartupItems();
        }

        private void DisableStartup_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).Tag is StartupItem item)
            {
                if (_startup.DisableItem(item))
                {
                    RefreshStartup_Click(sender, e);
                    MessageBox.Show($"{item.Name} вимкнено.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void EnableStartup_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).Tag is StartupItem item)
            {
                if (_startup.EnableItem(item))
                {
                    RefreshStartup_Click(sender, e);
                    MessageBox.Show($"{item.Name} увімкнено.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        // ===== ПРОГРАМИ (Деінсталятор) =====
        private async void BtnLoadPrograms_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true, (string)FindResource("Status_LoadingPrograms"));
            BtnLoadPrograms.IsEnabled = false;

            try
            {
                _allPrograms = await Task.Run(() => _uninstallManager.GetInstalledPrograms());
                ProgramsList.ItemsSource = _allPrograms;
                UpdateProgramsStats();
                GlobalStatus.Text = $"{(string)FindResource("Programs_Loaded")}: {_allPrograms.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка завантаження: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, (string)FindResource("Ready"));
            }
        }

        private async void BtnCalculateSizes_Click(object sender, RoutedEventArgs e)
        {
            if (_allPrograms.Count == 0)
            {
                MessageBox.Show((string)FindResource("Programs_LoadFirst"),
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SetBusy(true, (string)FindResource("Status_CalculatingSizes"));
            BtnCalculateSizes.IsEnabled = false;

            try
            {
                await _uninstallManager.CalculateProgramSizesAsync(_allPrograms,
                    _ => Dispatcher.Invoke(() =>
                    {
                        ProgramsList.Items.Refresh();
                        UpdateProgramsStats();
                    }));

                GlobalStatus.Text = (string)FindResource("Status_SizesCalculated");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, (string)FindResource("Ready"));
            }
        }

        private void ProgramsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = ProgramsSearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                ProgramsList.ItemsSource = _allPrograms;
            }
            else
            {
                ProgramsList.ItemsSource = _allPrograms.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase));
            }
            UpdateProgramsStats();
        }

        private void UpdateProgramsStats()
        {
            var source = ProgramsList.ItemsSource as IEnumerable<InstalledProgram> ?? _allPrograms;
            var list = source.ToList();
            ProgramsTotalCount.Text = list.Count.ToString();
            var total = list.Sum(p => p.Size);
            ProgramsTotalSize.Text = total > 0 ? FormatBytes(total) : "—";
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (ProgramsList.SelectedItem is not InstalledProgram program)
            {
                MessageBox.Show((string)FindResource("Programs_SelectFirst"),
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"{(string)FindResource("Programs_ConfirmUninstall")}\n\n{program.Name}?",
                (string)FindResource("Programs_UninstallTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            if (string.IsNullOrWhiteSpace(program.UninstallString))
            {
                MessageBox.Show((string)FindResource("Programs_NoUninstaller"),
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_uninstallManager.Uninstall(program))
            {
                MessageBox.Show((string)FindResource("Programs_UninstallStarted"),
                    "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show((string)FindResource("Programs_UninstallFailed"),
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnForceUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (ProgramsList.SelectedItem is not InstalledProgram program)
            {
                MessageBox.Show((string)FindResource("Programs_SelectFirst"),
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"{(string)FindResource("Programs_ForceConfirm")}\n\n" +
                $"{program.Name}\n\n" +
                $"{(string)FindResource("Programs_ForceWarning")}",
                (string)FindResource("Programs_ForceTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true, (string)FindResource("Status_ForceUninstalling"));
            _cts = new CancellationTokenSource();

            try
            {
                await _uninstallManager.ForceUninstallAsync(program, _cts.Token);

                MessageBox.Show($"{program.Name} — {(string)FindResource("Programs_ForceComplete")}",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);

                BtnLoadPrograms_Click(sender, e);
            }
            catch (OperationCanceledException)
            {
                GlobalStatus.Text = (string)FindResource("Status_Cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, (string)FindResource("Ready"));
            }
        }

        // ===== АНАЛІЗ ДИСКА =====
        private CancellationTokenSource? _analysisCts;

        private async void FindLargeFiles_Click(object sender, RoutedEventArgs e)
        {
            var drives = DriveList.SelectedItems.Cast<string>().ToList();
            if (drives.Count == 0)
            {
                MessageBox.Show("Виберіть принаймні один диск.", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true, "Пошук великих файлів...");
            _analysisCts = new CancellationTokenSource();
            LargeFilesList.ItemsSource = null;

            try
            {
                var results = await _diskAnalyzer.FindLargeFilesAsync(drives,
                    f => Dispatcher.Invoke(() => GlobalStatus.Text = f),
                    _analysisCts.Token);
                LargeFilesList.ItemsSource = results;
                LargeFilesLabel.Text = $"Великі файли: {results.Count}";
                GlobalStatus.Text = $"Знайдено {results.Count} великих файлів.";
            }
            catch (OperationCanceledException)
            {
                GlobalStatus.Text = (string)FindResource("Status_Cancelled");
            }
            finally
            {
                SetBusy(false, (string)FindResource("Ready"));
            }
        }

        private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var drives = DriveList.SelectedItems.Cast<string>().ToList();
            if (drives.Count == 0)
            {
                MessageBox.Show("Виберіть принаймні один диск.", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true, "Пошук дублікатів...");
            _analysisCts = new CancellationTokenSource();
            DuplicatesList.ItemsSource = null;

            try
            {
                var results = await _diskAnalyzer.FindDuplicatesAsync(drives,
                    f => Dispatcher.Invoke(() => GlobalStatus.Text = f),
                    _analysisCts.Token);
                DuplicatesList.ItemsSource = results;
                DuplicatesLabel.Text = $"Дублікати: {results.Count} груп";
                GlobalStatus.Text = $"Знайдено {results.Count} груп дублікатів.";
            }
            catch (OperationCanceledException)
            {
                GlobalStatus.Text = (string)FindResource("Status_Cancelled");
            }
            finally
            {
                SetBusy(false, (string)FindResource("Ready"));
            }
        }

        private void BtnCancelAnalysis_Click(object sender, RoutedEventArgs e)
        {
            _analysisCts?.Cancel();
            SetBusy(false, (string)FindResource("Status_Cancelled"));
        }

        // ===== ХЕЛПЕРИ =====
        private void SetBusy(bool isBusy, string statusText)
        {
            BtnScan.IsEnabled = !isBusy;
            BtnClean.IsEnabled = !isBusy;
            BtnCancel.IsEnabled = isBusy;
            BtnFindLarge.IsEnabled = !isBusy;
            BtnFindDuplicates.IsEnabled = !isBusy;
            if (BtnLoadPrograms != null) BtnLoadPrograms.IsEnabled = !isBusy;
            if (BtnCalculateSizes != null) BtnCalculateSizes.IsEnabled = !isBusy;
            StatusText.Text = statusText;
        }
    }
}