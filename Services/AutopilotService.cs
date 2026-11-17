using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using CryoTray.Models;

namespace CryoTray.Services
{
    public sealed class AutopilotService : IDisposable
    {
        private static AutopilotService? _instance;
        public static AutopilotService Instance => _instance ??= new AutopilotService();

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2.5);

        private readonly object _stateLock = new();
        private Timer? _timer;
        private int _polling;
        private bool _pausedByAutopilot;
        private bool _disposed;

        private AutopilotService()
        {
        }

        public void Start()
        {
            lock (_stateLock)
            {
                if (_disposed || _timer != null)
                {
                    return;
                }

                _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
            }
        }

        public void Stop()
        {
            Timer? timer;
            lock (_stateLock)
            {
                timer = _timer;
                _timer = null;
            }

            timer?.Dispose();
        }

        private void Poll()
        {
            if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0)
            {
                return;
            }

            try
            {
                Evaluate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Autopilot poll error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        private void Evaluate()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.AutopilotEnabled)
            {
                lock (_stateLock)
                {
                    _pausedByAutopilot = false;
                }
                return;
            }

            var whitelist = settings.AutopilotWhitelist ?? new List<string>();
            bool hasWhitelist = whitelist.Any(entry => !string.IsNullOrWhiteSpace(entry));
            bool isWhitelistedProcessRunning = hasWhitelist &&
                IsAnyWhitelistedProcessRunning(whitelist);

            bool pauseCooling = false;
            bool resumeCooling = false;

            lock (_stateLock)
            {
                if (isWhitelistedProcessRunning &&
                    !_pausedByAutopilot &&
                    PowerPlanService.Instance.IsCoolingModeActive)
                {
                    _pausedByAutopilot = true;
                    pauseCooling = true;
                }
                else if (!isWhitelistedProcessRunning && _pausedByAutopilot)
                {
                    _pausedByAutopilot = false;
                    resumeCooling = true;
                }
            }

            if (pauseCooling)
            {
                SetCoolingMode(false);
            }
            else if (resumeCooling)
            {
                SetCoolingMode(true);
            }
        }

        private static bool IsAnyWhitelistedProcessRunning(IReadOnlyCollection<string> whitelist)
        {
            var names = whitelist
                .Select(NormalizeProcessName)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (names.Count == 0)
            {
                return false;
            }

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Autopilot process enumeration error: {ex.Message}");
                return false;
            }

            try
            {
                foreach (var process in processes)
                {
                    if (process == null)
                    {
                        continue;
                    }

                    using (process)
                    {
                        try
                        {
                            if (process.Id == Environment.ProcessId)
                            {
                                continue;
                            }

                            if (names.Contains(NormalizeProcessName(process.ProcessName)))
                            {
                                return true;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                        }
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            return false;
        }

        private static string NormalizeProcessName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            try
            {
                return Path.GetFileNameWithoutExtension(value.Trim());
            }
            catch (ArgumentException)
            {
                return "";
            }
        }

        private static void SetCoolingMode(bool enable)
        {
            var application = System.Windows.Application.Current;
            if (application?.Dispatcher != null && !application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (PowerPlanService.Instance.IsCoolingModeActive != enable)
                    {
                        PowerPlanService.Instance.SetCoolingMode(enable);
                    }
                }));
                return;
            }

            if (PowerPlanService.Instance.IsCoolingModeActive != enable)
            {
                PowerPlanService.Instance.SetCoolingMode(enable);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }
    }
}
