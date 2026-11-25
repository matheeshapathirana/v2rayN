namespace ServiceLib.Services;

/// <summary>
/// Xray logs "Unables to find local process name" without a destination (traffic forwarded
/// through the TUN, e.g. hotspot clients, has no local process). The matching "accepted" line
/// carries the identical timestamp, so pair them and print a single line with the destination.
/// stdout/stderr race, so either line may come first; one line is held briefly to wait for its partner.
/// </summary>
public sealed class ProcessLookupLogMerger : IDisposable
{
    private const string ErrorMarker = "Unables to find local process name";
    private const string AcceptedMarker = " accepted ";
    private const string MergedTag = " (no local process)";
    private const int TimestampLength = 26; // 2026/11/25 11:14:04.161243

    private readonly Action<string> _emit;
    private readonly object _lock = new();
    private readonly Timer _timer;
    private readonly int _holdMs;
    private string? _held;

    public ProcessLookupLogMerger(Action<string> emit, int holdMs = 100)
    {
        _emit = emit;
        _holdMs = holdMs;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Push(string line)
    {
        lock (_lock)
        {
            if (_held != null)
            {
                if (IsPair(_held, line))
                {
                    var accepted = IsError(_held) ? line : _held;
                    _held = null;
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    _emit(accepted + MergedTag);
                    return;
                }
                FlushLocked();
            }

            if (IsError(line) || IsAccepted(line))
            {
                _held = line;
                _timer.Change(_holdMs, Timeout.Infinite);
                return;
            }

            _emit(line);
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            FlushLocked();
        }
    }

    public void Dispose()
    {
        Flush();
        _timer.Dispose();
    }

    private void FlushLocked()
    {
        if (_held == null)
        {
            return;
        }
        var line = _held;
        _held = null;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _emit(line);
    }

    private static bool IsError(string line) => line.Contains(ErrorMarker);

    private static bool IsAccepted(string line) => line.Contains(AcceptedMarker);

    private static bool IsPair(string a, string b)
    {
        if (!((IsError(a) && IsAccepted(b)) || (IsAccepted(a) && IsError(b))))
        {
            return false;
        }
        return a.Length >= TimestampLength
            && b.Length >= TimestampLength
            && string.CompareOrdinal(a, 0, b, 0, TimestampLength) == 0;
    }
}
