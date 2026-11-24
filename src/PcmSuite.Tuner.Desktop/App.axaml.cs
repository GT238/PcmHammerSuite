using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PcmSuite;

public partial class App : global::Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(desktop.Args?.FirstOrDefault(a => !a.StartsWith('-')));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
