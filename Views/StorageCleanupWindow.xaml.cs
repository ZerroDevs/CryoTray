using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class StorageCleanupWindow : Window
    {
        private readonly ObservableCollection<StorageCleanupCategoryRow> _categories = new();
        private readonly Dictionary<StorageCleanupCategory, StorageCleanupCategoryRow> _rows = new();

        private CancellationTokenSource? _scanCts;
        private CancellationTokenSource? _cleanCts;
        private StorageCleanupScan? _lastScan;
        private string _summaryStateKey = "StorageCleanup_Scanning";
        private bool _isInitializing;
        private bool _isScanning;
        private bool _isCleaning;
        private bool _isClosed;
        private bool _scanSucceeded;
        private bool _loaded;
        private bool _allowClose;
        private bool _closePromptActive;
        private bool _confirmVisible;
        private int _scanVersion;

        internal bool IsConfirmationVisibleForTest => _confirmVisible;
        internal bool IsClosedForTest => _isClosed;
        internal Func<string, bool>? ClosePromptForTest { get; set; }

        public StorageCleanupWindow()
        {
            InitializeComponent();
            LocalizationService.Instance.ApplyWindow(this);
            CategoriesItems.ItemsSource = _categories;
            BuildRows();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        private void BuildRows()
        {
            _isInitializing = true;
            var settings = SettingsService.Instance.Current;
            foreach (var definition in StorageCleanupCatalog.All)
            {
                var row = new StorageCleanupCategoryRow
                {
                    Category = definition.Category,
                    Title = LocalizationService.Get(definition.TitleKey),
                    Description = LocalizationService.Get(definition.DescriptionKey),
                    Badge = LocalizationService.Get(definition.BadgeKey),
                    IsSelected = IsSelectedInSettings(settings, definition.Category),
                    AccentOpacity = GetAccentOpacity(definition.Category),
                    FilesText = "--"
                };
                row.PropertyChanged += CategoryRow_PropertyChanged;
                _rows[definition.Category] = row;
                _categories.Add(row);
            }

            _isInitializing = false;
            UpdateActionStates();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded || _isClosed)
            {
                return;
            }

            _loaded = true;
            UpdateBrowserHint();
            LoadDriveInfo();
            await BeginScanAsync(hideResult: true);
        }

        private async Task BeginScanAsync(bool hideResult)
        {
            if (_isClosed)
            {
                return;
            }

            HideConfirm();
            _scanCts?.Cancel();
            var cts = new CancellationTokenSource();
            _scanCts = cts;
            int version = ++_scanVersion;

            _isScanning = true;
            _scanSucceeded = false;
            _lastScan = null;
            _summaryStateKey = "StorageCleanup_Scanning";
            SetCategorySizes("...");
            SetCategoryFiles("--");
            TxtTotal.Text = "--";
            TxtTotal.Tag = null;
            if (hideResult)
            {
                HideResult();
            }

            UpdateSummary();
            BuildCategoryChart(null);
            ShowProgress(LocalizationService.Get("StorageCleanup_Scanning"));
            UpdateActionStates();

            var progress = new Progress<StorageCleanupProgress>(
                update => OnScanProgress(update, version));

            try
            {
                StorageCleanupScan scan = await StorageCleanupService.ScanAsync(cts.Token, progress);
                if (cts.IsCancellationRequested || version != _scanVersion || _isClosed)
                {
                    return;
                }

                _scanSucceeded = true;
                ApplyScan(scan);
            }
            catch (OperationCanceledException)
            {
                if (version == _scanVersion && !_isClosed)
                {
                    _isScanning = false;
                    _summaryStateKey = "StorageCleanup_ScanCanceledSummary";
                    _lastScan = null;
                    SetCategorySizes("--");
                    SetCategoryFiles("--");
                    TxtTotal.Text = "--";
                    TxtTotal.Tag = null;
                    UpdateSummary();
                    BuildCategoryChart(null);
                    ShowWarning(LocalizationService.Get("StorageCleanup_ScanCanceled"));
                }
            }
            catch
            {
                if (version == _scanVersion && !_isClosed)
                {
                    _isScanning = false;
                    _scanSucceeded = false;
                    _lastScan = null;
                    _summaryStateKey = "StorageCleanup_ScanUnavailable";
                    SetCategorySizes("--");
                    SetCategoryFiles("--");
                    TxtTotal.Text = "--";
                    TxtTotal.Tag = null;
                    UpdateSummary();
                    BuildCategoryChart(null);
                    ShowError(LocalizationService.Get("StorageCleanup_ScanFailed"));
                }
            }
            finally
            {
                if (version == _scanVersion)
                {
                    _isScanning = false;
                    HideProgress();
                    UpdateActionStates();
                }

                if (ReferenceEquals(_scanCts, cts))
                {
                    _scanCts = null;
                }

                cts.Dispose();
            }
        }

        private void ApplyScan(StorageCleanupScan scan)
        {
            _lastScan = scan;
            _summaryStateKey = "";
            foreach (StorageCleanupCategoryRow row in _rows.Values)
            {
                StorageCleanupCategoryScan categoryScan = scan.Get(row.Category);
                row.Bytes = categoryScan.Bytes;
                row.Files = categoryScan.Files;
                row.SizeText = StorageCleanupService.FormatBytes(categoryScan.Bytes);
                row.FilesText = FormatFileCount(categoryScan.Files);
            }

            HideProgress();
            UpdateSummary();
            BuildCategoryChart(scan);
            LoadDriveInfo();
        }

        private async void BtnRescan_Click(object sender, RoutedEventArgs e)
        {
            await BeginScanAsync(hideResult: true);
        }

        private async void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning || _isCleaning || _isClosed)
            {
                return;
            }

            var selected = GetSelectedCategories();
            if (selected.Count == 0)
            {
                return;
            }

            if (RequiresConfirmation(selected))
            {
                ShowConfirm(selected);
                return;
            }

            await StartCleanAsync(selected);
        }

        private async void BtnConfirmClean_Click(object sender, RoutedEventArgs e)
        {
            if (!_confirmVisible || _isScanning || _isCleaning || _isClosed)
            {
                return;
            }

            var selected = GetSelectedCategories();
            HideConfirm();
            if (selected.Count > 0)
            {
                await StartCleanAsync(selected);
            }
        }

        private void BtnConfirmBack_Click(object sender, RoutedEventArgs e)
        {
            HideConfirm();
            BtnClean.Focus();
        }

        private async Task StartCleanAsync(IReadOnlyList<StorageCleanupCategory> selected)
        {
            if (_isClosed || selected.Count == 0)
            {
                return;
            }

            HideConfirm();
            HideResult();
            var cts = new CancellationTokenSource();
            _cleanCts = cts;
            _isCleaning = true;
            TxtTotal.Text = LocalizationService.Get("StorageCleanup_Cleaning");
            TxtTotal.Tag = null;
            ShowProgress(LocalizationService.Get("StorageCleanup_Cleaning"));
            UpdateActionStates();
            var progress = new Progress<StorageCleanupProgress>(
                update => OnCleanProgress(update, cts));

            try
            {
                StorageCleanupRunResult result = await StorageCleanupService.CleanAsync(
                    selected,
                    cts.Token,
                    progress);
                if (cts.IsCancellationRequested || _isClosed)
                {
                    return;
                }

                ShowResult(result);
                _isCleaning = false;
                HideProgress();
                UpdateActionStates();
                await BeginScanAsync(hideResult: false);
            }
            catch (OperationCanceledException)
            {
                if (!_isClosed)
                {
                    _isCleaning = false;
                    _scanSucceeded = false;
                    _lastScan = null;
                    _summaryStateKey = "StorageCleanup_CleanCanceledSummary";
                    SetCategorySizes("--");
                    SetCategoryFiles("--");
                    UpdateSummary();
                    BuildCategoryChart(null);
                    ShowWarning(LocalizationService.Get("StorageCleanup_CleanCanceled"));
                }
            }
            catch
            {
                if (!_isClosed)
                {
                    _isCleaning = false;
                    _scanSucceeded = false;
                    _lastScan = null;
                    _summaryStateKey = "StorageCleanup_ScanUnavailable";
                    SetCategorySizes("--");
                    SetCategoryFiles("--");
                    UpdateSummary();
                    BuildCategoryChart(null);
                    ShowError(LocalizationService.Get("StorageCleanup_CleanFailed"));
                }
            }
            finally
            {
                if (!_isClosed)
                {
                    _isCleaning = false;
                    HideProgress();
                    UpdateActionStates();
                }

                if (ReferenceEquals(_cleanCts, cts))
                {
                    _cleanCts = null;
                }

                cts.Dispose();
            }
        }

        private List<StorageCleanupCategory> GetSelectedCategories()
        {
            return StorageCleanupCatalog.All
                .Where(definition => _rows[definition.Category].IsSelected)
                .Select(definition => definition.Category)
                .ToList();
        }

        private static bool RequiresConfirmation(
            IReadOnlyCollection<StorageCleanupCategory> selected)
        {
            return selected.Any(category => category != StorageCleanupCategory.TempFiles);
        }

        internal bool ExerciseCleanGuardForTest(
            IReadOnlyList<StorageCleanupCategory> selected)
        {
            if (_isClosed || _isScanning || _isCleaning || selected.Count == 0)
            {
                return false;
            }

            _isInitializing = true;
            try
            {
                foreach (StorageCleanupCategoryRow row in _rows.Values)
                {
                    row.IsSelected = selected.Contains(row.Category);
                }
            }
            finally
            {
                _isInitializing = false;
            }

            HideConfirm();
            if (!RequiresConfirmation(selected))
            {
                UpdateActionStates();
                return false;
            }

            ShowConfirm(selected);
            return _confirmVisible;
        }

        internal void HideConfirmationForTest()
        {
            HideConfirm();
        }

        internal bool ExerciseCancelWhileBusyForTest(
            bool cleaning,
            CancellationTokenSource cts)
        {
            if (_isClosed || _isScanning || _isCleaning)
            {
                return false;
            }

            if (cleaning)
            {
                _isCleaning = true;
                _cleanCts = cts;
            }
            else
            {
                _isScanning = true;
                _scanCts = cts;
            }

            try
            {
                BtnCancel_Click(this, new RoutedEventArgs());
            }
            finally
            {
                _isScanning = false;
                _isCleaning = false;
                _scanCts = null;
                _cleanCts = null;
                UpdateActionStates();
            }

            return _isClosed;
        }

        internal bool ExerciseCloseWhileBusyForTest(
            bool cleaning,
            CancellationTokenSource cts,
            Func<string, bool> prompt)
        {
            if (_isClosed || _isScanning || _isCleaning)
            {
                return false;
            }

            if (cleaning)
            {
                _isCleaning = true;
                _cleanCts = cts;
            }
            else
            {
                _isScanning = true;
                _scanCts = cts;
            }

            Func<string, bool>? previousPrompt = ClosePromptForTest;
            ClosePromptForTest = prompt;
            var args = new CancelEventArgs();
            try
            {
                Window_Closing(this, args);
            }
            finally
            {
                ClosePromptForTest = previousPrompt;
                _isScanning = false;
                _isCleaning = false;
                _scanCts = null;
                _cleanCts = null;
                UpdateActionStates();
            }

            return args.Cancel;
        }

        private void ShowConfirm(IReadOnlyList<StorageCleanupCategory> selected)
        {
            if (_isClosed || _isScanning || _isCleaning)
            {
                return;
            }

            HideResult();
            string names = string.Join(
                ", ",
                selected
                    .Where(category => category != StorageCleanupCategory.TempFiles)
                    .Select(category =>
                        LocalizationService.Get(StorageCleanupCatalog.Get(category).ShortTitleKey)));
            long bytes = selected.Sum(category => _rows[category].Bytes);
            TxtConfirm.Text = LocalizationService.Format(
                "StorageCleanup_ConfirmOptIn",
                StorageCleanupService.FormatBytes(bytes),
                names);
            ConfirmBanner.Visibility = Visibility.Visible;
            _confirmVisible = true;
            UpdateActionStates();
            BtnConfirmBack.Focus();
        }

        private void HideConfirm()
        {
            if (!_confirmVisible)
            {
                return;
            }

            _confirmVisible = false;
            ConfirmBanner.Visibility = Visibility.Collapsed;
            UpdateActionStates();
        }

        private void ShowProgress(string text)
        {
            TxtChartState.Visibility = Visibility.Collapsed;
            TxtPhase.Text = text;
            TxtPhase.Visibility = Visibility.Visible;
            ScanProgress.Visibility = Visibility.Visible;
        }

        private void HideProgress()
        {
            TxtPhase.Text = "";
            TxtPhase.Visibility = Visibility.Collapsed;
            ScanProgress.Visibility = Visibility.Collapsed;
        }

        private void OnScanProgress(StorageCleanupProgress progress, int version)
        {
            if (_isClosed || !_isScanning || _scanSucceeded || version != _scanVersion)
            {
                return;
            }

            UpdateProgressUi(progress);
        }

        private void OnCleanProgress(
            StorageCleanupProgress progress,
            CancellationTokenSource cts)
        {
            if (_isClosed || !_isCleaning || !ReferenceEquals(_cleanCts, cts))
            {
                return;
            }

            UpdateProgressUi(progress);
        }

        private void UpdateProgressUi(StorageCleanupProgress progress)
        {
            string category = LocalizationService.Get(
                StorageCleanupCatalog.Get(progress.Category).ShortTitleKey);
            TxtPhase.Text = LocalizationService.Format(
                progress.Phase == StorageCleanupPhase.Scanning
                    ? "StorageCleanup_PhaseScan"
                    : "StorageCleanup_PhaseClean",
                category);
            TxtPhase.Visibility = Visibility.Visible;
            ScanProgress.Visibility = Visibility.Visible;

            int current = Math.Min(
                progress.CategoriesCompleted + 1,
                Math.Max(1, progress.TotalCategories));
            TxtFilesDetail.Text = LocalizationService.Format(
                "StorageCleanup_CategoryProgress",
                current,
                progress.TotalCategories);

            if (progress.Phase == StorageCleanupPhase.Scanning)
            {
                TxtSelectedValue.Text = StorageCleanupService.FormatBytes(progress.Bytes);
                TxtFilesFound.Text = FormatCount(progress.Files);
            }
        }

        private void ShowResult(StorageCleanupRunResult result)
        {
            string message = LocalizationService.Format(
                "StorageCleanup_Result",
                StorageCleanupService.FormatBytes(result.BytesFreed),
                result.FilesDeleted,
                result.SkippedLockedFiles);
            var parts = StorageCleanupCatalog.All
                .Where(definition =>
                    result.FreedBytesByCategory.TryGetValue(definition.Category, out long bytes) &&
                    bytes > 0)
                .Select(definition =>
                    $"{StorageCleanupService.FormatBytes(result.FreedBytesByCategory[definition.Category])} {LocalizationService.Get(definition.ShortTitleKey)}")
                .ToList();
            if (parts.Count > 0)
            {
                message += Environment.NewLine + LocalizationService.Format(
                    "StorageCleanup_ResultBreakdown",
                    string.Join(", ", parts));
            }

            TxtResult.Text = message;
            TxtResult.Tag = null;
            ResultBanner.SetResourceReference(Border.BorderBrushProperty, "SuccessTextBrush");
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, "SuccessTextBrush");
            BtnResultRescan.Visibility = Visibility.Collapsed;
            ResultBanner.Visibility = Visibility.Visible;
        }

        private void ShowWarning(string message)
        {
            TxtResult.Text = message;
            TxtResult.Tag = null;
            ResultBanner.SetResourceReference(Border.BorderBrushProperty, "WarningTextBrush");
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            BtnResultRescan.Visibility = Visibility.Visible;
            ResultBanner.Visibility = Visibility.Visible;
        }

        private void ShowError(string message)
        {
            TxtResult.Text = message;
            TxtResult.Tag = null;
            ResultBanner.SetResourceReference(Border.BorderBrushProperty, "DangerTextBrush");
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, "DangerTextBrush");
            BtnResultRescan.Visibility = Visibility.Visible;
            ResultBanner.Visibility = Visibility.Visible;
        }

        private void HideResult()
        {
            BtnResultRescan.Visibility = Visibility.Collapsed;
            ResultBanner.Visibility = Visibility.Collapsed;
        }

        private async void BtnResultRescan_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning || _isCleaning || _isClosed)
            {
                return;
            }

            await BeginScanAsync(hideResult: true);
        }

        private void UpdateSummary()
        {
            if (_lastScan == null)
            {
                string state = LocalizationService.Get(
                    string.IsNullOrWhiteSpace(_summaryStateKey)
                        ? "StorageCleanup_Scanning"
                        : _summaryStateKey);
                TxtSelectedValue.Text = "--";
                TxtSelectedDetail.Text = state;
                TxtFilesFound.Text = "--";
                TxtFilesDetail.Text = state;
                TxtTotal.Text = _isCleaning
                    ? LocalizationService.Get("StorageCleanup_Cleaning")
                    : "--";
                TxtTotal.Tag = null;
                return;
            }

            var selected = _rows.Values
                .Where(row => row.IsSelected)
                .ToList();
            long selectedBytes = selected.Sum(row => row.Bytes);
            int selectedFiles = selected.Sum(row => row.Files);

            TxtSelectedValue.Text = StorageCleanupService.FormatBytes(selectedBytes);
            TxtSelectedDetail.Text = selected.Count == 0
                ? LocalizationService.Get("StorageCleanup_NoneSelected")
                : LocalizationService.Format(
                    "StorageCleanup_FilesSelected",
                    FormatCount(selectedFiles));

            int totalFiles = _lastScan.Categories.Values.Sum(category => category.Files);
            TxtFilesFound.Text = FormatCount(totalFiles);
            TxtFilesDetail.Text = LocalizationService.Format(
                "StorageCleanup_CategoriesScanned",
                _lastScan.Categories.Count);
            TxtTotal.Text = _isCleaning
                ? LocalizationService.Get("StorageCleanup_Cleaning")
                : LocalizationService.Format(
                    "StorageCleanup_SelectedOfTotal",
                    StorageCleanupService.FormatBytes(selectedBytes),
                    StorageCleanupService.FormatBytes(_lastScan.TotalBytes));
            TxtTotal.Tag = null;
        }

        private void BuildCategoryChart(StorageCleanupScan? scan)
        {
            CategoryChartBars.Children.Clear();
            CategoryChartBars.ColumnDefinitions.Clear();

            if (scan == null || scan.TotalBytes <= 0)
            {
                string stateKey = scan == null
                    ? (string.IsNullOrWhiteSpace(_summaryStateKey)
                        ? "StorageCleanup_Scanning"
                        : _summaryStateKey)
                    : "StorageCleanup_NoReclaimable";
                TxtChartState.Text = LocalizationService.Get(stateKey);
                TxtChartState.Tag = null;
                TxtChartState.Visibility = Visibility.Visible;
                return;
            }

            TxtChartState.Visibility = Visibility.Collapsed;
            foreach (var definition in StorageCleanupCatalog.All)
            {
                StorageCleanupCategoryScan categoryScan = scan.Get(definition.Category);
                if (categoryScan.Bytes <= 0)
                {
                    continue;
                }

                CategoryChartBars.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(categoryScan.Bytes, GridUnitType.Star)
                });

                StorageCleanupCategoryRow row = _rows[definition.Category];
                var bar = new Border
                {
                    Margin = new Thickness(0, 0, 1, 0),
                    Opacity = row.IsSelected ? row.AccentOpacity : 0.18,
                    ToolTip = $"{row.Title}: {row.SizeText}"
                };
                bar.SetResourceReference(Border.BackgroundProperty, "RamAccentBrush");
                Grid.SetColumn(bar, CategoryChartBars.ColumnDefinitions.Count - 1);
                CategoryChartBars.Children.Add(bar);
            }
        }

        private void LoadDriveInfo()
        {
            try
            {
                string? root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrWhiteSpace(root))
                {
                    DriveCard.Visibility = Visibility.Collapsed;
                    return;
                }

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    DriveCard.Visibility = Visibility.Collapsed;
                    return;
                }

                long total = drive.TotalSize;
                long free = drive.AvailableFreeSpace;
                string driveName = drive.Name.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

                TxtDriveLabel.Text = LocalizationService.Format(
                    "StorageCleanup_DriveLabel",
                    driveName);
                TxtDriveFree.Text = LocalizationService.Format(
                    "StorageCleanup_DriveFree",
                    StorageCleanupService.FormatBytes(free),
                    StorageCleanupService.FormatBytes(total));

                DriveUsageBar.Children.Clear();
                DriveUsageBar.ColumnDefinitions.Clear();
                if (total > 0)
                {
                    long used = Math.Max(0, total - free);
                    DriveUsageBar.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(used, GridUnitType.Star)
                    });
                    DriveUsageBar.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(Math.Max(0, free), GridUnitType.Star)
                    });

                    var usedBar = new Border { Opacity = 0.7 };
                    usedBar.SetResourceReference(Border.BackgroundProperty, "RamAccentBrush");
                    Grid.SetColumn(usedBar, 0);

                    var freeBar = new Border { Opacity = 0.38 };
                    freeBar.SetResourceReference(
                        Border.BackgroundProperty,
                        "TextTertiaryBrush");
                    Grid.SetColumn(freeBar, 1);

                    DriveUsageBar.Children.Add(usedBar);
                    DriveUsageBar.Children.Add(freeBar);
                }

                DriveCard.Visibility = Visibility.Visible;
            }
            catch
            {
                DriveCard.Visibility = Visibility.Collapsed;
            }
        }

        private void OnLanguageChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnLanguageChanged));
                return;
            }

            if (_isClosed)
            {
                return;
            }

            LocalizationService.Instance.ApplyWindow(this);
            foreach (var pair in _rows)
            {
                var definition = StorageCleanupCatalog.Get(pair.Key);
                pair.Value.Title = LocalizationService.Get(definition.TitleKey);
                pair.Value.Description = LocalizationService.Get(definition.DescriptionKey);
                pair.Value.Badge = LocalizationService.Get(definition.BadgeKey);
                if (pair.Value.FilesText != "--")
                {
                    pair.Value.FilesText = FormatFileCount(pair.Value.Files);
                }
            }

            HideConfirm();
            UpdateSummary();
            BuildCategoryChart(_lastScan);
            LoadDriveInfo();
            UpdateActionStates();
        }

        private void CategoryRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isInitializing ||
                !string.Equals(
                    e.PropertyName,
                    nameof(StorageCleanupCategoryRow.IsSelected),
                    StringComparison.Ordinal))
            {
                return;
            }

            PersistPreferences();
            HideConfirm();
            UpdateActionStates();
            UpdateBrowserHint();
            UpdateSummary();
            BuildCategoryChart(_lastScan);
        }

        private void PersistPreferences()
        {
            var settings = SettingsService.Instance.Current;
            settings.CleanTemp = _rows[StorageCleanupCategory.TempFiles].IsSelected;
            settings.CleanBrowserCache = _rows[StorageCleanupCategory.BrowserCache].IsSelected;
            settings.CleanGpuShaderCache = _rows[StorageCleanupCategory.GpuShaderCache].IsSelected;
            settings.CleanPrefetch = _rows[StorageCleanupCategory.Prefetch].IsSelected;
            SettingsService.Instance.Save();
        }

        private static bool IsSelectedInSettings(
            AppSettings settings,
            StorageCleanupCategory category)
        {
            return category switch
            {
                StorageCleanupCategory.TempFiles => settings.CleanTemp,
                StorageCleanupCategory.BrowserCache => settings.CleanBrowserCache,
                StorageCleanupCategory.GpuShaderCache => settings.CleanGpuShaderCache,
                StorageCleanupCategory.Prefetch => settings.CleanPrefetch,
                _ => false
            };
        }

        private void UpdateBrowserHint()
        {
            bool show = _rows.TryGetValue(
                    StorageCleanupCategory.BrowserCache,
                    out StorageCleanupCategoryRow? browserRow) &&
                browserRow.IsSelected &&
                StorageCleanupService.HasRunningBrowser();
            BrowserHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateActionStates()
        {
            var selected = GetSelectedCategories();
            long selectedBytes = selected.Sum(category => _rows[category].Bytes);
            foreach (StorageCleanupCategoryRow row in _rows.Values)
            {
                row.IsSelectionEnabled = !_isCleaning;
            }

            BtnRescan.IsEnabled = !_isScanning && !_isCleaning;
            BtnCancel.IsEnabled = true;
            BtnClean.IsEnabled = !_isScanning &&
                !_isCleaning &&
                !_confirmVisible &&
                _scanSucceeded &&
                selected.Count > 0;
            BtnClean.Content = _scanSucceeded && selected.Count > 0
                ? LocalizationService.Format(
                    "StorageCleanup_CleanAmount",
                    StorageCleanupService.FormatBytes(selectedBytes))
                : LocalizationService.Get("StorageCleanup_CleanSelected");
            BtnCancel.Content = _isScanning
                ? LocalizationService.Get("StorageCleanup_CancelScan")
                : _isCleaning
                    ? LocalizationService.Get("StorageCleanup_CancelCleanup")
                    : LocalizationService.Get("StorageCleanup_Close");
        }

        private void SetCategorySizes(string text)
        {
            foreach (StorageCleanupCategoryRow row in _rows.Values)
            {
                row.SizeText = text;
            }
        }

        private void SetCategoryFiles(string text)
        {
            foreach (StorageCleanupCategoryRow row in _rows.Values)
            {
                row.FilesText = text;
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (!_allowClose && (_isScanning || _isCleaning))
            {
                e.Cancel = true;
                if (_closePromptActive)
                {
                    return;
                }

                _closePromptActive = true;
                string message = LocalizationService.Get(
                    _isCleaning
                        ? "StorageCleanup_CloseCleanBody"
                        : "StorageCleanup_CloseScanBody");
                bool closeApproved = ClosePromptForTest != null
                    ? ClosePromptForTest(message)
                    : MessageBox.Show(
                        this,
                        message,
                        LocalizationService.Get("StorageCleanup_CloseBusyTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No) == MessageBoxResult.Yes;
                _closePromptActive = false;
                if (!closeApproved)
                {
                    return;
                }

                _allowClose = true;
                Close();
                return;
            }

            _isClosed = true;
            _scanCts?.Cancel();
            _cleanCts?.Cancel();
            HideProgress();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_confirmVisible)
                {
                    HideConfirm();
                    return;
                }

                Close();
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning)
            {
                BtnCancel.IsEnabled = false;
                TxtPhase.Text = LocalizationService.Get("StorageCleanup_CancelingScan");
                _scanCts?.Cancel();
                return;
            }

            if (_isCleaning)
            {
                BtnCancel.IsEnabled = false;
                TxtPhase.Text = LocalizationService.Get("StorageCleanup_CancelingCleanup");
                _cleanCts?.Cancel();
                return;
            }

            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static double GetAccentOpacity(StorageCleanupCategory category)
        {
            return category switch
            {
                StorageCleanupCategory.TempFiles => 1.0,
                StorageCleanupCategory.BrowserCache => 0.72,
                StorageCleanupCategory.GpuShaderCache => 0.52,
                StorageCleanupCategory.Prefetch => 0.34,
                _ => 1.0
            };
        }

        private static string FormatCount(int value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string FormatFileCount(int files)
        {
            return LocalizationService.Format(
                "StorageCleanup_FileCount",
                FormatCount(files));
        }
    }

    public sealed class StorageCleanupCategoryRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _sizeText = "...";
        private string _filesText = "--";
        private bool _isSelectionEnabled = true;
        private string _title = "";
        private string _description = "";
        private string _badge = "";

        public StorageCleanupCategory Category { get; init; }
        public long Bytes { get; set; }
        public int Files { get; set; }
        public double AccentOpacity { get; init; }

        public string Title
        {
            get => _title;
            set
            {
                if (_title == value)
                {
                    return;
                }

                _title = value;
                OnPropertyChanged();
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description == value)
                {
                    return;
                }

                _description = value;
                OnPropertyChanged();
            }
        }

        public string Badge
        {
            get => _badge;
            set
            {
                if (_badge == value)
                {
                    return;
                }

                _badge = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string SizeText
        {
            get => _sizeText;
            set
            {
                if (_sizeText == value)
                {
                    return;
                }

                _sizeText = value;
                OnPropertyChanged();
            }
        }

        public string FilesText
        {
            get => _filesText;
            set
            {
                if (_filesText == value)
                {
                    return;
                }

                _filesText = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelectionEnabled
        {
            get => _isSelectionEnabled;
            set
            {
                if (_isSelectionEnabled == value)
                {
                    return;
                }

                _isSelectionEnabled = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
