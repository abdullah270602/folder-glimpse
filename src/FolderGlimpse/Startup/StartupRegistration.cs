using Microsoft.Win32;
using System.IO;
using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Startup;

internal interface IStartupRegistration
{
    bool IsEnabled { get; }
    event EventHandler? Changed;
    bool TrySetEnabled(bool enabled, out string? error);
}

internal sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FolderGlimpse.exe");
    public event EventHandler? Changed;

    public RegistryStartupRegistration()
    {
        StartupRegistrationMigration.TryMigrate(new RegistryStartupValueStore(), ExecutablePath);
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunPath);
                var registered = Convert.ToString(key?.GetValue(StartupRegistrationMigration.CurrentValueName))?.Trim().Trim('"');
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
            if (enabled)
            {
                key.SetValue(StartupRegistrationMigration.CurrentValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
                key.DeleteValue(StartupRegistrationMigration.LegacyValueName, false);
            }
            else
            {
                key.DeleteValue(StartupRegistrationMigration.CurrentValueName, false);
                key.DeleteValue(StartupRegistrationMigration.LegacyValueName, false);
            }
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

    private sealed class RegistryStartupValueStore : IStartupValueStore
    {
        public string? Read(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath);
            return Convert.ToString(key?.GetValue(name));
        }

        public void Write(string name, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
            key.SetValue(name, value, RegistryValueKind.String);
        }

        public void Delete(string name)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
            key.DeleteValue(name, false);
        }
    }
}
