using Microsoft.Win32;
using System.IO;

namespace FolderPeek.Startup;

internal interface IStartupRegistration
{
    bool IsEnabled { get; }
    event EventHandler? Changed;
    bool TrySetEnabled(bool enabled, out string? error);
}

internal sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FolderPeek";
    private string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FolderPeek.exe");
    public event EventHandler? Changed;

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunPath);
                var registered = Convert.ToString(key?.GetValue(ValueName))?.Trim().Trim('"');
                return string.Equals(Path.GetFullPath(registered ?? string.Empty), Path.GetFullPath(ExecutablePath), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
            if (enabled) key.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
            Changed?.Invoke(this, EventArgs.Empty);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            error = "Windows could not update the startup setting.";
            return false;
        }
    }
}
