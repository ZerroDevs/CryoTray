using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CryoTray.Services
{
    public enum ActivitySeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ActivityEvent
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public ActivitySeverity Severity { get; set; } = ActivitySeverity.Info;
        public string Kind { get; set; } = "";
        public string MessageKey { get; set; } = "";
        public string[] Args { get; set; } = Array.Empty<string>();
        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// Bounded local activity trail. Writes JSONL under the user's CryoTray settings folder
    /// so a support bundle can include the latest events after a crash or failed update.
    /// </summary>
    public sealed class ActivityLogService
    {
        private const int MaxEvents = 300;
        private const long MaxFileBytes = 256 * 1024;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        private static ActivityLogService? _instance;
        public static ActivityLogService Instance => _instance ??= new ActivityLogService();

        private readonly object _lock = new();
        private readonly List<ActivityEvent> _events = new();
        private readonly string _folder;
        private readonly string _filePath;

        public event Action? Changed;

        public ActivityLogService()
        {
            _folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CryoTray");
            _filePath = Path.Combine(_folder, "activity.jsonl");
            Load();
        }

        internal ActivityLogService(string folder)
        {
            _folder = folder;
            _filePath = Path.Combine(_folder, "activity.jsonl");
            Load();
        }

        public void Record(
            ActivitySeverity severity,
            string kind,
            string messageKey,
            params object?[] args)
        {
            RecordCore(severity, kind, messageKey, "", args);
        }

        public void RecordDetail(
            ActivitySeverity severity,
            string kind,
            string messageKey,
            string detail,
            params object?[] args)
        {
            RecordCore(severity, kind, messageKey, detail ?? "", args);
        }

        private void RecordCore(
            ActivitySeverity severity,
            string kind,
            string messageKey,
            string detail,
            object?[] args)
        {
            var item = new ActivityEvent
            {
                TimestampUtc = DateTime.UtcNow,
                Severity = severity,
                Kind = kind ?? "",
                MessageKey = messageKey ?? "",
                Args = (args ?? Array.Empty<object?>())
                    .Select(arg => arg?.ToString() ?? "")
                    .ToArray(),
                Detail = Sanitize(detail)
            };

            bool persisted;
            lock (_lock)
            {
                _events.Add(item);
                TrimMemory();
                persisted = TryPersistLocked();
            }

            if (persisted)
            {
                Changed?.Invoke();
            }
            else
            {
                Changed?.Invoke();
            }
        }

        public IReadOnlyList<ActivityEvent> Snapshot()
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }

        public string Format(ActivityEvent item)
        {
            if (item == null)
            {
                return "";
            }

            object?[] args = item.Args.Cast<object?>().ToArray();
            string message = LocalizationService.Format(item.MessageKey, args);
            return string.IsNullOrWhiteSpace(item.Detail)
                ? message
                : $"{message} {item.Detail}";
        }

        public string SerializeForBundle()
        {
            var builder = new StringBuilder();
            lock (_lock)
            {
                foreach (ActivityEvent item in _events)
                {
                    builder.AppendLine(JsonSerializer.Serialize(item, JsonOptions));
                }
            }

            return builder.ToString();
        }

        public string FormatForBundle()
        {
            var builder = new StringBuilder();
            lock (_lock)
            {
                foreach (ActivityEvent item in _events)
                {
                    builder.Append(item.TimestampUtc.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                    builder.Append(" [");
                    builder.Append(item.Severity);
                    builder.Append("] ");
                    builder.AppendLine(Format(item));
                }
            }

            return builder.ToString();
        }

        public void Clear()
        {
            lock (_lock)
            {
                _events.Clear();
                try
                {
                    if (File.Exists(_filePath))
                    {
                        File.Delete(_filePath);
                    }
                }
                catch
                {
                }
            }

            Changed?.Invoke();
        }

        public static string Sanitize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string result = value;
            result = ReplacePath(result, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
            result = ReplacePath(result, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%");
            result = ReplacePath(result, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%");
            return result;
        }

        private static string ReplacePath(string text, string? path, string replacement)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return text;
            }

            return text.Replace(path, replacement, StringComparison.OrdinalIgnoreCase);
        }

        private void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_filePath))
                    {
                        return;
                    }

                    foreach (string line in File.ReadLines(_filePath))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        try
                        {
                            ActivityEvent? item = JsonSerializer.Deserialize<ActivityEvent>(line);
                            if (item != null && !string.IsNullOrWhiteSpace(item.MessageKey))
                            {
                                _events.Add(item);
                            }
                        }
                        catch
                        {
                            // Ignore a truncated or malformed line from a previous crash.
                        }
                    }

                    TrimMemory();
                }
                catch
                {
                }
            }
        }

        private void TrimMemory()
        {
            if (_events.Count > MaxEvents)
            {
                _events.RemoveRange(0, _events.Count - MaxEvents);
            }
        }

        private bool TryPersistLocked()
        {
            try
            {
                Directory.CreateDirectory(_folder);
                if (File.Exists(_filePath) && new FileInfo(_filePath).Length > MaxFileBytes)
                {
                    RewriteFileLocked();
                    return true;
                }

                ActivityEvent? latest = _events.LastOrDefault();
                if (latest == null)
                {
                    return false;
                }

                File.AppendAllText(
                    _filePath,
                    JsonSerializer.Serialize(latest, JsonOptions) + Environment.NewLine,
                    new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RewriteFileLocked()
        {
            var builder = new StringBuilder();
            foreach (ActivityEvent item in _events)
            {
                builder.AppendLine(JsonSerializer.Serialize(item, JsonOptions));
            }

            File.WriteAllText(_filePath, builder.ToString(), new UTF8Encoding(false));
        }
    }
}
