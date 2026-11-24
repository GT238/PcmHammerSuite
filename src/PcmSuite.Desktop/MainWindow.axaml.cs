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
    private const int HelpTab = 4;

    private readonly PCMHammerAvalonia.ViewModels.MainWindowViewModel flash = new();
    private readonly PCMLoggerAvalonia.ViewModels.MainWindowViewModel logger = new();

    private int currentTab = FlashTab;
    private bool switching;

    // A BIN or XDF given on the command line (for example "Open with" from Explorer), opened in the Tuner at start.
    private readonly string? startupFile;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupFile)
    {
        this.startupFile = startupFile is { Length: > 0 } && File.Exists(startupFile) ? Path.GetFullPath(startupFile) : null;
        InitializeComponent();

        // Starts at 1920 x 1080; on a smaller screen, shrink to fit instead of opening off the edge.
        this.Opened += async (_, _) =>
        {
            // Fit the screen first, before anything slow happens, so the status bar is never under the taskbar.
            if (this.Screens.ScreenFromWindow(this) is { } screen)
            {
                double maxWidth = screen.WorkingArea.Width / screen.Scaling;
                double maxHeight = screen.WorkingArea.Height / screen.Scaling;
                if (this.Width > maxWidth || this.Height > maxHeight)
                {
                    this.Width = Math.Min(this.Width, maxWidth);
                    this.Height = Math.Min(this.Height, maxHeight);
                    this.Position = screen.WorkingArea.Position;
                }
            }

            // Reopen the project that was open last time, with its BIN (unless a file was given to open instead).
            try
            {
                await this.tune.RestoreLastProjectAsync(this.startupFile == null);
                if (this.startupFile != null)
                {
                    this.Tabs.SelectedIndex = TuneTab;
                    if (Path.GetExtension(this.startupFile).Equals(".xdf", StringComparison.OrdinalIgnoreCase))
                    {
                        await this.tune.OpenXdfAsync(this.startupFile);
                    }
                    else
                    {
                        await this.tune.ImportBinAsync(this.startupFile, copyIntoProject: false);
                    }
                }
            }
            catch (Exception ex)
            {
                this.ShellStatus.Text = "Could not reopen the last project: " + ex.Message;
            }
        };

        // PCM Hammer's desktop layouts: the flasher's menu / side panel / status bar, and the logger's full desktop view
        // (not the phone-style views, which are what PcmSuite showed at first).
        this.FlashHost.Content = new FlashDesktopView { DataContext = this.flash };
        this.LoggerHost.Content = new PCMLoggerAvalonia.Views.MainView { DataContext = this.logger };
        this.tune = new Tune.TuneViewModel
        {
            LogFolderProvider = () => this.logger.LogFolder,
            LiveSource = new LoggerLiveSource(this.logger),
            ProfilesFolderProvider = () => Tune.LogProfileCatalog.SharedFolder,
        };

        // One profile folder for the Logger and the Tune tab: put the tuning profiles that ship with PcmSuite into
        // it (never overwriting a file the user has edited).
        Tune.LogProfileCatalog.Seed(Path.Combine(AppContext.BaseDirectory, "Profiles"), Tune.LogProfileCatalog.SharedFolder);
        // A project's recordings go in its own logs folder; closing the project puts the Logger back where it was.
        string defaultLogFolder = this.logger.LogFolder;
        this.tune.ProjectChanged += project =>
            this.logger.LogFolder = project != null ? project.LogsFolder : defaultLogFolder;

        this.tuneView = new Tune.TuneView { DataContext = this.tune };
        this.TuneHost.Content = this.tuneView;
        this.helpView = new HelpView();
        this.HelpHost.Content = this.helpView;
        this.LogsHost.Content = new LogsView();
        this.AboutHost.Content = new AboutView("PcmSuite");
        this.CreditsHost.Content = new CreditsView();

        // "?" in the Tuner: open the Help tab at the right topic and section.
        this.tune.HelpRequested += topic =>
        {
            string[] parts = topic.Split('#', 2);
            this.helpView.Show(parts[0], parts.Length > 1 ? parts[1] : null);
            this.Tabs.SelectedIndex = HelpTab;
        };

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

        // A new project that starts with a read from the PCM: take the user to the Flash tab with the project's reads
        // folder on the clipboard (the Flash screen's file picker cannot be preset).
        this.tune.FlashReadRequested += async project =>
        {
            if (this.Clipboard != null)
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(this.Clipboard, project.ReadsFolder);
            }

            this.Tabs.SelectedIndex = FlashTab;
        };

        // Logging profiles: the Logger opens a profile through its own file picker and cannot be handed a path,
        // so the shell switches tabs and puts the profile's path on the clipboard, as for Send to Flash.
        this.tune.UseProfileRequested += async path =>
        {
            this.loggerProfileHint = path;
            if (this.Clipboard != null)
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(this.Clipboard, path);
            }

            this.Tabs.SelectedIndex = LoggerTab;
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
                                || this.loggerInBackground
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
            if (this.loggerInBackground)
            {
                this.loggerInBackground = false;
                await this.ReleaseAsync(LoggerTab);
            }

            this.closeConfirmed = true;
            this.Close();
        };
    }

    private readonly HelpView helpView;
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

            string message;
            bool loggerHeld = (previous == LoggerTab && this.logger.IsConnected) || this.loggerInBackground;
            if (loggerHeld && (next == LoggerTab || next == TuneTab || next == HelpTab))
            {
                // Logger <-> Tuner (or Help): the Logger keeps its connection so the Tuner can follow the engine live.
                this.loggerInBackground = next != LoggerTab;
                message = next == TuneTab
                    ? "Logger stays connected for the live trace (switch Live on in a table). "
                    : string.Empty;
            }
            else if (loggerHeld)
            {
                this.loggerInBackground = false;
                message = await this.ReleaseAsync(LoggerTab);
            }
            else
            {
                message = await this.ReleaseAsync(previous);
            }

            this.currentTab = next;
            if (next == HelpTab)
            {
                this.helpView.Refresh();
            }

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

    // True while the Logger is still connected although the Tune tab is showing (for the live trace).
    private bool loggerInBackground;

    private string? flashFileHint;
    private string? loggerProfileHint;

    private string ArrivalHint(int tab)
    {
        if (tab == LoggerTab && this.loggerProfileHint != null)
        {
            string hint = $"Profile '{Path.GetFileNameWithoutExtension(this.loggerProfileHint)}' is ready: its path is on the clipboard. " +
                          "Press Connect, then Params → Open Profile and paste the path into the file name box.";
            this.loggerProfileHint = null;
            return hint;
        }

        if (tab == FlashTab && this.flashFileHint != null)
        {
            string hint = $"Ready to flash {Path.GetFileName(this.flashFileHint)}: its path is on the clipboard " +
                          "(paste it into the file picker). Use Select Device to connect, then Write.";
            this.flashFileHint = null;
            return hint;
        }

        string project = this.tune.CurrentProject is { } open
            ? $" Project '{open.DisplayName}': save reads in {open.ReadsFolder}."
            : string.Empty;
        return tab switch
        {
            LoggerTab => "Press Connect to use the adapter here." +
                         (this.tune.CurrentProject is { } p ? $" Recordings go to {p.LogsFolder}." : string.Empty),
            FlashTab => "Use Select Device to connect here." + project,
            _ => string.Empty,
        };
    }
}
