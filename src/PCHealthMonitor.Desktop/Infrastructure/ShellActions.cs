using System.Diagnostics;
using System.IO;

namespace PCHealthMonitor.Desktop.Infrastructure;

/// <summary>User-initiated shell actions (only opening folders in Explorer).</summary>
public static class ShellActions
{
    /// <summary>Opens a folder in Explorer, selecting <paramref name="selectFile"/> when it exists.</summary>
    public static void OpenFolder(string folder, string? selectFile = null)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var arguments = selectFile is not null && File.Exists(selectFile)
                ? $"/select,\"{selectFile}\""
                : $"\"{folder}\"";
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show($"The folder could not be opened:\n{folder}", "PC Health Monitor",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }
}
