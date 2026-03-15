using Terminal.Gui;
using CommandCentral.Tui.Services;
using CommandCentral.Tui.Views;

var daemonUrl = args.Length > 0 ? args[0] : "http://localhost:9000";
using var client = new DaemonClient(daemonUrl);

Application.Init();

try
{
    var mainWindow = new MainWindow(client);
    Application.Top.Add(mainWindow);
    Application.Run();
}
finally
{
    Application.Shutdown();
}
