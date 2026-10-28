using System.Collections.Concurrent;
using System.Threading.Channels;

namespace XivType;

// Native UI reads stay on the framework thread. This worker receives managed strings only.
internal sealed class DiagnosticStore : IDisposable
{
    private readonly string directory;
    private readonly ConcurrentDictionary<string, string> snapshots = new();
    private readonly Channel<(string Name, string Text)> history = Channel.CreateBounded<(string, string)>(
        new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly ConcurrentQueue<string> commands = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    public string? Error { get; private set; }

    public DiagnosticStore(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
        // A reload starts disabled and discards any unconsumed development command.
        File.WriteAllText(Path.Combine(directory, "control.txt"), "");
        worker = Task.Run(Run);
    }

    public void Write(string name, string text) => snapshots[name] = text;
    public void Append(string name, string text) => history.Writer.TryWrite((name, text));
    public bool TryGetCommand(out string command) => commands.TryDequeue(out command!);

    private async Task Run()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(100, cancellation.Token).ConfigureAwait(false);
                try
                {
                    var control = Path.Combine(directory, "control.txt");
                    if (File.Exists(control))
                    {
                        var command = (await File.ReadAllTextAsync(control, cancellation.Token).ConfigureAwait(false)).Trim();
                        if (command.Length > 0)
                        {
                            await File.WriteAllTextAsync(control, "", cancellation.Token).ConfigureAwait(false);
                            commands.Enqueue(command);
                        }
                    }
                    Flush();
                }
                catch (IOException ex) { Error = ex.Message; }
                catch (UnauthorizedAccessException ex) { Error = ex.Message; }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { Flush(); }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    private void Flush()
    {
        foreach (var name in snapshots.Keys)
        {
            if (!snapshots.TryRemove(name, out var text)) continue;
            var path = Path.Combine(directory, name);
            File.WriteAllText(path + ".tmp", text);
            File.Move(path + ".tmp", path, true);
        }
        while (history.Reader.TryRead(out var entry))
        {
            var path = Path.Combine(directory, entry.Name);
            if (File.Exists(path) && new FileInfo(path).Length > 16 * 1024 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, entry.Text);
        }
    }

    public void Dispose()
    {
        history.Writer.TryComplete();
        cancellation.Cancel();
        if (worker.Wait(1000)) cancellation.Dispose();
    }
}
