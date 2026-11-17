using System;
using System.IO;
using System.Text.Json;
using CryoTray.Models;

namespace CryoTray.Services
{
    public class SettingsService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoTray");
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        private static SettingsService? _instance;
        public static SettingsService Instance => _instance ??= new SettingsService();

        public AppSettings Current { get; private set; }

        public event Action? SettingsChanged;

        public SettingsService()
        {
            Current = Load();
        }

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch
            {
                // Fall back to default
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Current, options);
                File.WriteAllText(SettingsFile, json);
                SettingsChanged?.Invoke();
            }
            catch
            {
                // Ignore
            }
        }

        public void Export(string filePath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(Current, options);
            File.WriteAllText(filePath, json);
        }

        public void Import(string filePath)
        {
            string json = File.ReadAllText(filePath);
            var imported = JsonSerializer.Deserialize<AppSettings>(json);
            if (imported != null)
            {
                Current = imported;
                Save();
            }
        }

        public void Reset()
        {
            Current = new AppSettings();
            Save();
        }
    }
}
