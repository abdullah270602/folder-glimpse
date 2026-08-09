using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace FolderPeek.Input;

internal enum HookGesture { SpaceDown, SpaceUp, Escape, LostRelease }

internal sealed class KeyboardHook : IDisposable
{
    private readonly Func<bool> _canOwnSpace;
    private readonly Thread _thread;
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly bool _allowInjectedInput;
    private readonly System.Threading.Timer _watchdog;
    private nint _hook;
    private uint _threadId;
    private int _spaceDown;
    private int _owned;
    private int _suppressNextUp;
    private long _downAtTicks;
    private int _canConsumeEscape;
    private readonly ConcurrentQueue<HookGesture> _pendingGestures = new();
    private int _drainScheduled;

    internal event Action<HookGesture>? Gesture;
    internal bool CanConsumeEscape { set => Volatile.Write(ref _canConsumeEscape, value ? 1 : 0); }

    internal KeyboardHook(Func<bool> canOwnSpace, bool allowInjectedInput = false)
    {
        _canOwnSpace = canOwnSpace;
        _allowInjectedInput = allowInjectedInput;
        _callback = OnKeyboard;
        _thread = new Thread(Run) { IsBackground = true, Name = "FolderPeek keyboard hook" };
        _watchdog = new System.Threading.Timer(WatchForLostRelease, null, Timeout.Infinite, Timeout.Infinite);
    }

    internal void Start()
    {
        _thread.Start();
        _watchdog.Change(500, 500);
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _callback, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0) { }
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
        var down = wParam == NativeMethods.WmKeyDown || wParam == NativeMethods.WmSysKeyDown;
        var up = wParam == NativeMethods.WmKeyUp || wParam == NativeMethods.WmSysKeyUp;

        if (data.VkCode == NativeMethods.VkSpace)
        {
            if (down)
            {
                if (Interlocked.Exchange(ref _spaceDown, 1) == 1)
                    return Volatile.Read(ref _owned) == 1 ? 1 : NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                _downAtTicks = Environment.TickCount64;
                var injected = (data.Flags & NativeMethods.LlkhfInjected) != 0;
                var owned = (!injected || _allowInjectedInput) && _canOwnSpace();
                Volatile.Write(ref _owned, owned ? 1 : 0);
                if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"space down injected={injected} owned={owned}");
                if (owned) { Publish(HookGesture.SpaceDown); return 1; }
            }
            else if (up)
            {
                var owned = Interlocked.Exchange(ref _owned, 0) == 1 || Interlocked.Exchange(ref _suppressNextUp, 0) == 1;
                Volatile.Write(ref _spaceDown, 0);
                if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"space up owned={owned}");
                if (owned) { Publish(HookGesture.SpaceUp); return 1; }
            }
        }
        else if (data.VkCode == NativeMethods.VkEscape && down && Volatile.Read(ref _canConsumeEscape) == 1 && _canOwnSpace())
        {
            Publish(HookGesture.Escape);
            return 1;
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void WatchForLostRelease(object? _)
    {
        if (Volatile.Read(ref _spaceDown) == 0 || Environment.TickCount64 - Volatile.Read(ref _downAtTicks) < 750) return;
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VkSpace) & 0x8000) != 0) return;
        if (Interlocked.Exchange(ref _spaceDown, 0) == 1 && Interlocked.Exchange(ref _owned, 0) == 1)
        {
            Volatile.Write(ref _suppressNextUp, 1);
            Publish(HookGesture.LostRelease);
        }
    }

    private void Publish(HookGesture gesture)
    {
        _pendingGestures.Enqueue(gesture);
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(static owner => owner.DrainGestures(), this, false);
    }

    private void DrainGestures()
    {
        while (_pendingGestures.TryDequeue(out var gesture)) Gesture?.Invoke(gesture);
        Volatile.Write(ref _drainScheduled, 0);
        if (!_pendingGestures.IsEmpty && Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(static owner => owner.DrainGestures(), this, false);
    }

    public void Dispose()
    {
        _watchdog.Dispose();
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, 0x0012, 0, 0);
        if (_thread.IsAlive) _thread.Join(1000);
    }
}
