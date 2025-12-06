using System.Diagnostics;
using Microsoft.Win32;

namespace ButteryTaskbar;

/// <summary>
/// Tray icon and menu UI
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly AppConfig _config;
    private readonly TaskbarManager _taskbarManager;
    private string _newerVersion = string.Empty;
    private bool _isUpToDate = true;
    
    public TrayIconManager(AppConfig config, TaskbarManager taskbarManager)
    {
        _config = config;
        _taskbarManager = taskbarManager;
        
        _contextMenu = new ContextMenuStrip();
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = AppConstants.AppName,
            ContextMenuStrip = _contextMenu
        };
        
        BuildMenu();
        
        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _contextMenu.Show(Cursor.Position);
            }
        };
        
        // Start update check
        Task.Run(CheckForUpdates);
    }
    
    private void BuildMenu()
    {
        _contextMenu.Items.Clear();
        
        // Title
        var titleItem = new ToolStripLabel($"{AppConstants.AppName} {AppConstants.AppVersion}")
        {
            Font = new Font(_contextMenu.Font, FontStyle.Bold)
        };
        _contextMenu.Items.Add(titleItem);
        
        // Build date
        var buildDate = DateTime.UtcNow;
        var dateItem = new ToolStripLabel(buildDate.ToString("d MMMM yyyy"));
        _contextMenu.Items.Add(dateItem);
        
        // Update button if available
        if (!_isUpToDate)
        {
            var updateItem = new ToolStripMenuItem($"Update to {_newerVersion}");
            updateItem.Click += (s, e) => OpenUrl(AppConstants.ReleasesUrl);
            _contextMenu.Items.Add(updateItem);
        }
        
        _contextMenu.Items.Add(new ToolStripSeparator());
        
        // Configuration options
        var enabledItem = new ToolStripMenuItem("Enabled", null, OnEnabledToggle)
        {
            Checked = _config.Enabled,
            CheckOnClick = true
        };
        _contextMenu.Items.Add(enabledItem);
        
        var toggleShortcutItem = new ToolStripMenuItem("Ctrl+Win+F11 to toggle", null, OnToggleShortcutToggle)
        {
            Checked = _config.ToggleShortcutEnabled,
            CheckOnClick = true
        };
        _contextMenu.Items.Add(toggleShortcutItem);
        
        var scrollItem = new ToolStripMenuItem("Scroll to reveal taskbar", null, OnScrollToggle)
        {
            Checked = _config.ScrollActivationEnabled,
            CheckOnClick = true
        };
        _contextMenu.Items.Add(scrollItem);
        
        var autoHideItem = new ToolStripMenuItem("Auto-hide when disabled", null, OnAutoHideToggle)
        {
            Checked = _config.AutoHideWhenDisabled,
            CheckOnClick = true
        };
        _contextMenu.Items.Add(autoHideItem);
        
        var autoLaunchItem = new ToolStripMenuItem("Start at log-in (non-admin)", null, OnAutoLaunchToggle)
        {
            Checked = _config.AutoLaunchEnabled,
            CheckOnClick = true
        };
        _contextMenu.Items.Add(autoLaunchItem);
        
        var noteLabel = new ToolStripLabel("Not recommended. Instead,")
        {
            Font = new Font(_contextMenu.Font, FontStyle.Italic),
            ForeColor = Color.Gray
        };
        _contextMenu.Items.Add(noteLabel);
        
        var noteLabel2 = new ToolStripLabel("use Task Scheduler to run")
        {
            Font = new Font(_contextMenu.Font, FontStyle.Italic),
            ForeColor = Color.Gray
        };
        _contextMenu.Items.Add(noteLabel2);
        
        var noteLabel3 = new ToolStripLabel("with 'highest privileges.'")
        {
            Font = new Font(_contextMenu.Font, FontStyle.Italic),
            ForeColor = Color.Gray
        };
        _contextMenu.Items.Add(noteLabel3);
        
        _contextMenu.Items.Add(new ToolStripSeparator());
        
        // Quit button
        var quitItem = new ToolStripMenuItem("Quit", null, OnQuit);
        _contextMenu.Items.Add(quitItem);
    }
    
    private void OnEnabledToggle(object? sender, EventArgs e)
    {
        _config.Enabled = !_config.Enabled;
        _taskbarManager.SetEnabled(_config.Enabled);
        _config.Save();
        BuildMenu();
    }
    
    private void OnToggleShortcutToggle(object? sender, EventArgs e)
    {
        _config.ToggleShortcutEnabled = !_config.ToggleShortcutEnabled;
        _taskbarManager.SetHooksAppropriately();
        _config.Save();
        BuildMenu();
    }
    
    private void OnScrollToggle(object? sender, EventArgs e)
    {
        _config.ScrollActivationEnabled = !_config.ScrollActivationEnabled;
        _taskbarManager.SetHooksAppropriately();
        _config.Save();
        BuildMenu();
    }
    
    private void OnAutoHideToggle(object? sender, EventArgs e)
    {
        _config.AutoHideWhenDisabled = !_config.AutoHideWhenDisabled;
        _config.Save();
        BuildMenu();
    }
    
    private void OnAutoLaunchToggle(object? sender, EventArgs e)
    {
        SetAutoLaunch(!_config.AutoLaunchEnabled);
        _config.Save();
        BuildMenu();
    }
    
    private void OnQuit(object? sender, EventArgs e)
    {
        Application.Exit();
    }
    
    private void SetAutoLaunch(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            
            if (key == null) return;
            
            var exePath = Application.ExecutablePath;
            var valueName = AppConstants.AppName;
            
            if (enabled)
            {
                key.SetValue(valueName, $"\"{exePath}\"");
                _config.AutoLaunchEnabled = true;
            }
            else
            {
                key.DeleteValue(valueName, false);
                _config.AutoLaunchEnabled = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set auto-launch: {ex.Message}");
        }
    }
    
    private async Task CheckForUpdates()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Buttery Taskbar {AppConstants.AppVersion}");
            
            var apiUrl = $"https://api.github.com{AppConstants.LatestReleaseApiPath}";
            var response = await client.GetStringAsync(apiUrl);
            
            // Simple JSON parsing to extract tag_name
            var tagKey = "\"tag_name\":";
            var startIdx = response.IndexOf(tagKey);
            if (startIdx >= 0)
            {
                startIdx += tagKey.Length;
                var firstQuote = response.IndexOf('"', startIdx);
                if (firstQuote >= 0)
                {
                    var secondQuote = response.IndexOf('"', firstQuote + 1);
                    if (secondQuote >= 0)
                    {
                        var tag = response.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                        _isUpToDate = tag == AppConstants.AppVersion;
                        if (!_isUpToDate)
                        {
                            _newerVersion = tag;
                            BeginInvoke(() => BuildMenu());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to check for updates: {ex.Message}");
        }
    }
    
    private void BeginInvoke(Action action)
    {
        if (_contextMenu.InvokeRequired)
        {
            _contextMenu.Invoke(action);
        }
        else
        {
            action();
        }
    }
    
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
