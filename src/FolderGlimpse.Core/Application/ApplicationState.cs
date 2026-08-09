using System.Text.Json;

namespace FolderGlimpse.Core.Application;

public sealed record ApplicationState(bool HasCompletedOnboarding = false, bool HasShownTrayCloseHint = false);

public interface IApplicationStateService
{
    ApplicationState Current { get; }
    string? LastError { get; }
    void Load();
    bool TryUpdate(Func<ApplicationState, ApplicationState> update, out string? error);
}

public sealed class JsonApplicationStateService : IApplicationStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private ApplicationState _current = new();

    public JsonApplicationStateService(string path) => _path = Path.GetFullPath(path);
    public ApplicationState Current { get { lock (_gate) return _current; } }
    public string? LastError { get; private set; }

    public void Load()
    {
        try
        {
            var loaded = !File.Exists(_path) || string.IsNullOrWhiteSpace(File.ReadAllText(_path))
                ? new ApplicationState()
                : JsonSerializer.Deserialize<ApplicationState>(File.ReadAllText(_path), JsonOptions) ?? new ApplicationState();
            lock (_gate) _current = loaded;
            LastError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            lock (_gate) _current = new ApplicationState();
            LastError = "FolderGlimpse could not read its application state. Welcome will be shown safely.";
        }
    }

    public bool TryUpdate(Func<ApplicationState, ApplicationState> update, out string? error)
    {
        ArgumentNullException.ThrowIfNull(update);
        ApplicationState previous;
        ApplicationState next;
        lock (_gate)
        {
            previous = _current;
            next = update(previous);
            if (next == previous) { error = null; return true; }
            if (!TryWrite(next, out error)) return false;
            _current = next;
            LastError = null;
        }
        return true;
    }

    private bool TryWrite(ApplicationState state, out string? error)
    {
        string? temp = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(state, JsonOptions));
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temp, _path, true);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = error = "Unable to save FolderGlimpse application state.";
            return false;
        }
        finally
        {
            if (temp is not null) { try { File.Delete(temp); } catch { } }
        }
    }
}
