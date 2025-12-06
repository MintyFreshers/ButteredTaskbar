using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ButteryTaskbar;

/// <summary>
/// Manages taskbar visibility and Windows hooks
/// </summary>
public class TaskbarManager : IDisposable
{
    private readonly AppConfig _config;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    
    private bool _isWinKeyDown;
    private bool _shouldShowTaskbarDueToFocus = true;
    private long _shouldStayVisibleUntil;
    private readonly List<IntPtr> _taskbarHandles = new();
    private readonly List<IntPtr> _coreWindows = new();
    private NativeMethods.RECT _primaryMonitorRect;
    
    private readonly CancellationTokenSource _cts = new();
    private Task? _taskbarUpdateTask;
    private bool _stopPolling;
    private bool _winKeyPressRequested;
    
    public TaskbarManager(AppConfig config)
    {
        _config = config;
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
        
        RefreshMonitorRect();
    }
    
    public void Start()
    {
        SetHooksAppropriately();
        _taskbarUpdateTask = Task.Run(TaskbarUpdateLoop, _cts.Token);
    }
    
    public void Stop()
    {
        _cts.Cancel();
        UnhookWindowsHookEx();
        _shouldShowTaskbarDueToFocus = true;
        SetTaskbarVisibility();
        _taskbarUpdateTask?.Wait();
    }
    
