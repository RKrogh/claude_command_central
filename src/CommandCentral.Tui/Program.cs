using Terminal.Gui;
using CommandCentral.Tui.Views;

Application.Init();

try
{
    var mainWindow = new MainWindow();
    Application.Top.Add(mainWindow);
    Application.Run();
}
finally
{
    Application.Shutdown();
}
