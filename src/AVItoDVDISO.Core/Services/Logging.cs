# ---------------------------------
# File: src\AVItoDVDISO.Core\Services\Logging.cs
# ---------------------------------
using System.Collections.Concurrent;

namespace AVItoDVDISO.Core.Services;

public sealed class LogBuffer
{
    private readonly ConcurrentQueue<string> _lines = new();

    public event Action<string>? LineAdded;

    public void Add(string line)
    {
        _lines.Enqueue(line);
        LineAdded?.Invoke(line);
    }

    public IReadOnlyList<string> Snapshot()
    {
        return _lines.ToArray();
    }
}