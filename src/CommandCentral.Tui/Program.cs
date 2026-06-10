using Terminal.Gui;
using CommandCentral.Tui.Services;
using CommandCentral.Tui.Views;

var daemonUrl = args.Length > 0 ? args[0] : "http://localhost:9000";

var store = new TuiStateStore();
using var client = new DaemonClient(daemonUrl);
var eventStream = new DaemonEventStreamClient(daemonUrl, store);
using var cts = new CancellationTokenSource();

Application.Init();

try
{
    var mainWindow = new MainWindow(store, daemonUrl);
    Application.Top.Add(mainWindow);

    // Fast first paint from a plain GET, then live updates over the
    // WebSocket (which re-sends a snapshot on every (re)connect).
    _ = Task.Run(async () =>
    {
        var initial = await client.GetStateAsync(cts.Token);
        if (initial is not null)
            store.ApplySnapshot(initial);

        await eventStream.RunAsync(cts.Token);
    });

    Application.Run();
}
finally
{
    cts.Cancel();
    Application.Shutdown();
}
