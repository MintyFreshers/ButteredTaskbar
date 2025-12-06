using System.Diagnostics;

namespace ButteryTaskbar;

/// <summary>
/// Application configuration
/// </summary>
public class AppConfig
{
    public bool Enabled { get; set; } = true;
    public bool ScrollActivationEnabled { get; set; } = true;
    public bool ToggleShortcutEnabled { get; set; } = false;
    public bool AutoLaunchEnabled { get; set; } = false;
    public bool AutoHideWhenDisabled { get; set; } = true;
    
    private const int ConfigVersion = 2;
    private const string ConfigFileName = "config";
    
    public void Save()
    {
        try
        {
            var dataDir = GetDataDirectory();
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            
            var configPath = Path.Combine(dataDir, ConfigFileName);
            using var writer = new BinaryWriter(File.Open(configPath, FileMode.Create));
            
            writer.Write((long)ConfigVersion);
            writer.Write(Enabled);
            writer.Write(ToggleShortcutEnabled);
            writer.Write(ScrollActivationEnabled);
            writer.Write(AutoLaunchEnabled);
            writer.Write(AutoHideWhenDisabled);
            
            // Pad to 512 bytes
            var remainingBytes = 512 - (sizeof(long) + 5 * sizeof(bool));
            writer.Write(new byte[remainingBytes]);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
    
    public void Load()
    {
        try
        {
            var configPath = Path.Combine(GetDataDirectory(), ConfigFileName);
            if (!File.Exists(configPath))
            {
                return;
            }
            
            using var reader = new BinaryReader(File.Open(configPath, FileMode.Open));
            
            var version = reader.ReadInt64();
            if (version < 1 || version > ConfigVersion)
            {
                Debug.WriteLine($"Unsupported config version: {version}");
                return;
            }
            
            Enabled = reader.ReadBoolean();
            ToggleShortcutEnabled = reader.ReadBoolean();
            ScrollActivationEnabled = reader.ReadBoolean();
            AutoLaunchEnabled = reader.ReadBoolean();
            
            if (version == 2)
            {
                AutoHideWhenDisabled = reader.ReadBoolean();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load config: {ex.Message}");
        }
    }
    
    private static string GetDataDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, AppConstants.AppName);
    }
}
