using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CryoTray.Models;
using CryoTray.Services;

namespace CryoTray.Views
{
    public partial class StartupManagerWindow : Window
    {
        public sealed record DelayOptionChoice(int Seconds, string Label);

        private sealed class StartupEntryRowViewModel : INotifyPropertyChanged
        {
            private readonly StartupManagerWindow _owner;
            private StartupEntryMode _mode;
            private int _delaySeconds;
            private int _priorityOrder;
            private bool _waitForIdle;
            private StartupDelayItemState _runtimeState;
            private string _lastError;
            private ImageSource? _icon;
            private bool _suppressCallbacks;

            public StartupItemRecord Record { get; }
            public string EntryId => Record.EntryId;
            public string Name => Record.Name;
            public string CommandLine => Record.CommandLine;
            public bool IsReadOnly => Record.IsReadOnly;
            public bool IsEditable => !Record.IsReadOnly;
            public IReadOnlyList<string> ModeOptions { get; private set; }
            public IReadOnlyList<DelayOptionChoice> DelayOptions { get; private set; }

            public string Subtitle { get; private set; } = "";
            public string ReadOnlyLabel => string.IsNullOrWhiteSpace(Record.ReadOnlyReasonKey)
                ? ""
                : LocalizationService.Get(Record.ReadOnlyReasonKey);
            public bool IsDelayed => _mode == StartupEntryMode.Delayed;
            public string PriorityLabel => $"#{_priorityOrder}";

            public ImageSource? Icon
            {
                get => _icon;
                set
                {
                    if (!ReferenceEquals(_icon, value))
                    {
                        _icon = value;
                        OnPropertyChanged();
                    }
                }
            }

            public int SelectedModeIndex
            {
                get => ModeToIndex(_mode);
                set
                {
                    StartupEntryMode target = IndexToMode(value);
                    if (_suppressCallbacks || target == _mode)
                    {
                        return;
                    }

                    _owner.HandleRowModeChanged(this, target);
                }
            }

            public int DelaySeconds
            {
                get => _delaySeconds;
                set
                {
                    int clamped = StartupManagerService.ClampDelaySeconds(value);
                    if (_suppressCallbacks || clamped == _delaySeconds)
                    {
                        return;
                    }

                    _delaySeconds = clamped;
                    Record.DelaySeconds = clamped;
                    OnPropertyChanged();
                    _owner.HandleRowDelayOptionsChanged(this);
                }
            }

            public bool WaitForIdle
            {
                get => _waitForIdle;
                set
                {
                    if (_suppressCallbacks || value == _waitForIdle)
                    {
                        return;
                    }

                    _waitForIdle = value;
                    Record.WaitForIdle = value;
                    OnPropertyChanged();
                    _owner.HandleRowDelayOptionsChanged(this);
                }
            }

            public string RuntimeStateText { get; private set; } = "";

            public StartupEntryRowViewModel(
                StartupManagerWindow owner,
                StartupItemRecord record,
                IReadOnlyList<string> modeOptions,
                IReadOnlyList<DelayOptionChoice> delayOptions)
            {
                _owner = owner;
                Record = record;
                ModeOptions = modeOptions;
                DelayOptions = delayOptions;

                _suppressCallbacks = true;
                _mode = record.Mode;
                _delaySeconds = record.DelaySeconds;
                _priorityOrder = record.PriorityOrder;
                _waitForIdle = record.WaitForIdle;
                _runtimeState = record.RuntimeState;
                _lastError = record.LastError;
                RefreshLocalizedFields(modeOptions, delayOptions);
                _suppressCallbacks = false;
            }

            public void SyncFromRecord(
                StartupItemRecord latest,
                IReadOnlyList<string> modeOptions,
                IReadOnlyList<DelayOptionChoice> delayOptions)
            {
                _suppressCallbacks = true;
                _mode = latest.Mode;
                _delaySeconds = latest.DelaySeconds;
                _priorityOrder = latest.PriorityOrder;
                _waitForIdle = latest.WaitForIdle;
                _runtimeState = latest.RuntimeState;
                _lastError = latest.LastError;
                Record.Mode = latest.Mode;
                Record.DelaySeconds = latest.DelaySeconds;
                Record.PriorityOrder = latest.PriorityOrder;
                Record.WaitForIdle = latest.WaitForIdle;
                Record.RuntimeState = latest.RuntimeState;
                Record.LastError = latest.LastError;
                RefreshLocalizedFields(modeOptions, delayOptions);
                _suppressCallbacks = false;

                OnPropertyChanged(nameof(SelectedModeIndex));
                OnPropertyChanged(nameof(IsDelayed));
                OnPropertyChanged(nameof(DelaySeconds));
                OnPropertyChanged(nameof(PriorityLabel));
                OnPropertyChanged(nameof(WaitForIdle));
            }

