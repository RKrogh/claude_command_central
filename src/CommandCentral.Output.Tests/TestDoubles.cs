using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Output.Tests;

/// <summary>
/// In-memory state store for tests; optionally seeded with initial state.
/// </summary>
internal sealed class InMemoryStateStore(DaemonState? initial = null) : IStateStore
{
    private readonly DaemonState _state = initial ?? new DaemonState();

    public int UpdateCount { get; private set; }

    public DaemonState State => new()
    {
        SelectedInstanceId = _state.SelectedInstanceId,
        VoiceAssignments = new Dictionary<string, string>(_state.VoiceAssignments)
    };

    public void Update(Action<DaemonState> mutate)
    {
        mutate(_state);
        UpdateCount++;
    }
}

/// <summary>
/// Captures log entries so tests can assert on levels and messages.
/// </summary>
internal sealed class TestLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public int Count(LogLevel level) => Entries.Count(e => e.Level == level);

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}

internal sealed class StubPersonalityManager : IPersonalityManager
{
    public PersonalityConfig? GetForSlot(string slotId) => null;
    public string? ResolveVoiceRefPath(string slotId) => null;
    public string? ResolveSlotConfigPath(string slotId) => null;
    public void Reload() { }
}

/// <summary>
/// Creates a unique temp directory and deletes it on dispose.
/// </summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ccc-tests-{Guid.NewGuid():N}");

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
