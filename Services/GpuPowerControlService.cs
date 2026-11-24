using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryoTray.Models;
using CryoTray.Services.GpuPower;

namespace CryoTray.Services
{
    /// <summary>
    /// Serializes every vendor call on a background thread, never writes unless the
    /// opt-in setting is enabled, and restores the VBIOS default on disable and exit.
    /// </summary>
    public sealed class GpuPowerControlService : IDisposable
    {
        private static readonly Lazy<GpuPowerControlService> LazyInstance = new(() => new GpuPowerControlService());
        public static GpuPowerControlService Instance => LazyInstance.Value;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Func<IGpuPowerBackend?> _backendFactory;
        private IGpuPowerBackend? _backend;
        private string _backendKey = "";
        private bool _hasWritten;
        private bool _startupApplied;
        private bool _disposed;

        private GpuPowerControlState _state = GpuPowerControlState.Initial;

        public GpuPowerControlState State => _state;
        public event Action<GpuPowerControlState>? StateChanged;

        internal GpuPowerControlService(Func<IGpuPowerBackend?>? backendFactory = null)
        {
            _backendFactory = backendFactory ?? CreateSystemBackend;
            _state = _state with { IsEnabled = SettingsService.Instance.Current.GpuPowerControlEnabled };
        }

        public async Task<bool> RefreshAsync()
        {
            return await RunExclusiveAsync(async () =>
            {
                EnsureBackend();
                if (_backend == null || _backend.Bounds == null)
                {
                    return false;
                }

                if (!_backend.TryReadPercent(out double current, out string readError))
                {
                    SetState(_state with
                    {
                        IsSupported = true,
                        CurrentPercent = _state.CurrentPercent,
                        Detail = readError
                    });
                    return false;
                }

                GpuPowerBounds bounds = _backend.Bounds;
                var settings = SettingsService.Instance.Current;
                bool enabled = settings.GpuPowerControlEnabled;

                if (enabled && !_startupApplied)
                {
                    _startupApplied = true;
                    double target = settings.GpuReapplyPowerLimitOnStartup
                        ? settings.GpuLastPowerLimitPercent
                        : bounds.DefaultPercent;
                    if (!TryApplyLocked(target, applyDefaultWhenFull: true, out string applyError))
                    {
                        SetState(_state with
                        {
                            IsSupported = true,
                            IsEnabled = true,
                            CurrentPercent = current,
                            MinPercent = bounds.MinPercent,
                            DefaultPercent = bounds.DefaultPercent,
                            MaxPercent = bounds.MaxPercent,
                            Detail = applyError
                        });
                        return false;
                    }

                    if (!_backend.TryReadPercent(out current, out _))
                    {
                        current = GpuPowerMath.Clamp(bounds, target);
                    }
                }

                SetState(new GpuPowerControlState(
                    true,
                    false,
                    enabled,
                    current,
                    bounds.MinPercent,
                    bounds.DefaultPercent,
                    bounds.MaxPercent,
                    CurrentGpuName(),
                    ""));
                return true;
            });
        }

        public async Task<bool> EnableAsync()
        {
            bool result = await RunExclusiveAsync(async () =>
            {
                var settings = SettingsService.Instance.Current;
                EnsureBackend();
                if (_backend == null || _backend.Bounds == null)
                {
                    SetEnabledSetting(false);
                    SetState(_state with { IsSupported = false, IsEnabled = false, Detail = "unsupported" });
                    return false;
                }

                GpuPowerBounds bounds = _backend.Bounds;
                // Explicit opt-in always starts from the native VBIOS default. The
                // saved preset is only re-applied at startup when the user opted in.
                if (!TryApplyLocked(bounds.DefaultPercent, applyDefaultWhenFull: true, out string error))
                {
                    SetEnabledSetting(false);
                    SetState(_state with
                    {
                        IsSupported = true,
                        IsEnabled = false,
                        MinPercent = bounds.MinPercent,
                        DefaultPercent = bounds.DefaultPercent,
                        MaxPercent = bounds.MaxPercent,
                        Detail = error
                    });
                    return false;
                }

                _startupApplied = true;
                SetEnabledSetting(true);
                double current = _backend.TryReadPercent(out double readback, out _)
                    ? readback
                    : bounds.DefaultPercent;
                SetState(new GpuPowerControlState(
                    true,
                    false,
                    true,
                    current,
                    bounds.MinPercent,
                    bounds.DefaultPercent,
                    bounds.MaxPercent,
                    CurrentGpuName(),
                    ""));
                return true;
            });

            return result;
        }

