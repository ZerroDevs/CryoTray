using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CryoTray.Models;

namespace CryoTray.Services
{
    public enum CliCommandKind
    {
        None,
        Help,
        CoolingOn,
        CoolingOff,
        CoolingToggle,
        Profile,
        RamOptimize,
        OverlayOn,
        OverlayOff,
        OverlayToggle,
        Status
    }

    public sealed record CliCommandRequest(CliCommandKind Kind, string Argument = "");

    public sealed record CliCommandResponse(int ExitCode, string Message);

    public sealed record CliParseResult(
        bool IsCliInvocation,
        bool IsValid,
        CliCommandRequest? Request,
        string ErrorMessage = "");

    /// <summary>
    /// Parses CLI automation commands (/cooling, /profile, /ram-optimize, /overlay, /status, /help)
    /// and hosts an asynchronous user-SID ACL named-pipe server for zero-overhead single-instance control.
    /// </summary>
    public sealed class CliCommandService : IDisposable
    {
        public const string DefaultPipeName = "CryoTray-Cli-Pipe-99210";

        private static readonly Lazy<CliCommandService> LazyInstance = new(() => new CliCommandService());
        public static CliCommandService Instance => LazyInstance.Value;

        private readonly object _sync = new();
        private CancellationTokenSource? _cts;
        private Func<CliCommandRequest, Task<CliCommandResponse>>? _handler;
        private Task? _listenTask;
        private bool _disposed;

        public bool IsServerRunning
        {
            get
            {
                lock (_sync)
                {
                    return _cts != null && !_cts.IsCancellationRequested && _listenTask != null;
                }
            }
        }

        private CliCommandService()
        {
        }

        public static CliParseResult TryParseArgs(string[]? args)
        {
            if (args == null || args.Length == 0)
            {
                return new CliParseResult(false, true, null);
            }

            string rawFlag = (args[0] ?? "").Trim();
            if (rawFlag.Length == 0)
            {
                return new CliParseResult(false, true, null);
            }

            // Ignore standard startup / diagnostic / uninstall flags handled elsewhere in App.OnStartup.
            if (rawFlag.Equals(StartupTaskService.AutostartArg, StringComparison.OrdinalIgnoreCase) ||
                rawFlag.Equals("/test", StringComparison.OrdinalIgnoreCase) ||
                rawFlag.Equals("/test-unit", StringComparison.OrdinalIgnoreCase) ||
                rawFlag.Equals("/test-updates", StringComparison.OrdinalIgnoreCase) ||
                rawFlag.Equals("/dump", StringComparison.OrdinalIgnoreCase) ||
                rawFlag.Equals("/uninstall-cleanup", StringComparison.OrdinalIgnoreCase))
            {
                return new CliParseResult(false, true, null);
            }

            string normalized = NormalizeFlag(rawFlag);
            switch (normalized)
            {
                case "help":
                case "h":
                case "?":
                    return new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.Help));