    public void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        if (enabled)
        {
            RefreshTaskbarState();
        }
        else
        {
            _shouldShowTaskbarDueToFocus = true;
        }
        SetTaskbarVisibility();
        SetHooksAppropriately();
    }
    
    public void OnWindowActivated(IntPtr hwnd)
    {
        if (!_config.Enabled) return;
        
        var className = GetWindowClassName(hwnd);
        
        if (className == "Windows.UI.Core.CoreWindow")
        {
            if (!_coreWindows.Contains(hwnd))
            {
                _coreWindows.Add(hwnd);
            }
        }
        
        RefreshTaskbarState();
        SetTaskbarVisibility();
    }
    
    public void SetHooksAppropriately()
    {
        bool shouldHookKeyboard = _config.Enabled || _config.ToggleShortcutEnabled;
        bool shouldHookMouse = _config.Enabled && _config.ScrollActivationEnabled;
        
        if ((_keyboardHook != IntPtr.Zero) != shouldHookKeyboard)
        {
            if (shouldHookKeyboard)
            {
                var moduleHandle = NativeMethods.GetModuleHandle(null);
                _keyboardHook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL, 
                    _keyboardProc, 
                    moduleHandle, 
                    0);
            }
            else
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
        }
        
        if ((_mouseHook != IntPtr.Zero) != shouldHookMouse)
        {
            if (shouldHookMouse)
            {
                var moduleHandle = NativeMethods.GetModuleHandle(null);
                _mouseHook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_MOUSE_LL, 
                    _mouseProc, 
                    moduleHandle, 
                    0);
            }
            else
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }
    }
    
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var vkCode = hookStruct.vkCode;
            
            if (wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
            {
                if (HandleKeyDown(vkCode))
                {
                    return (IntPtr)1;
                }
            }
            else if (wParam == (IntPtr)NativeMethods.WM_KEYUP)
            {
                if ((vkCode == NativeMethods.VK_LWIN || vkCode == NativeMethods.VK_RWIN) && _config.Enabled)
                {
                    var otherKey = vkCode == NativeMethods.VK_LWIN ? NativeMethods.VK_RWIN : NativeMethods.VK_LWIN;
                    _isWinKeyDown = (NativeMethods.GetKeyState(otherKey) & 0xF0) != 0;
                    _shouldStayVisibleUntil = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 400;
                    SetTaskbarVisibility();
                }
            }
        }
        
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }
    
    private bool HandleKeyDown(uint vkCode)
    {
        if (vkCode == NativeMethods.VK_LWIN || vkCode == NativeMethods.VK_RWIN)
        {
            if (_config.Enabled && !_isWinKeyDown)
            {
                _isWinKeyDown = true;
                SetTaskbarVisibility();
            }
        }
        else if (vkCode == NativeMethods.VK_F11)
        {
            bool shift = (NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0xF0) != 0;
            bool ctrl = (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0xF0) != 0;
            bool win = (NativeMethods.GetKeyState(NativeMethods.VK_LWIN) & 0xF0) != 0 ||
                      (NativeMethods.GetKeyState(NativeMethods.VK_RWIN) & 0xF0) != 0;
            
            if (win && ctrl && !shift && _config.ToggleShortcutEnabled)
            {
                SetEnabled(!_config.Enabled);
                _config.Save();
                return true;
            }
        }
        
        return false;
    }
    
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_MOUSEWHEEL)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var delta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
            
            if (delta != 0 && HandleMouseScroll(delta, hookStruct.pt.X, hookStruct.pt.Y))
            {
                return (IntPtr)1;
            }
        }
        
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
    
    private bool HandleMouseScroll(int delta, int mouseX, int mouseY)
    {
        if (mouseY == _primaryMonitorRect.Bottom - 1 &&
            mouseX >= _primaryMonitorRect.Left &&
            mouseX < _primaryMonitorRect.Right &&
            !_shouldShowTaskbarDueToFocus)
        {
            _shouldShowTaskbarDueToFocus = true;
            _winKeyPressRequested = true;
            SetTaskbarVisibility();
            return true;
        }
        
        return false;
    }
    
    private void RefreshTaskbarState()
    {
        bool shouldShow = false;
        var activehWnd = NativeMethods.GetForegroundWindow();
        
        foreach (var hwnd in _coreWindows)
        {
            if (hwnd == activehWnd)
            {
                shouldShow = true;
                break;
            }
        }
        
        _shouldShowTaskbarDueToFocus = shouldShow;
    }
    
    private void SetTaskbarVisibility()
    {
        _stopPolling = true;
        // Signal the taskbar update task
    }
    
    private async Task TaskbarUpdateLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            RefreshTaskbars();
            RefreshMonitorRect();
            
            if (_winKeyPressRequested)
            {
                // Simulate Win key press
                var input = new NativeMethods.INPUT
                {
                    type = 1, // KEYBOARD
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = NativeMethods.VK_LWIN,
                        dwFlags = 0
                    }
                };
                NativeMethods.SendInput(1, ref input, Marshal.SizeOf<NativeMethods.INPUT>());
                
                input.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
                NativeMethods.SendInput(1, ref input, Marshal.SizeOf<NativeMethods.INPUT>());
                
                _winKeyPressRequested = false;
            }
            
            if (_taskbarHandles.Count > 0)
            {
                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = _taskbarHandles[0],
                    lParam = (_config.Enabled || _config.AutoHideWhenDisabled) 
                        ? (IntPtr)NativeMethods.ABS_AUTOHIDE 
                        : IntPtr.Zero
                };
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref abd);
            }
            
            _stopPolling = false;
            int attempts = 0;
            
            while (attempts < 60 && !_stopPolling && !_cts.Token.IsCancellationRequested)
            {
                bool shouldShow = _shouldShowTaskbarDueToFocus || 
                                _isWinKeyDown || 
                                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _shouldStayVisibleUntil;
                
                bool failed = false;
                foreach (var hwnd in _taskbarHandles)
                {
                    NativeMethods.ShowWindow(hwnd, shouldShow 
                        ? NativeMethods.SW_SHOWNOACTIVATE 
                        : NativeMethods.SW_HIDE);
                    
                    bool actuallyShown = NativeMethods.IsWindowVisible(hwnd);
                    failed = shouldShow != actuallyShown;
                }
                
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _shouldStayVisibleUntil)
                {
                    attempts = 0;
                    await Task.Delay(100, _cts.Token);
                }
                else if (failed)
                {
                    await Task.Delay(10, _cts.Token);
                }
                else if (shouldShow)
                {
                    break;
                }
                else
                {
                    await Task.Delay(50, _cts.Token);
                    attempts += 8;
                }
                
                attempts++;
            }
            
            // Wait for next trigger
            await Task.Delay(100, _cts.Token);
        }
    }
    
    private void RefreshTaskbars()
    {
        _taskbarHandles.Clear();
        
        var primaryTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primaryTaskbar != IntPtr.Zero)
        {
            _taskbarHandles.Add(primaryTaskbar);
        }
        
        IntPtr hwnd = IntPtr.Zero;
        while (true)
        {
            hwnd = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "Shell_SecondaryTrayWnd", null);
            if (hwnd == IntPtr.Zero) break;
            _taskbarHandles.Add(hwnd);
        }
    }
    
    private void RefreshMonitorRect()
    {
        var pt = new NativeMethods.POINT { X = 0, Y = 0 };
        var monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        
        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            _primaryMonitorRect = info.rcWork;
        }
    }
    
    private static string GetWindowClassName(IntPtr hwnd)
    {
        var className = new char[256];
        NativeMethods.GetClassName(hwnd, className, className.Length);
        return new string(className).TrimEnd('\0');
    }
    
    private void UnhookWindowsHookEx()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }
    
    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