        public async Task DisableAsync()
        {
            await RunExclusiveAsync(async () =>
            {
                // Restore when this session wrote a limit, and also when the feature
                // is still enabled so an external tuning tool cannot leave a cap behind.
                bool shouldRestore = _hasWritten ||
                    SettingsService.Instance.Current.GpuPowerControlEnabled;
                if (shouldRestore && _backend != null && _backend.Bounds != null)
                {
                    bool restored = _backend.TryRestoreDefault(out string restoreError);
                    if (restored)
                    {
                        _hasWritten = false;
                    }
                    else if (!string.IsNullOrWhiteSpace(restoreError) && _backend.Bounds != null)
                    {
                        SetState(_state with { Detail = restoreError });
                    }
                }

                SetEnabledSetting(false);
                if (_backend?.Bounds != null)
                {
                    GpuPowerBounds bounds = _backend.Bounds;
                    SetState(new GpuPowerControlState(
                        true,
                        false,
                        false,
                        bounds.DefaultPercent,
                        bounds.MinPercent,
                        bounds.DefaultPercent,
                        bounds.MaxPercent,
                        CurrentGpuName(),
                        ""));
                }
                else
                {
                    SetState(_state with { IsEnabled = false });
                }

                return true;
            });
        }

        public async Task<bool> ApplyPercentAsync(double percent)
        {
            return await RunExclusiveAsync(async () =>
            {
                if (!SettingsService.Instance.Current.GpuPowerControlEnabled)
                {
                    return false;
                }

                EnsureBackend();
                if (_backend?.Bounds == null)
                {
                    return false;
                }

                if (!TryApplyLocked(percent, applyDefaultWhenFull: true, out string error))
                {
                    SetState(_state with { Detail = error, IsBusy = false });
                    return false;
                }

                if (_backend.TryReadPercent(out double applied, out _))
                {
                    SetState(_state with
                    {
                        CurrentPercent = applied,
                        Detail = ""
                    });
                }

                var settings = SettingsService.Instance.Current;
                settings.GpuLastPowerLimitPercent = _state.CurrentPercent;
                SettingsService.Instance.SaveWithoutNotification();
                return true;
            });
        }

        public async Task<bool> ResetAsync()
        {
            return await RunExclusiveAsync(async () =>
            {
                if (!SettingsService.Instance.Current.GpuPowerControlEnabled || _backend?.Bounds == null)
                {
                    return false;
                }

                if (!_backend.TryRestoreDefault(out string error))
                {
                    SetState(_state with { Detail = error, IsBusy = false });
                    return false;
                }

                _hasWritten = false;
                double current = _backend.TryReadPercent(out double readback, out _)
                    ? readback
                    : _backend.Bounds.DefaultPercent;
                SetState(_state with
                {
                    CurrentPercent = current,
                    Detail = ""
                });

                var settings = SettingsService.Instance.Current;
                settings.GpuLastPowerLimitPercent = _backend.Bounds.DefaultPercent;
                SettingsService.Instance.SaveWithoutNotification();
                return true;
            });
        }

