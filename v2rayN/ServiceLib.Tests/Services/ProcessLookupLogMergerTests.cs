namespace ServiceLib.Tests.Services;

public class ProcessLookupLogMergerTests
{
    private const string Err = "2026/11/25 11:14:04.161243 [Error] app/router: Unables to find local process name: common/net: not found";
    private const string Acc = "2026/11/25 11:14:04.161243 from udp:172.18.0.1:61732 accepted udp:57.144.243.32:443 [tun -> proxy]";

    [Test]
    public async Task ErrorThenAccepted_MergesIntoOneLine()
    {
        var lines = new List<string>();
        using var m = new ProcessLookupLogMerger(lines.Add);

        m.Push(Err);
        m.Push(Acc);

        await lines.Count.Should().BeEqualTo(1);
        await lines[0].Should().BeEqualTo(Acc + " (no local process)");
    }

    [Test]
    public async Task AcceptedThenError_MergesIntoOneLine()
    {
        var lines = new List<string>();
        using var m = new ProcessLookupLogMerger(lines.Add);

        m.Push(Acc);
        m.Push(Err);

        await lines.Count.Should().BeEqualTo(1);
        await lines[0].Should().BeEqualTo(Acc + " (no local process)");
    }

    [Test]
    public async Task UnrelatedLine_PassesThroughUnchanged()
    {
        var lines = new List<string>();
        using var m = new ProcessLookupLogMerger(lines.Add);

        m.Push("Xray 26.10.31 started");

        await lines.Count.Should().BeEqualTo(1);
        await lines[0].Should().BeEqualTo("Xray 26.10.31 started");
    }

    [Test]
    public async Task DifferentTimestamps_DoNotMerge_AndKeepOrder()
    {
        var lines = new List<string>();
        using var m = new ProcessLookupLogMerger(lines.Add);

        m.Push(Err);
        m.Push(Acc.Replace("161243", "999999"));
        m.Flush();

        await lines.Count.Should().BeEqualTo(2);
        await lines[0].Should().BeEqualTo(Err);
        await lines[1].Should().BeEqualTo(Acc.Replace("161243", "999999"));
    }

    [Test]
    public async Task HeldLine_IsFlushedAfterTimeout()
    {
        var lines = new List<string>();
        using var m = new ProcessLookupLogMerger(l => { lock (lines) { lines.Add(l); } }, holdMs: 20);

        m.Push(Err);
        await Task.Delay(300);

        string[] snapshot;
        lock (lines)
        {
            snapshot = [.. lines];
        }
        await snapshot.Length.Should().BeEqualTo(1);
        await snapshot[0].Should().BeEqualTo(Err);
    }
}
