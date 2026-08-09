using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using FolderGlimpse.Core.Application;

namespace FolderGlimpse.Application;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _pipeName;
    private Task? _listener;

    private SingleInstanceCoordinator(Mutex mutex, bool isPrimary, string? pipeName)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        _pipeName = pipeName;
    }

    internal bool IsPrimary { get; }
    internal event Action<ActivationRequest>? ActivationRequested;

    internal static SingleInstanceCoordinator Create(bool isolatedCapture)
    {
        var session = Process.GetCurrentProcess().SessionId;
        var suffix = isolatedCapture ? $"Capture.{Environment.ProcessId}" : $"Session.{session}";
        var mutex = new Mutex(true, $"Local\\FolderGlimpse.{suffix}", out var created);
        return new SingleInstanceCoordinator(mutex, created, isolatedCapture ? null : $"FolderGlimpse.Activation.{session}.v1");
    }

    internal void StartListening()
    {
        if (!IsPrimary || _pipeName is null || _listener is not null) return;
        _listener = Task.Run(ListenAsync);
    }

    internal bool TrySignal(ActivationRequest request, TimeSpan timeout)
    {
        if (_pipeName is null) return false;
        var deadline = Environment.TickCount64 + Math.Max(1, (long)timeout.TotalMilliseconds);
        do
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                client.Connect(350);
                if (GetNamedPipeServerProcessId(client.SafePipeHandle, out var serverProcessId))
                    AllowSetForegroundWindow(serverProcessId);
                client.WriteByte(ActivationRequestCodec.Encode(request));
                client.Flush();
                return true;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException)
            {
                Thread.Sleep(75);
            }
        } while (Environment.TickCount64 < deadline);
        return false;
    }

    private async Task ListenAsync()
    {
        while (!_lifetime.IsCancellationRequested && _pipeName is not null)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_lifetime.Token);
                var value = server.ReadByte();
                if (value >= 0 && ActivationRequestCodec.TryDecode((byte)value, out var request))
                    ActivationRequested?.Invoke(request);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (IOException) { }
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        try { _listener?.Wait(500); } catch { }
        _lifetime.Dispose();
        if (IsPrimary) { try { _mutex.ReleaseMutex(); } catch (ApplicationException) { } }
        _mutex.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