        /// <summary>
        /// Synchronous, bounded restore for OnExit, unhandled exceptions, and session
        /// ending. It does nothing when the feature is disabled or was never written.
        /// </summary>
        public void RestoreForExit()
        {
            if (_disposed || !_hasWritten)
            {
                return;
            }

            bool entered = false;
            try
            {
                entered = _gate.Wait(TimeSpan.FromSeconds(3));
                if (!entered || _backend == null)
                {
                    return;
                }

                _backend.TryRestoreDefault(out _);
                _hasWritten = false;
            }
            catch
            {
            }
            finally
            {
                if (entered)
                {
                    _gate.Release();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            RestoreForExit();
            _disposed = true;
            try
            {
                _backend?.Dispose();
            }
            catch
            {
            }
            _backend = null;
            _gate.Dispose();
        }

        private bool TryApplyLocked(double percent, bool applyDefaultWhenFull, out string error)
        {
            error = "";
            if (_backend?.Bounds == null)
            {
                error = "GPU power control is not initialized.";
                return false;
            }

            GpuPowerBounds bounds = _backend.Bounds;
            double target = GpuPowerMath.Clamp(bounds, percent);
            bool useDefault = applyDefaultWhenFull && target >= bounds.DefaultPercent - 0.01;
            if (useDefault)
            {
                if (!_backend.TryRestoreDefault(out error))
                {
                    return false;
                }
                _hasWritten = target < 100.0 || bounds.DefaultPercent < 100.0;
                return true;
            }

            if (!_backend.TryApplyPercent(target, out _, out error))
            {
                return false;
            }

            _hasWritten = true;
            return true;
        }

        private async Task<bool> RunExclusiveAsync(Func<Task<bool>> action)
        {
            if (_disposed)
            {
                return false;
            }

            SetState(_state with { IsBusy = true });
            bool entered = false;
            try
            {
                entered = await _gate.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (!entered)
                {
                    SetState(_state with { IsBusy = false, Detail = "The GPU driver did not respond." });
                    return false;
                }

                bool result = await Task.Run(action).ConfigureAwait(false);
                SetState(_state with { IsBusy = false });
                return result;
            }
            catch (Exception ex)
            {
                SetState(_state with { IsBusy = false, Detail = ex.Message });
                return false;
            }
            finally
            {
                if (entered)
                {
                    _gate.Release();
                }
            }
        }

        private void EnsureBackend()
        {
            var snapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            GpuTelemetry? gpu = HardwareMonitorService.SelectPrimaryGpu(
                snapshot.Gpus,
                SettingsService.Instance.Current.PreferredGpuIdentifier);

            string key = gpu == null
                ? ""
                : $"{gpu.Vendor}|{gpu.Name}|{gpu.Identifier}";
            if (_backend != null && string.Equals(key, _backendKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                _backend?.Dispose();
            }
            catch
            {
            }
            _backend = null;
            _backendKey = key;

            if (gpu == null)
            {
                if (SettingsService.Instance.Current.GpuPowerControlEnabled)
                {
                    SetEnabledSetting(false);
                }

                SetState(_state with
                {
                    IsSupported = false,
                    IsBusy = false,
                    IsEnabled = false,
                    MinPercent = double.NaN,
                    DefaultPercent = double.NaN,
                    MaxPercent = double.NaN,
                    CurrentPercent = double.NaN,
                    GpuName = "",
                    Detail = "No GPU telemetry is available yet."
                });
                return;
            }

            IGpuPowerBackend? backend = _backendFactory();
            if (backend == null)
            {
                if (SettingsService.Instance.Current.GpuPowerControlEnabled)
                {
                    SetEnabledSetting(false);
                }

                SetState(_state with
                {
                    IsSupported = false,
                    IsBusy = false,
                    IsEnabled = false,
                    GpuName = gpu.Name,
                    Detail = IsKnownGpuVendor(gpu.Vendor)
                        ? "Power limit control not supported by this GPU's VBIOS"
                        : "This GPU vendor does not expose an adjustable board power range."
                });
                return;
            }

            _backend = backend;
            if (backend.Bounds == null)
            {
                if (SettingsService.Instance.Current.GpuPowerControlEnabled)
                {
                    SetEnabledSetting(false);
                }

                SetState(_state with
                {
                    IsSupported = false,
                    IsBusy = false,
                    IsEnabled = false,
                    GpuName = gpu.Name,
                    Detail = "Power limit control not supported by this GPU's VBIOS"
                });
                return;
            }

            GpuPowerBounds bounds = backend.Bounds;
            SetState(new GpuPowerControlState(
                true,
                false,
                SettingsService.Instance.Current.GpuPowerControlEnabled,
                bounds.DefaultPercent,
                bounds.MinPercent,
                bounds.DefaultPercent,
                bounds.MaxPercent,
                gpu.Name,
                ""));
        }

        private IGpuPowerBackend? CreateSystemBackend()
        {
            var snapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            GpuTelemetry? gpu = HardwareMonitorService.SelectPrimaryGpu(
                snapshot.Gpus,
                SettingsService.Instance.Current.PreferredGpuIdentifier);
            if (gpu == null)
            {
                return null;
            }

            try
            {
                if (string.Equals(gpu.Vendor, "Nvidia", StringComparison.OrdinalIgnoreCase))
                {
                    return new NvapiPowerBackend(gpu.Name);
                }
                if (string.Equals(gpu.Vendor, "AMD", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        return new AdlxPowerBackend(gpu.Name);
                    }
                    catch
                    {
                        // Older driver packages expose only ADL OverdriveN.
                    }

                    return new AdlPowerBackend(gpu.Name);
                }
            }
            catch
            {
            }

            return null;
        }

        private static string CurrentGpuName()
        {
            var snapshot = HardwareMonitorService.Instance.CurrentSnapshot;
            GpuTelemetry? gpu = HardwareMonitorService.SelectPrimaryGpu(
                snapshot.Gpus,
                SettingsService.Instance.Current.PreferredGpuIdentifier);
            return gpu?.Name ?? "";
        }

        private static bool IsKnownGpuVendor(string? vendor)
        {
            return string.Equals(vendor, "Nvidia", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(vendor, "AMD", StringComparison.OrdinalIgnoreCase);
        }

        private void SetEnabledSetting(bool enabled)
        {
            var settings = SettingsService.Instance.Current;
            if (settings.GpuPowerControlEnabled == enabled)
            {
                return;
            }

            settings.GpuPowerControlEnabled = enabled;
            SettingsService.Instance.SaveWithoutNotification();
        }

        private void SetState(GpuPowerControlState state)
        {
            _state = state;
            try
            {
                StateChanged?.Invoke(state);
            }
            catch
            {
            }
        }
    }
}