            public void SyncRuntimeState(StartupDelayItemState state, string error)
            {
                if (_runtimeState == state && string.Equals(_lastError, error, StringComparison.Ordinal))
                {
                    return;
                }

                _runtimeState = state;
                _lastError = error;
                Record.RuntimeState = state;
                Record.LastError = error;
                UpdateRuntimeStateText();
            }

            public void RefreshLocalizedFields(
                IReadOnlyList<string> modeOptions,
                IReadOnlyList<DelayOptionChoice> delayOptions)
            {
                ModeOptions = modeOptions;
                DelayOptions = delayOptions;

                string sourceLabel = LocalizationService.Get(Record.SourceKind switch
                {
                    StartupSourceKind.RegistryCurrentUserRun => "StartupManager_SourceHkcuRun",
                    StartupSourceKind.RegistryLocalMachineRun => "StartupManager_SourceHklmRun",
                    StartupSourceKind.RegistryLocalMachineRun32 => "StartupManager_SourceHklmRun32",
                    StartupSourceKind.StartupFolderUser => "StartupManager_SourceFolderUser",
                    StartupSourceKind.StartupFolderCommon => "StartupManager_SourceFolderCommon",
                    StartupSourceKind.ScheduledTask => "StartupManager_SourceTask",
                    _ => "Common_Unknown"
                });

                Subtitle = string.IsNullOrWhiteSpace(Record.Publisher)
                    ? sourceLabel
                    : $"{sourceLabel} · {Record.Publisher}";

                UpdateRuntimeStateText();
                OnPropertyChanged(nameof(ModeOptions));
                OnPropertyChanged(nameof(DelayOptions));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(ReadOnlyLabel));
            }

            private void UpdateRuntimeStateText()
            {
                RuntimeStateText = _runtimeState switch
                {
                    StartupDelayItemState.Pending => LocalizationService.Format("StartupManager_StatePending", _delaySeconds),
                    StartupDelayItemState.WaitingForIdle => LocalizationService.Get("StartupManager_StateWaitingIdle"),
                    StartupDelayItemState.Launching => LocalizationService.Get("StartupManager_StateLaunching"),
                    StartupDelayItemState.Launched => LocalizationService.Get("StartupManager_StateLaunched"),
                    StartupDelayItemState.AlreadyRunning => LocalizationService.Get("StartupManager_StateAlreadyRunning"),
                    StartupDelayItemState.SkippedMissing => LocalizationService.Get("StartupManager_StateSkipped"),
                    StartupDelayItemState.Cancelled => LocalizationService.Get("StartupManager_StateCancelled"),
                    StartupDelayItemState.Failed => string.IsNullOrWhiteSpace(_lastError)
                        ? LocalizationService.Get("StartupManager_StateFailed")
                        : _lastError,
                    _ => ""
                };
                OnPropertyChanged(nameof(RuntimeStateText));
            }

            private static int ModeToIndex(StartupEntryMode mode) => mode switch
            {
                StartupEntryMode.Enabled => 0,
                StartupEntryMode.Delayed => 1,
                _ => 2
            };

            private static StartupEntryMode IndexToMode(int idx) => idx switch
            {
                0 => StartupEntryMode.Enabled,
                1 => StartupEntryMode.Delayed,
                _ => StartupEntryMode.Disabled
            };

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private static readonly int[] GlobalDelayPresets = { 15, 30, 45, 60, 90, 120 };
        private static readonly int[] ItemDelayPresets = { 5, 15, 30, 45, 60, 90, 120, 180 };

        private readonly ObservableCollection<StartupEntryRowViewModel> _visibleRows = new();
        private readonly List<StartupEntryRowViewModel> _allRows = new();
        private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource? _scanCts;
        private string _activeFilter = "All";
        private bool _isBinding;
        private bool _isClosed;
        private bool _loaded;

        internal bool IsClosedForTest => _isClosed;
        internal int VisibleCountForTest => _visibleRows.Count;
        internal int AllCountForTest => _allRows.Count;

