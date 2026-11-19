using Avalonia.Controls;

namespace PcmSuite;

/// <summary>
/// Prototype shell. Each PcmHammer screen opens and holds its own adapter, so the shell hands the
/// adapter over: leaving a tab releases that tab's device, and the tab you arrive at connects as
/// usual. (A true shared connection needs both view models to accept a device from outside.)
/// </summary>
public partial class MainWindow : Window
{
    private const int FlashTab = 0;
    private const int LoggerTab = 1;
    private const int TuneTab = 2;

    private readonly PCMHammerAvalonia.ViewModels.MainWindowViewModel flash = new();
    private readonly PCMLoggerAvalonia.ViewModels.MainWindowViewModel logger = new();

    private int currentTab = FlashTab;
    private bool switching;

    public MainWindow()
    {
        InitializeComponent();

        this.FlashHost.Content = new PCMHammerAvalonia.Views.MainView { DataContext = this.flash };
        this.LoggerHost.Content = new PCMLoggerAvalonia.Views.MobileMainView { DataContext = this.logger };
        this.tune = new Tune.TuneViewModel { LogFolderProvider = () => this.logger.LogFolder };
        this.tuneView = new Tune.TuneView { DataContext = this.tune };
        this.TuneHost.Content = this.tuneView;
        this.CreditsHost.Content = new CreditsView();

        // The flasher loads its PCM kernels from files beside the exe. PCM Hammer only permits redistributing them
        // inside its official releases, so PcmSuite does not bundle them: tell the user instead of failing later.
        if (Directory.GetFiles(AppContext.BaseDirectory, "Kernel-*.bin").Length == 0)
        {
            this.ShellStatus.Text = "Kernel files not found. Copy the Kernel-*.bin files from the official PCM Hammer release " +
                                    "into this folder (" + AppContext.BaseDirectory + ") before reading or writing a PCM.";
        }

        // "Send to Flash": the Flash screen picks its file through its own dialog and has no way to be
        // handed a path, so the shell switches tabs and puts the path on the clipboard for the picker.
        this.tune.SendToFlashRequested += async path =>
        {
            this.flashFileHint = path;
            if (this.Clipboard != null)
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(this.Clipboard, path);
            }

            this.Tabs.SelectedIndex = FlashTab;
        };

        // Closing the window must not lose unsaved tuning edits, must not kill a flash operation half-way through,
        // and must let go of the adapter (and stop logging, which closes the log file) before the app exits.
        this.Closing += async (_, e) =>
        {
            if (this.closeConfirmed)
            {
                return;
            }

            if (this.flash.IsOperationRunning)
            {
                e.Cancel = true;
                this.ShellStatus.Text = "A flash operation is running. Wait for it to finish, or Cancel it, before closing PcmSuite.";
                return;
            }

            bool holdsAdapter = (this.currentTab == LoggerTab && this.logger.IsConnected)
                                || (this.currentTab == FlashTab && this.flash.SelectedDevice != null);
            if (!this.tune.IsDirty && !holdsAdapter)
            {
                return;
            }

            e.Cancel = true;

            if (this.tune.IsDirty)
            {
                // The question is shown over the Tune screen, so bring that screen up first.
                this.Tabs.SelectedIndex = TuneTab;
                if (!await this.tuneView.ConfirmDiscardAsync())
                {
                    return;
                }
            }

            await this.ReleaseAsync(this.currentTab);
            this.closeConfirmed = true;
            this.Close();
        };
    }

    private readonly Tune.TuneViewModel tune;
    private readonly Tune.TuneView tuneView;
    private bool closeConfirmed;

    private async void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Controls inside the tabs (their own tab strips, combo boxes) raise this event too.
        if (!ReferenceEquals(e.Source, this.Tabs) || this.switching)
        {
            return;
        }

        int next = this.Tabs.SelectedIndex;
        int previous = this.currentTab;
        if (next == previous || next < 0)
        {
            return;
        }

        this.switching = true;
        try
        {
            // Never take the adapter away in the middle of a read, write or verify.
            if (previous == FlashTab && this.flash.IsOperationRunning)
            {
                this.Tabs.SelectedIndex = previous;
                this.ShellStatus.Text = "A flash operation is running. Wait for it to finish, or Cancel it, before switching tabs.";
                return;
            }

            string message = await this.ReleaseAsync(previous);
            this.currentTab = next;
            this.ShellStatus.Text = message + this.ArrivalHint(next);
        }
        finally
        {
            this.switching = false;
        }
    }

    /// <summary>Let go of the adapter held by the tab being left.</summary>
    private async Task<string> ReleaseAsync(int tab)
    {
        switch (tab)
        {
            case LoggerTab when this.logger.IsConnected:
                // Stops logging and recording, then closes the port.
                await this.logger.ToggleConnectionCommand.ExecuteAsync(null);
                return "Logger disconnected. ";

            case FlashTab when this.flash.SelectedDevice != null:
                try
                {
                    this.flash.SelectedDevice.Dispose();
                }
                catch
                {
                    // Best effort: the port is being released regardless.
                }

                this.flash.PcmFlasher = null;
                this.flash.PcmReader = null;
                this.flash.Vehicle = null;
                this.flash.SelectedDevice = null;
                return "Flash adapter released. ";

            default:
                return string.Empty;
        }
    }

    private string? flashFileHint;

    private string ArrivalHint(int tab)
    {
        if (tab == FlashTab && this.flashFileHint != null)
        {
            string hint = $"Ready to flash {Path.GetFileName(this.flashFileHint)}: its path is on the clipboard " +
                          "(paste it into the file picker). Use Select Device to connect, then Write.";
            this.flashFileHint = null;
            return hint;
        }

        return tab switch
        {
            LoggerTab => "Press Connect to use the adapter here.",
            FlashTab => "Use Select Device to connect here.",
            _ => string.Empty,
        };
    }
}