                case "cooling":
                {
                    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                    {
                        return new CliParseResult(true, false, null, "ERROR: /cooling requires <on|off|toggle>.");
                    }

                    string mode = args[1].Trim().ToLowerInvariant();
                    return mode switch
                    {
                        "on" or "1" or "true" => new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.CoolingOn)),
                        "off" or "0" or "false" => new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.CoolingOff)),
                        "toggle" => new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.CoolingToggle)),
                        _ => new CliParseResult(true, false, null, $"ERROR: Unknown /cooling mode '{args[1]}'. Expected on, off, or toggle.")
                    };
                }

                case "profile":
                {
                    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                    {
                        return new CliParseResult(true, false, null, "ERROR: /profile requires <key>.");
                    }

                    string profileKey = string.Join(" ", args.Skip(1)).Trim();
                    return new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.Profile, profileKey));
                }

                case "ram-optimize":
                case "ramoptimize":
                case "ram":
                    return new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.RamOptimize));

                case "overlay":
                {
                    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                    {
                        return new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.OverlayToggle));
                    }

                    string mode = args[1].Trim().ToLowerInvariant();
                    return mode switch
                    {
                        "on" or "1" or "true" => new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.OverlayOn)),
                        "off" or "0" or "false" => new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.OverlayOff)),
                        "toggle" => new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.OverlayToggle)),
                        _ => new CliParseResult(true, false, null, $"ERROR: Unknown /overlay mode '{args[1]}'. Expected on, off, or toggle.")
                    };
                }

                case "status":
                    return new CliParseResult(true, true, new CliCommandRequest(CliCommandKind.Status));

                default:
                    return new CliParseResult(true, false, null, $"ERROR: Unknown argument '{rawFlag}'. Run CryoTray.exe /help.");
            }
        }

        public static string GetUsageText()
        {
            return string.Join(Environment.NewLine,
                "CryoTray CLI Control:",
                "  /cooling <on|off|toggle>    Enable, disable, or toggle Cooling Mode",
                "  /profile <key>              Switch profile (cooling, eco, legacy, balanced, work,",
                "                              gaming-low, gaming-medium, gaming-high, gaming-ultimate,",
                "                              ext:<guid>, or pinned plan GUID / name)",
                "  /ram-optimize               Run RAM cleanup using configured CryoTray scopes",
                "  /overlay [on|off|toggle]    Show, hide, or toggle the floating overlay",
                "  /status                     Print current cooling state, profile, and telemetry",
                "  /help                       Show this help text");
        }

        public static PowerProfileDefinition? ResolveProfileForCli(string? rawKey, AppSettings? settings)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return null;
            }

            string trimmed = rawKey.Trim();
            PowerProfileDefinition? direct = PowerProfiles.Resolve(trimmed, settings);
            if (direct != null)
            {
                return direct;
            }

            string compact = NormalizeToken(trimmed);
            foreach (PowerProfileDefinition builtIn in PowerProfiles.MenuProfiles)
            {
                if (NormalizeToken(builtIn.Key) == compact ||
                    NormalizeToken(builtIn.SchemeName) == compact)
                {
                    return builtIn;
                }
            }

            if (compact is "windowsbalanced" or "default")
            {
                return PowerProfiles.BalancedProfile;
            }

            // Match pinned external plan by raw GUID or display name.
            Guid.TryParse(trimmed.Trim('{', '}'), out Guid rawGuid);
            foreach (PowerProfileDefinition ext in PowerProfiles.GetExternalProfiles(settings))
            {
                if ((rawGuid != Guid.Empty && ext.SchemeGuid == rawGuid) ||
                    string.Equals(ext.SchemeName, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeToken(ext.SchemeName) == compact)
                {
                    return ext;
                }
            }

            return null;
        }

        public void StartServer(
            Func<CliCommandRequest, Task<CliCommandResponse>> handler,
            string pipeName = DefaultPipeName)
        {
            if (_disposed || handler == null)
            {
                return;
            }

            lock (_sync)
            {
                if (_cts != null)
                {
                    return;
                }

                _handler = handler;
                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;
                _listenTask = Task.Run(() => ListenLoopAsync(pipeName, token), token);
            }
        }

        public static CliCommandResponse SendToRunningInstance(
            CliCommandRequest request,
            string pipeName = DefaultPipeName,
            int connectTimeoutMs = 1500,
            int responseTimeoutMs = 4500)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                client.Connect(connectTimeoutMs);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

                string payload = JsonSerializer.Serialize(request);
                writer.WriteLine(payload);

                using var cts = new CancellationTokenSource(responseTimeoutMs);
                string? replyLine = reader.ReadLineAsync(cts.Token).AsTask().GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(replyLine))
                {
                    return new CliCommandResponse(3, "ERROR: Empty response from running CryoTray instance.");
                }

                CliCommandResponse? response = JsonSerializer.Deserialize<CliCommandResponse>(replyLine);
                return response ?? new CliCommandResponse(3, "ERROR: Invalid response from CryoTray instance.");
            }
            catch (TimeoutException)
            {
                return new CliCommandResponse(3, "ERROR: Timed out waiting for running CryoTray instance.");
            }
            catch (Exception ex)
            {
                return new CliCommandResponse(3, $"ERROR: {ex.Message}");
            }
        }

        public static void WriteCliConsoleOutput(string message, bool isError)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                {
                    NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
                }

                if (isError)
                {
                    Console.Error.WriteLine(message);
                    Console.Error.Flush();
                }
                else
                {
                    Console.WriteLine(message);
                    Console.Out.Flush();
                }
            }
            catch
            {
            }
        }

        private async Task ListenLoopAsync(string pipeName, CancellationToken token)
        {
            PipeSecurity security = CreateCurrentUserPipeSecurity();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = NamedPipeServerStreamAcl.Create(
                        pipeName,
                        PipeDirection.InOut,
                        maxNumberOfServerInstances: 4,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        inBufferSize: 4096,
                        outBufferSize: 4096,
                        security);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    await HandleClientAsync(server, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    try
                    {
                        await Task.Delay(100, token).ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken token)
        {
            CliCommandResponse response;
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                CliCommandRequest? req = string.IsNullOrWhiteSpace(line)
                    ? null
                    : JsonSerializer.Deserialize<CliCommandRequest>(line);

                if (req == null)
                {
                    response = new CliCommandResponse(2, "ERROR: Invalid CLI payload.");
                }
                else if (_handler == null)
                {
                    response = new CliCommandResponse(3, "ERROR: CryoTray CLI handler is unavailable.");
                }
                else
                {
                    response = await _handler(req).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                response = new CliCommandResponse(3, $"ERROR: {ex.Message}");
            }

            try
            {
                if (server.IsConnected)
                {
                    using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response)).ConfigureAwait(false);
                    server.WaitForPipeDrain();
                }
            }
            catch
            {
            }
        }

        internal static PipeSecurity CreateCurrentUserPipeSecurity()
        {
            var security = new PipeSecurity();

            SecurityIdentifier? currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                security.AddAccessRule(new PipeAccessRule(
                    currentUser,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow));
            }

            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            return security;
        }

        private static string NormalizeFlag(string arg)
        {
            string trimmed = arg.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..];
            }
            else if (trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..];
            }

            return trimmed.ToLowerInvariant();
        }

        private static string NormalizeToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
            }

            return sb.ToString();
        }

        public void StopServer()
        {
            CancellationTokenSource? cts;
            lock (_sync)
            {
                cts = _cts;
                _cts = null;
                _listenTask = null;
                _handler = null;
            }

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                }
                cts.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopServer();
        }
    }
}