        public StartupManagerWindow()
        {
            InitializeComponent();
            TextScaleService.Attach(this);
            LocalizationService.Instance.ApplyWindow(this);
            LstStartupItems.ItemsSource = _visibleRows;

            BindGlobalPolicyControls();
            UpdateFilterButtons();

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            StartupDelayService.Instance.StatusChanged += OnQueueStatusChanged;
            Closed += OnWindowClosed;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded || _isClosed)
            {
                return;
            }

            _loaded = true;
            UpdateQueueBanner(StartupDelayService.Instance.CurrentStatus);
            await ReloadEntriesAsync();
        }

        private async Task ReloadEntriesAsync()
        {
            if (_isClosed)
            {
                return;
            }

            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();
            CancellationToken ct = _scanCts.Token;

            SetLoadingState(true);
            ClearNotice();

            try
            {
                IReadOnlyList<StartupItemRecord> items = await StartupManagerService.Instance.DiscoverAllAsync(ct);
                if (ct.IsCancellationRequested || _isClosed)
                {
                    return;
                }

                IReadOnlyList<string> modeOptions = BuildModeOptions();
                IReadOnlyList<DelayOptionChoice> delayOptions = BuildDelayOptions(ItemDelayPresets);

                _allRows.Clear();
                foreach (StartupItemRecord item in items)
                {
                    var vm = new StartupEntryRowViewModel(this, item, modeOptions, delayOptions);
                    if (!string.IsNullOrWhiteSpace(item.ExecutablePath) &&
                        _iconCache.TryGetValue(item.ExecutablePath, out ImageSource? cachedIcon))
                    {
                        vm.Icon = cachedIcon;
                    }
                    _allRows.Add(vm);
                }

                SetLoadingState(false);
                ApplyFilterAndSearch();
                ScheduleLazyIconDecode(ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetLoadingState(false);
                ShowNotice(ex.Message);
            }
        }

        private void ScheduleLazyIconDecode(CancellationToken ct)
        {
            var pending = _allRows
                .Where(r => r.Icon == null && !string.IsNullOrWhiteSpace(r.Record.ExecutablePath))
                .ToList();
            if (pending.Count == 0)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_isClosed || ct.IsCancellationRequested)
                {
                    return;
                }

                foreach (StartupEntryRowViewModel vm in pending)
                {
                    if (_isClosed || ct.IsCancellationRequested)
                    {
                        return;
                    }

                    string exePath = vm.Record.ExecutablePath;
                    if (!_iconCache.TryGetValue(exePath, out ImageSource? icon))
                    {
                        icon = ExtractSmallFrozenIcon(exePath);
                        _iconCache[exePath] = icon;
                    }

                    vm.Icon = icon;
                }
            }));
        }

        private static ImageSource? ExtractSmallFrozenIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!File.Exists(expanded))
            {
                return null;
            }

            var shfi = new NativeMethods.SHFILEINFOW();
            try
            {
                IntPtr res = NativeMethods.SHGetFileInfoW(
                    expanded,
                    0,
                    ref shfi,
                    (uint)Marshal.SizeOf<NativeMethods.SHFILEINFOW>(),
                    NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

                if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
                {
                    return null;
                }

                BitmapSource src = Imaging.CreateBitmapSourceFromHIcon(
                    shfi.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shfi.hIcon != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(shfi.hIcon);
                }
            }
        }

        private void BindGlobalPolicyControls()
        {
            _isBinding = true;
            try
            {
                AppSettings settings = SettingsService.Instance.Current;
                var choices = BuildDelayOptions(GlobalDelayPresets);
                CmbDefaultDelay.ItemsSource = choices;
                CmbDefaultDelay.DisplayMemberPath = nameof(DelayOptionChoice.Label);
                CmbDefaultDelay.SelectedValuePath = nameof(DelayOptionChoice.Seconds);

                int current = StartupManagerService.ClampDelaySeconds(settings.CryoBootDefaultDelaySeconds);
                int matchIdx = -1;
                for (int i = 0; i < choices.Count; i++)
                {
                    if (choices[i].Seconds == current)
                    {
                        matchIdx = i;
                        break;
                    }
                }
                CmbDefaultDelay.SelectedIndex = matchIdx >= 0 ? matchIdx : 2;
                ChkWaitForIdle.IsChecked = settings.CryoBootWaitForSystemIdle;
            }
            finally
            {
                _isBinding = false;
            }
        }

        private void CmbDefaultDelay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isBinding || CmbDefaultDelay.SelectedValue is not int seconds)
            {
                return;
            }

            SettingsService.Instance.Current.CryoBootDefaultDelaySeconds = StartupManagerService.ClampDelaySeconds(seconds);
            SettingsService.Instance.Save();
        }

        private void ChkWaitForIdle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isBinding)
            {
                return;
            }

            SettingsService.Instance.Current.CryoBootWaitForSystemIdle = ChkWaitForIdle.IsChecked == true;
            SettingsService.Instance.Save();
        }

        private void HandleRowModeChanged(StartupEntryRowViewModel vm, StartupEntryMode targetMode)
        {
            ClearNotice();
            if (targetMode != StartupEntryMode.Delayed)
            {
                StartupDelayService.Instance.RemovePendingEntry(vm.EntryId);
            }

            if (!StartupManagerService.Instance.TrySetEntryMode(vm.Record, targetMode, out string error))
            {
                IReadOnlyList<string> modeOptions = BuildModeOptions();
                IReadOnlyList<DelayOptionChoice> delayOptions = BuildDelayOptions(ItemDelayPresets);
                vm.SyncFromRecord(vm.Record, modeOptions, delayOptions);
                ShowNotice(error);
                return;
            }

            SyncAllRowsFromDiscoveredSettings();
            ApplyFilterAndSearch();
        }

        private void HandleRowDelayOptionsChanged(StartupEntryRowViewModel vm)
        {
            if (vm.Record.Mode != StartupEntryMode.Delayed)
            {
                return;
            }

            StartupManagerService.Instance.UpdateDelayedOptions(vm.EntryId, vm.DelaySeconds, vm.WaitForIdle);
        }

        private void SyncAllRowsFromDiscoveredSettings()
        {
            var delayedById = (SettingsService.Instance.Current.CryoBootEntries ?? new List<StartupDelaySetting>())
                .Where(d => !string.IsNullOrWhiteSpace(d.EntryId))
                .GroupBy(d => d.EntryId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<string> modeOptions = BuildModeOptions();
            IReadOnlyList<DelayOptionChoice> delayOptions = BuildDelayOptions(ItemDelayPresets);

            foreach (StartupEntryRowViewModel vm in _allRows)
            {
                if (delayedById.TryGetValue(vm.EntryId, out StartupDelaySetting? delayed))
                {
                    vm.Record.Mode = StartupEntryMode.Delayed;
                    vm.Record.DelaySeconds = delayed.DelaySeconds;
                    vm.Record.PriorityOrder = delayed.PriorityOrder;
                    vm.Record.WaitForIdle = delayed.WaitForIdle;
                }
                else if (vm.Record.Mode == StartupEntryMode.Delayed)
                {
                    vm.Record.Mode = vm.Record.NativeWindowsMode;
                }

                vm.SyncFromRecord(vm.Record, modeOptions, delayOptions);
            }
        }

        private void BtnPriorityUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string entryId } &&
                StartupManagerService.Instance.MoveDelayedPriority(entryId, -1))
            {
                SyncAllRowsFromDiscoveredSettings();
                ApplyFilterAndSearch();
            }
        }

        private void BtnPriorityDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string entryId } &&
                StartupManagerService.Instance.MoveDelayedPriority(entryId, +1))
            {
                SyncAllRowsFromDiscoveredSettings();
                ApplyFilterAndSearch();
            }
        }

        private void BtnLaunchNow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string entryId })
            {
                return;
            }

            ClearNotice();
            StartupEntryRowViewModel? vm = _allRows.FirstOrDefault(r =>
                string.Equals(r.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
            if (vm == null)
            {
                return;
            }

            if (StartupDelayService.Instance.IsQueueActive &&
                StartupDelayService.Instance.LaunchQueuedItemNow(entryId))
            {
                RefreshQueueRuntimeStates();
                return;
            }

            if (StartupManagerService.Instance.LaunchItemNow(vm.Record, out string error))
            {
                vm.SyncRuntimeState(StartupDelayItemState.Launched, "");
            }
            else
            {
                vm.SyncRuntimeState(StartupDelayItemState.Failed, error);
                ShowNotice(error);
            }
        }

        private void BtnLaunchAll_Click(object sender, RoutedEventArgs e)
        {
            ClearNotice();
            StartupDelayService.Instance.LaunchAllNow();
        }

        private void BtnCancelQueue_Click(object sender, RoutedEventArgs e)
        {
            StartupDelayService.Instance.CancelPendingQueue();
            RefreshQueueRuntimeStates();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await ReloadEntriesAsync();
        }

        private void FilterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string filter })
            {
                SetActiveFilterForTest(filter);
            }
        }

        internal void SetActiveFilterForTest(string filter)
        {
            _activeFilter = filter;
            UpdateFilterButtons();
            ApplyFilterAndSearch();
        }

        internal void SetSearchTextForTest(string query)
        {
            TxtSearch.Text = query;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(TxtSearch.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplyFilterAndSearch();
        }

        internal void ApplyFilterAndSearch()
        {
            string query = (TxtSearch?.Text ?? "").Trim();
            IEnumerable<StartupEntryRowViewModel> filtered = _allRows;

            filtered = _activeFilter switch
            {
                "Enabled" => filtered.Where(r => r.Record.Mode == StartupEntryMode.Enabled),
                "Delayed" => filtered.Where(r => r.Record.Mode == StartupEntryMode.Delayed),
                "Disabled" => filtered.Where(r => r.Record.Mode == StartupEntryMode.Disabled),
                _ => filtered
            };

            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(r =>
                    r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.CommandLine.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            List<StartupEntryRowViewModel> ordered = filtered
                .OrderBy(r => r.Record.Mode == StartupEntryMode.Delayed ? 0 : r.Record.Mode == StartupEntryMode.Enabled ? 1 : 2)
                .ThenBy(r => r.Record.PriorityOrder)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _visibleRows.Clear();
            foreach (StartupEntryRowViewModel row in ordered)
            {
                _visibleRows.Add(row);
            }

            UpdateSummaryAndFilterCounts();

            if (TxtListOverlayState != null && !_allRowsLoading())
            {
                if (_visibleRows.Count == 0)
                {
                    TxtListOverlayState.Text = LocalizationService.Get("StartupManager_EmptySearch");
                    TxtListOverlayState.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtListOverlayState.Visibility = Visibility.Collapsed;
                }
            }
        }

        private bool _allRowsLoading() =>
            BtnRefresh != null && !BtnRefresh.IsEnabled;

        private void UpdateSummaryAndFilterCounts()
        {
            int total = _allRows.Count;
            int enabled = _allRows.Count(r => r.Record.Mode == StartupEntryMode.Enabled);
            int delayed = _allRows.Count(r => r.Record.Mode == StartupEntryMode.Delayed);
            int disabled = _allRows.Count(r => r.Record.Mode == StartupEntryMode.Disabled);

            if (TxtSummary != null)
            {
                TxtSummary.Text = LocalizationService.Format(
                    "StartupManager_Summary",
                    total,
                    delayed,
                    disabled);
            }

            if (BtnFilterAll != null)
            {
                BtnFilterAll.Content = $"{LocalizationService.Get("StartupManager_FilterAll")} ({total})";
                BtnFilterEnabled.Content = $"{LocalizationService.Get("StartupManager_ModeEnabled")} ({enabled})";
                BtnFilterDelayed.Content = $"{LocalizationService.Get("StartupManager_FilterDelayed")} ({delayed})";
                BtnFilterDisabled.Content = $"{LocalizationService.Get("StartupManager_ModeDisabled")} ({disabled})";
            }
        }

        private void UpdateFilterButtons()
        {
            ApplyFilterPillStyle(BtnFilterAll, _activeFilter == "All");
            ApplyFilterPillStyle(BtnFilterEnabled, _activeFilter == "Enabled");
            ApplyFilterPillStyle(BtnFilterDelayed, _activeFilter == "Delayed");
            ApplyFilterPillStyle(BtnFilterDisabled, _activeFilter == "Disabled");
        }

        private static void ApplyFilterPillStyle(Button? btn, bool active)
        {
            if (btn == null)
            {
                return;
            }

            if (active)
            {
                btn.SetResourceReference(BackgroundProperty, "AccentButtonBrush");
                btn.SetResourceReference(ForegroundProperty, "AccentButtonTextBrush");
                btn.SetResourceReference(BorderBrushProperty, "AccentButtonBrush");
            }
            else
            {
                btn.SetResourceReference(BackgroundProperty, "CardBackgroundBrush");
                btn.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
                btn.SetResourceReference(BorderBrushProperty, "BorderStrongBrush");
            }
        }

        private void OnQueueStatusChanged(CryoBootQueueStatus status)
        {
            if (_isClosed || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosed)
                {
                    return;
                }

                UpdateQueueBanner(status);
                RefreshQueueRuntimeStates();
            }));
        }

        private void UpdateQueueBanner(CryoBootQueueStatus status)
        {
            if (BdrQueueBanner == null || TxtQueueBanner == null)
            {
                return;
            }

            if (!status.IsActive)
            {
                BdrQueueBanner.Visibility = Visibility.Collapsed;
                return;
            }

            TxtQueueBanner.Text = status.WaitingForIdle
                ? LocalizationService.Format("StartupManager_QueueIdleWait", status.RemainingCount, status.NextEntryName)
                : LocalizationService.Format("StartupManager_QueueCountdown", status.NextEntryName, status.SecondsUntilNext, status.RemainingCount);
            BdrQueueBanner.Visibility = Visibility.Visible;
        }

        private void RefreshQueueRuntimeStates()
        {
            var states = StartupDelayService.Instance.GetRuntimeStatesSnapshot();
            foreach (StartupEntryRowViewModel vm in _allRows)
            {
                if (states.TryGetValue(vm.EntryId, out var runtime))
                {
                    vm.SyncRuntimeState(runtime.State, runtime.Error);
                }
                else
                {
                    vm.SyncRuntimeState(StartupDelayItemState.None, "");
                }
            }
        }

        private void OnLanguageChanged()
        {
            if (_isClosed)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosed)
                {
                    return;
                }

                LocalizationService.Instance.ApplyWindow(this);
                BindGlobalPolicyControls();
                IReadOnlyList<string> modeOptions = BuildModeOptions();
                IReadOnlyList<DelayOptionChoice> delayOptions = BuildDelayOptions(ItemDelayPresets);
                foreach (StartupEntryRowViewModel vm in _allRows)
                {
                    vm.RefreshLocalizedFields(modeOptions, delayOptions);
                }

                UpdateSummaryAndFilterCounts();
                UpdateQueueBanner(StartupDelayService.Instance.CurrentStatus);
            }));
        }

        private void SetLoadingState(bool isLoading)
        {
            if (BtnRefresh != null)
            {
                BtnRefresh.IsEnabled = !isLoading;
            }

            if (TxtListOverlayState != null)
            {
                if (isLoading)
                {
                    TxtListOverlayState.Text = LocalizationService.Get("StartupManager_Loading");
                    TxtListOverlayState.Visibility = Visibility.Visible;
                }
                else if (_visibleRows.Count > 0)
                {
                    TxtListOverlayState.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ShowNotice(string text)
        {
            if (BdrNotice != null && TxtNotice != null && !string.IsNullOrWhiteSpace(text))
            {
                TxtNotice.Text = text;
                BdrNotice.Visibility = Visibility.Visible;
            }
        }

        private void ClearNotice()
        {
            if (BdrNotice != null)
            {
                BdrNotice.Visibility = Visibility.Collapsed;
            }
            if (TxtNotice != null)
            {
                TxtNotice.Text = "";
            }
        }

        private static IReadOnlyList<string> BuildModeOptions() => new[]
        {
            LocalizationService.Get("StartupManager_ModeEnabled"),
            LocalizationService.Get("StartupManager_ModeDelayed"),
            LocalizationService.Get("StartupManager_ModeDisabled")
        };

        private static IReadOnlyList<DelayOptionChoice> BuildDelayOptions(int[] presets) => presets
            .Select(sec => new DelayOptionChoice(
                sec,
                string.Format(CultureInfo.CurrentCulture, LocalizationService.Get("StartupManager_SecondsFormat"), sec)))
            .ToList();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                TxtSearch.Focus();
                TxtSearch.SelectAll();
                e.Handled = true;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed &&
                e.OriginalSource is DependencyObject dep &&
                !IsInteractiveDescendant(dep))
            {
                DragMove();
            }
        }

        private static bool IsInteractiveDescendant(DependencyObject dep)
        {
            while (dep != null)
            {
                if (dep is System.Windows.Controls.Primitives.ButtonBase ||
                    dep is System.Windows.Controls.Primitives.TextBoxBase ||
                    dep is ComboBox ||
                    dep is ListBoxItem)
                {
                    return true;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _isClosed = true;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;

            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            StartupDelayService.Instance.StatusChanged -= OnQueueStatusChanged;

            _visibleRows.Clear();
            _allRows.Clear();
            _iconCache.Clear();
            if (LstStartupItems != null)
            {
                LstStartupItems.ItemsSource = null;
            }
            if (CmbDefaultDelay != null)
            {
                CmbDefaultDelay.ItemsSource = null;
            }
        }
    }
}
