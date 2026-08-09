namespace FolderGlimpse.Core.Application;

public enum LaunchIntentKind { Normal, Startup, Settings, About, Capture }
public enum ShellSection { Home, Settings, HowToUse, About }
public enum InitialSurface { None, Welcome, Home, Settings, About }
public enum ActivationRequest : byte { OpenDefault = 1, Settings = 2, About = 3 }

public readonly record struct LaunchIntent(LaunchIntentKind Kind)
{
    public static LaunchIntent Parse(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        if (args.Any(IsCaptureArgument)) return new(LaunchIntentKind.Capture);
        if (args.Contains("--startup", StringComparer.OrdinalIgnoreCase)) return new(LaunchIntentKind.Startup);
        if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) return new(LaunchIntentKind.Settings);
        if (args.Contains("--about", StringComparer.OrdinalIgnoreCase)) return new(LaunchIntentKind.About);
        return new(LaunchIntentKind.Normal);
    }

    public ActivationRequest? ActivationRequest => Kind switch
    {
        LaunchIntentKind.Normal => Application.ActivationRequest.OpenDefault,
        LaunchIntentKind.Settings => Application.ActivationRequest.Settings,
        LaunchIntentKind.About => Application.ActivationRequest.About,
        _ => null
    };

    private static bool IsCaptureArgument(string argument) =>
        argument.StartsWith("--capture-settings=", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--capture-preview=", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--capture-tray=", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--capture-about=", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--capture-welcome=", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--capture-main=", StringComparison.OrdinalIgnoreCase);
}

public sealed class ShellNavigationModel
{
    public ShellSection Current { get; private set; } = ShellSection.Home;
    public event EventHandler<ShellSection>? Changed;
    public void Navigate(ShellSection section)
    {
        if (section == Current) return;
        Current = section;
        Changed?.Invoke(this, section);
    }
}

public static class InitialSurfacePolicy
{
    public static InitialSurface Decide(LaunchIntent intent, bool hasCompletedOnboarding) => intent.Kind switch
    {
        LaunchIntentKind.Startup or LaunchIntentKind.Capture => InitialSurface.None,
        LaunchIntentKind.Settings => InitialSurface.Settings,
        LaunchIntentKind.About => InitialSurface.About,
        _ => hasCompletedOnboarding ? InitialSurface.Home : InitialSurface.Welcome
    };
}

public static class ActivationRequestCodec
{
    public static byte Encode(ActivationRequest request) => (byte)request;
    public static bool TryDecode(byte value, out ActivationRequest request)
    {
        request = (ActivationRequest)value;
        return Enum.IsDefined(request);
    }
}
