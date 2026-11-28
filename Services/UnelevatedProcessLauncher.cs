using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CryoTray.Services
{
    internal interface IStartupProcessLauncher
    {
        bool TryLaunch(
            string executablePath,
            string arguments,
            string workingDirectory,
            bool preserveElevation,
            out string errorMessage);
    }

    /// <summary>
    /// Launches delayed startup programs under the interactive non-elevated Explorer
    /// user token so apps like Discord or Steam never inherit CryoTray's Administrator
    /// privileges.
    /// </summary>
    internal sealed class UnelevatedProcessLauncher : IStartupProcessLauncher
    {
        public static UnelevatedProcessLauncher Instance { get; } = new();

        public bool TryLaunch(
            string executablePath,
            string arguments,
            string workingDirectory,
            bool preserveElevation,
            out string errorMessage)
        {
            errorMessage = "";
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                errorMessage = "Target executable path is empty.";
                return false;
            }

            string resolvedTarget = Environment.ExpandEnvironmentVariables(executablePath.Trim());
            if (!File.Exists(resolvedTarget) && !Directory.Exists(resolvedTarget))
            {
                errorMessage = $"Target not found: {resolvedTarget}";
                return false;
            }

            string resolvedWorkDir = ResolveWorkingDirectory(resolvedTarget, workingDirectory);
            string resolvedArgs = string.IsNullOrWhiteSpace(arguments)
                ? ""
                : Environment.ExpandEnvironmentVariables(arguments.Trim());

            var (spawnFile, spawnArgs) = ResolveSpawnCommand(resolvedTarget, resolvedArgs);

            if (preserveElevation && StartupTaskService.IsCurrentProcessElevated())
            {
                return TryLaunchDirect(spawnFile, spawnArgs, resolvedWorkDir, out errorMessage);
            }

            return TryLaunchUsingShellToken(spawnFile, spawnArgs, resolvedWorkDir, out errorMessage);
        }

        internal static string ResolveWorkingDirectory(string resolvedTarget, string workingDirectory)
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                string expanded = Environment.ExpandEnvironmentVariables(workingDirectory.Trim());
                if (Directory.Exists(expanded))
                {
                    return expanded;
                }
            }

            string? parent = Path.GetDirectoryName(resolvedTarget);
            return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)
                ? parent
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        internal static (string FileName, string Arguments) ResolveSpawnCommand(
            string resolvedTarget,
            string arguments)
        {
            string ext = Path.GetExtension(resolvedTarget);
            if (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                string inner = string.IsNullOrEmpty(arguments)
                    ? $"\"{resolvedTarget}\""
                    : $"\"{resolvedTarget}\" {arguments}";
                return (cmdExe, $"/c \"{inner}\"");
            }

            if (Directory.Exists(resolvedTarget) ||
                ext.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
                (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                 !ext.Equals(".com", StringComparison.OrdinalIgnoreCase)))
            {
                string explorerExe = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe");
                string explorerArgs = string.IsNullOrEmpty(arguments)
                    ? $"\"{resolvedTarget}\""
                    : $"\"{resolvedTarget}\" {arguments}";
                return (explorerExe, explorerArgs);
            }

            return (resolvedTarget, arguments);
        }

        private static bool TryLaunchDirect(
            string fileName,
            string arguments,
            string workingDirectory,
            out string errorMessage)
        {
            errorMessage = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };
                using Process? process = Process.Start(psi);
                if (process == null)
                {
                    errorMessage = "Process.Start returned no process handle.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static bool TryLaunchUsingShellToken(
            string fileName,
            string arguments,
            string workingDirectory,
            out string errorMessage)
        {
            errorMessage = "";
            if (!StartupTaskService.IsCurrentProcessElevated())
            {
                return TryLaunchDirect(fileName, arguments, workingDirectory, out errorMessage);
            }

            if (!TryGetShellProcessId(out uint shellPid))
            {
                errorMessage = "Interactive Explorer shell is not ready yet.";
                return false;
            }

            IntPtr shellProcess = IntPtr.Zero;
            IntPtr shellToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr envBlock = IntPtr.Zero;
            NativeMethods.PROCESS_INFORMATION procInfo = default;

            try
            {
                shellProcess = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                    false,
                    shellPid);
                if (shellProcess == IntPtr.Zero)
                {
                    errorMessage = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }

                if (!NativeMethods.OpenProcessToken(
                        shellProcess,
                        NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_QUERY,
                        out shellToken))
                {
                    errorMessage = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }

                uint desiredAccess =
                    NativeMethods.TOKEN_ASSIGN_PRIMARY |
                    NativeMethods.TOKEN_DUPLICATE |
                    NativeMethods.TOKEN_QUERY |
                    NativeMethods.TOKEN_ADJUST_DEFAULT |
                    NativeMethods.TOKEN_ADJUST_SESSIONID;

                if (!NativeMethods.DuplicateTokenEx(
                        shellToken,
                        desiredAccess,
                        IntPtr.Zero,
                        NativeMethods.SecurityImpersonation,
                        NativeMethods.TokenPrimary,
                        out primaryToken))
                {
                    errorMessage = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }

                uint creationFlags = 0;
                if (NativeMethods.CreateEnvironmentBlock(out envBlock, primaryToken, false))
                {
                    creationFlags |= NativeMethods.CREATE_UNICODE_ENVIRONMENT;
                }

                var startupInfo = new NativeMethods.STARTUPINFOW
                {
                    cb = (uint)Marshal.SizeOf<NativeMethods.STARTUPINFOW>(),
                    lpDesktop = @"winsta0\default"
                };

                string commandLine = string.IsNullOrEmpty(arguments)
                    ? $"\"{fileName}\""
                    : $"\"{fileName}\" {arguments}";

                bool created = NativeMethods.CreateProcessWithTokenW(
                    primaryToken,
                    0,
                    fileName,
                    commandLine,
                    creationFlags,
                    envBlock,
                    workingDirectory,
                    ref startupInfo,
                    out procInfo);

                if (!created)
                {
                    errorMessage = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                if (procInfo.hThread != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(procInfo.hThread);
                }
                if (procInfo.hProcess != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(procInfo.hProcess);
                }
                if (envBlock != IntPtr.Zero)
                {
                    NativeMethods.DestroyEnvironmentBlock(envBlock);
                }
                if (primaryToken != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(primaryToken);
                }
                if (shellToken != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(shellToken);
                }
                if (shellProcess != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(shellProcess);
                }
            }
        }

        private static bool TryGetShellProcessId(out uint shellPid)
        {
            shellPid = 0;
            IntPtr shellHwnd = NativeMethods.GetShellWindow();
            if (shellHwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(shellHwnd, out shellPid);
                if (shellPid != 0)
                {
                    return true;
                }
            }

            try
            {
                int currentSession = Process.GetCurrentProcess().SessionId;
                foreach (Process proc in Process.GetProcessesByName("explorer"))
                {
                    using (proc)
                    {
                        if (proc.SessionId == currentSession && proc.Id > 0)
                        {
                            shellPid = (uint)proc.Id;
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
