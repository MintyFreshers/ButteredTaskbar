using System.Runtime.InteropServices;

namespace ButteryTaskbar;

/// <summary>
/// Hidden window for receiving shell hook messages
/// </summary>
public class ShellHookWindow : Form
{
    private readonly TaskbarManager _taskbarManager;
    private uint _shellHookMessage;
    private uint _taskbarCreatedMessage;
    
    public ShellHookWindow(TaskbarManager taskbarManager)
    {
        _taskbarManager = taskbarManager;
        
        // Make window hidden
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-10000, -10000);
        Size = new Size(0, 0);
        
        // Register shell hook
        Load += OnLoad;
    }
    
    private void OnLoad(object? sender, EventArgs e)
    {
        _shellHookMessage = NativeMethods.RegisterWindowMessage("SHELLHOOK");
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        
        NativeMethods.RegisterShellHookWindow(Handle);
    }
    
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _shellHookMessage)
        {
            var wParam = (uint)m.WParam.ToInt32();
            
            if (wParam == NativeMethods.HSHELL_WINDOWACTIVATED ||
                wParam == NativeMethods.HSHELL_RUDEAPPACTIVATED)
            {
                var hwnd = m.LParam;
                _taskbarManager.OnWindowActivated(hwnd);
            }
        }
        else if (m.Msg == _taskbarCreatedMessage)
        {
            // Taskbar was recreated (e.g., explorer.exe restarted)
            _taskbarManager.SetHooksAppropriately();
        }
        
        base.WndProc(ref m);
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _taskbarManager?.Dispose();
        }
        base.Dispose(disposing);
    }
}
