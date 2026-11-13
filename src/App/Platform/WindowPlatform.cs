using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NEO.Core;
using NEO.Infrastructure;
using Forms = System.Windows.Forms;

namespace NEO.Platform;

internal static partial class WindowPlatform
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const uint NoSize = 1, NoActivate = 0x10;

    public static void Initialize(Window window, bool passive)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | (window.ShowInTaskbar ? 0 : WsExToolWindow) | (passive ? WsExNoActivate : 0));
        if (window.AllowsTransparency)
        {
            int disabled = 1; // DWMNCRP_DISABLED / DWMSBT_NONE / DWMWCP_DONOTROUND
            int noBorder = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(handle, 2, ref disabled, sizeof(int));
            DwmSetWindowAttribute(handle, 33, ref disabled, sizeof(int));
            DwmSetWindowAttribute(handle, 34, ref noBorder, sizeof(int));
            int backdropResult = DwmSetWindowAttribute(handle, 38, ref disabled, sizeof(int));
            NEOLog.Write("ui.native.chrome", new { window = window.GetType().Name, transparent = window.AllowsTransparency, layered = (GetWindowLong(handle, GwlExStyle) & 0x80000) != 0, backdrop = "none", backdropResult });
        }
        else
        {
            int rounded = 2;
            DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        }
    }

    public static void Position(Window window, Settings settings, bool bottom, bool center = false)
    {
        var screens = Forms.Screen.AllScreens;
        var screen = screens[Math.Min(settings.Monitor, screens.Length - 1)];
        var handle = new WindowInteropHelper(window).Handle;
        // First move onto the target monitor so GetDpiForWindow reports its DPI.
        SetWindowPos(handle, IntPtr.Zero, screen.WorkingArea.Left, screen.WorkingArea.Top, 0, 0, NoSize | NoActivate | 4);
        var scale = GetDpiForWindow(handle) / 96.0;
        if (scale <= 0) scale = 1;
        var area = screen.WorkingArea;
        int width = Math.Min(area.Width - 24, (int)Math.Round(window.Width * scale)), height = Math.Min(area.Height - 24, (int)Math.Round(window.Height * scale));
        int x = area.Left + (area.Width - width) / 2;
        int y = center ? area.Top + (area.Height - height) / 2 : bottom ? area.Bottom - height - (int)(24 * scale) : area.Top + (int)(settings.TopOffset * scale);
        SetWindowPos(handle, settings.AlwaysOnTop ? new IntPtr(-1) : new IntPtr(-2), x, y, width, height, NoActivate);
    }

    public static bool IsFullscreenOtherApp(Window window)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == new WindowInteropHelper(window).Handle) return false;
        GetWindowThreadProcessId(foreground, out uint processId);
        if (processId == Environment.ProcessId) return false;
        // Desktop and shell windows must never trigger fullscreen hiding.
        var name = new System.Text.StringBuilder(256);
        GetClassName(foreground, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        var bounds = Forms.Screen.FromHandle(foreground).Bounds;
        var target = Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle);
        return target.Bounds == bounds && rect.Left <= bounds.Left && rect.Top <= bounds.Top
            && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int count);

    public static IDisposable RegisterShortcut(Window window, Action action)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        const int id = 0x4E45;
        if (!RegisterHotKey(source.Handle, id, 0x4003, 0x20))
            throw new InvalidOperationException("Ctrl+Alt+Space is already registered by another application. NEO is still available in the tray.");
        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && wParam.ToInt32() == id) { action(); handled = true; }
            return IntPtr.Zero;
        }
        source.AddHook(Hook);
        return new ShortcutRegistration(() => { UnregisterHotKey(source.Handle, id); source.RemoveHook(Hook); });
    }
    private sealed class ShortcutRegistration(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
