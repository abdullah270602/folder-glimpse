using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FolderGlimpse.Core.Input;
using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Input;

internal readonly record struct MouseTriggerGesture(MouseTriggerOptions Trigger, ExplorerSnapshot Target, HoverPoint ReleasePoint);

internal sealed class MouseTriggerHook : IDisposable
{
    private readonly Func<MouseTriggerInput, ExplorerSnapshot?> _tryCapture;
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly System.Threading.Timer _watchdog;
    private readonly ConcurrentQueue<MouseTriggerGesture> _pending = new();
    private nint _hook;
    private uint _threadId;
    private Exception? _startError;
    private int _desiredEnabled;
    private int _ownedButton;
    private int _suppressNextButtonUp;
    private long _ownedAtTicks;
    private MouseTriggerOptions _ownedTrigger;
    private ExplorerSnapshot? _ownedTarget;
    private int _drainScheduled;

    internal event Action<MouseTriggerGesture>? Gesture;
    internal event Action<Exception>? HookFailed;

    internal MouseTriggerHook(Func<MouseTriggerInput, ExplorerSnapshot?> tryCapture)
    {
        _tryCapture = tryCapture;
        _callback = OnMouse;
        _thread = new Thread(Run) { IsBackground = true, Name = "FolderGlimpse mouse trigger hook" };
        _watchdog = new System.Threading.Timer(WatchForLostRelease, null, Timeout.Infinite, Timeout.Infinite);
    }

    internal void Start(bool enabled)
    {
        Volatile.Write(ref _desiredEnabled, enabled ? 1 : 0);
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("The mouse trigger service did not start in time.");
        if (_startError is not null) throw new InvalidOperationException("The mouse trigger hook could not be installed.", _startError);
        _watchdog.Change(500, 500);
    }

