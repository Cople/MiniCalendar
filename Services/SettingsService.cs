using System.IO;
using Newtonsoft.Json;
using MiniCalendar.Models;

namespace MiniCalendar.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings? _settings;

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "MiniCalendar");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                _settings = new AppSettings();
            }
        }
        else
        {
            _settings = new AppSettings();
        }

        return _settings;
    }

    public void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(_settingsPath, json);
    }

    public AppSettings GetSettings()
    {
        return _settings ??= LoadSettings();
    }

    public void SetAutoStart(bool enable)
    {
        try
        {
            using var registryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (registryKey != null)
            {
                if (enable)
                {
                    var exePath = Environment.ProcessPath;
                    
                    if (string.IsNullOrEmpty(exePath))
                    {
                        exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            exePath = Path.ChangeExtension(exePath, ".exe");
                        }
                    }

                    if (!string.IsNullOrEmpty(exePath))
                    {
                        registryKey.SetValue("MiniCalendar", $@"""{exePath}""");
                    }
                }
                else
                {
                    registryKey.DeleteValue("MiniCalendar", false);
                }
            }
        }
        catch
        {
            // 忽略错误，避免影响主流程
        }
    }
}