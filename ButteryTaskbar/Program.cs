namespace ButteryTaskbar;

static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // Ensure only one instance is running
        using var mutex = new Mutex(true, "ButteryTaskbar_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                $"{AppConstants.AppName} is already running.", 
                AppConstants.AppName, 
                MessageBoxButtons.OK, 
                MessageBoxIcon.Information);
            return;
        }
        
        ApplicationConfiguration.Initialize();
        
        // Load configuration
        var config = new AppConfig();
        config.Load();
        
        // Create taskbar manager
        var taskbarManager = new TaskbarManager(config);
        taskbarManager.Start();
        
        // Create tray icon
        using var trayIcon = new TrayIconManager(config, taskbarManager);
        
        // Create hidden window for shell hooks
        using var shellWindow = new ShellHookWindow(taskbarManager);
        shellWindow.Show();
        shellWindow.Hide();
        
        // Run the application
        Application.Run();
        
        // Cleanup
        taskbarManager.Stop();
    }
}