    internal void SetEnabled(bool enabled)
    {
        Volatile.Write(ref _desiredEnabled, enabled ? 1 : 0);
        var threadId = Volatile.Read(ref _threadId);
        if (threadId != 0) NativeMethods.PostThreadMessage(threadId, NativeMethods.WmAppConfigureMouseHook, 0, 0);
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        // Create the native message queue before Start returns so an immediate settings change
        // cannot lose its configuration message in the small pre-GetMessage window.
        NativeMethods.PeekMessage(out _, 0, 0, 0, NativeMethods.PmNoRemove);
        if (Volatile.Read(ref _desiredEnabled) == 1 && !InstallHook())
            _startError = new Win32Exception(Marshal.GetLastWin32Error());
        _started.Set();
        if (_startError is not null) return;
        while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == NativeMethods.WmAppConfigureMouseHook) ConfigureHook();
        }
        if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private void ConfigureHook()
    {
        if (Volatile.Read(ref _desiredEnabled) == 1)
        {
            if (_hook == 0 && !InstallHook()) HookFailed?.Invoke(new Win32Exception(Marshal.GetLastWin32Error()));
        }
        else if (_hook != 0 && Volatile.Read(ref _ownedButton) == 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private bool InstallHook()
    {
        _hook = NativeMethods.SetWindowsHookExMouse(NativeMethods.WhMouseLl, _callback, NativeMethods.GetModuleHandle(null), 0);
        return _hook != 0;
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        var message = unchecked((int)wParam);
        var button = ButtonFor(message);
        if (button is null) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        var data = Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);

        if (IsDownMessage(message))
        {
            if (Volatile.Read(ref _ownedButton) != 0) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            if (Volatile.Read(ref _suppressNextButtonUp) == (int)button.Value + 1)
                Volatile.Write(ref _suppressNextButtonUp, 0);
            var input = new MouseTriggerInput(button.Value, new(data.Point.X, data.Point.Y),
                IsDown(NativeMethods.VkControl), IsDown(NativeMethods.VkShift), IsDown(NativeMethods.VkMenu),
                IsDown(NativeMethods.VkLWin) || IsDown(NativeMethods.VkRWin),
                (data.Flags & NativeMethods.LlmhfInjected) != 0, NativeMethods.GetForegroundWindow(), DateTimeOffset.UtcNow);
            ExplorerSnapshot? target = null;
            try { target = _tryCapture(input); }
            catch (Exception exception) { if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"mouse eligibility failure: {exception.Message}"); }
            if (target is null) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            _ownedTarget = target;
            _ownedTrigger = MouseTriggerPolicy.Match(input);
            Volatile.Write(ref _ownedAtTicks, Environment.TickCount64);
            Volatile.Write(ref _ownedButton, (int)button.Value + 1);
            return 1;
        }

        var encodedButton = (int)button.Value + 1;
        if (Volatile.Read(ref _ownedButton) != encodedButton)
        {
            if (Volatile.Read(ref _suppressNextButtonUp) == encodedButton)
            {
                Volatile.Write(ref _suppressNextButtonUp, 0);
                return 1;
            }
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }
        Volatile.Write(ref _ownedButton, 0);
        if (Volatile.Read(ref _suppressNextButtonUp) == encodedButton)
            Volatile.Write(ref _suppressNextButtonUp, 0);
        var ownedTarget = _ownedTarget;
        var ownedTrigger = _ownedTrigger;
        _ownedTarget = null;
        _ownedTrigger = MouseTriggerOptions.None;
        if (ownedTarget is not null) Publish(new(ownedTrigger, ownedTarget, new(data.Point.X, data.Point.Y)));
        if (Volatile.Read(ref _desiredEnabled) == 0)
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WmAppConfigureMouseHook, 0, 0);
        return 1;
    }

    private static bool IsDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    private static bool IsDownMessage(int message) => message is NativeMethods.WmLButtonDown or NativeMethods.WmRButtonDown or NativeMethods.WmMButtonDown;
    private static MouseTriggerButton? ButtonFor(int message) => message switch
    {
        NativeMethods.WmLButtonDown or NativeMethods.WmLButtonUp => MouseTriggerButton.Left,
        NativeMethods.WmRButtonDown or NativeMethods.WmRButtonUp => MouseTriggerButton.Right,
        NativeMethods.WmMButtonDown or NativeMethods.WmMButtonUp => MouseTriggerButton.Middle,
        _ => null
    };

    private void WatchForLostRelease(object? _)
    {
        var encodedButton = Volatile.Read(ref _ownedButton);
        if (encodedButton == 0 || Environment.TickCount64 - Volatile.Read(ref _ownedAtTicks) < 750) return;
        var key = encodedButton switch
        {
            (int)MouseTriggerButton.Left + 1 => NativeMethods.VkLButton,
            (int)MouseTriggerButton.Right + 1 => NativeMethods.VkRButton,
            (int)MouseTriggerButton.Middle + 1 => NativeMethods.VkMButton,
            _ => 0
        };
        if (key != 0 && IsDown(key)) return;
        Volatile.Write(ref _suppressNextButtonUp, encodedButton);
        if (Interlocked.CompareExchange(ref _ownedButton, 0, encodedButton) != encodedButton)
        {
            Interlocked.CompareExchange(ref _suppressNextButtonUp, 0, encodedButton);
            return;
        }
        _ownedTarget = null;
        _ownedTrigger = MouseTriggerOptions.None;
        if (Volatile.Read(ref _desiredEnabled) == 0)
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WmAppConfigureMouseHook, 0, 0);
    }

    private void Publish(MouseTriggerGesture gesture)
    {
        _pending.Enqueue(gesture);
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(static owner => owner.Drain(), this, false);
    }

    private void Drain()
    {
        try
        {
            while (_pending.TryDequeue(out var gesture))
                try { Gesture?.Invoke(gesture); } catch (Exception exception) { if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"mouse gesture failure: {exception.Message}"); }
        }
        finally
        {
            Volatile.Write(ref _drainScheduled, 0);
            if (!_pending.IsEmpty && Interlocked.Exchange(ref _drainScheduled, 1) == 0)
                ThreadPool.UnsafeQueueUserWorkItem(static owner => owner.Drain(), this, false);
        }
    }

    public void Dispose()
    {
        _watchdog.Change(Timeout.Infinite, Timeout.Infinite);
        var threadId = Volatile.Read(ref _threadId);
        if (threadId != 0) NativeMethods.PostThreadMessage(threadId, NativeMethods.WmQuit, 0, 0);
        if (_thread.IsAlive) _thread.Join(1000);
        _watchdog.Dispose();
        _started.Dispose();
    }
}
