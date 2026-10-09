using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PCHealthMonitor.Desktop.Infrastructure;

/// <summary>Asks Windows (DWM) to draw this window's title bar in dark mode. Purely cosmetic; ignored if unsupported.</summary>
internal static partial class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;          // Windows 10 20H1+ / Windows 11
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19; // Windows 10 1809–1909

    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older system: keep the default title bar.
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
