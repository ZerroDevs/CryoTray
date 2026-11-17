using System;
using System.Diagnostics;
using System.IO;

namespace CryoTray.Services
{
    public static class StartupTaskService
    {
        private const string TaskName = "CryoTray";

        public static bool IsStartupConfigured()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{TaskName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit();
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static void SetStartup(bool enable)
        {
            try
            {
                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        exePath = Path.Combine(AppContext.BaseDirectory, "CryoTray.exe");
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/delete /tn \"{TaskName}\" /f",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupTaskService error: {ex.Message}");
            }
        }
    }
}
