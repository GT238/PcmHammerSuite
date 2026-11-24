using Avalonia.Controls;

namespace PcmSuite;

/// <summary>
/// The Tuner on its own. It is the same TuneViewModel and TuneView the full suite hosts; this shell leaves out the pieces that
/// need an adapter (live trace, Send to Flash, reading the PCM) and says where to go instead.
/// </summary>
public partial class MainWindow : Window
{
    private const int TuneTab = 0;
    private const int HelpTab = 1;

    private readonly HelpView helpView;
    private readonly Tune.TuneViewModel tune;
    private readonly Tune.TuneView tuneView;
    private bool closeConfirmed;

    // A BIN or XDF given on the command line (for example "Open with" from Explorer), opened at start.
    private readonly string? startupFile;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupFile)
    {
        this.startupFile = startupFile is { Length: > 0 } && File.Exists(startupFile) ? Path.GetFullPath(startupFile) : null;
        InitializeComponent();

        this.Opened += async (_, _) =>
        {
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

            try
            {
                await this.tune.RestoreLastProjectAsync(this.startupFile == null);
                if (this.startupFile != null)
                {
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

        // No Logger here: log files come from the project's logs folder, and there is no live source.
        this.tune = new Tune.TuneViewModel
        {
            ProfilesFolderProvider = () => Tune.LogProfileCatalog.SharedFolder,
        };
        this.tune.LogFolderProvider = () => this.tune.CurrentProject?.LogsFolder;

        Tune.LogProfileCatalog.Seed(Path.Combine(AppContext.BaseDirectory, "Profiles"), Tune.LogProfileCatalog.SharedFolder);

        this.tuneView = new Tune.TuneView { DataContext = this.tune };
        this.TuneHost.Content = this.tuneView;
        this.helpView = new HelpView();
        this.HelpHost.Content = this.helpView;
        this.CreditsHost.Content = new CreditsView();

        this.Tabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this.Tabs) && this.Tabs.SelectedIndex == HelpTab)
            {
                this.helpView.Refresh();
            }
        };

        this.tune.HelpRequested += topic =>
        {
            string[] parts = topic.Split('#', 2);
            this.helpView.Show(parts[0], parts.Length > 1 ? parts[1] : null);
            this.Tabs.SelectedIndex = HelpTab;
        };

        // The pieces that need the other apps: say where the file is instead.
        this.tune.SendToFlashRequested += path =>
            this.ShellStatus.Text = $"Open {path} in PCM Hammer (or the full PcmSuite) to flash it.";
        this.tune.FlashReadRequested += project =>
            this.ShellStatus.Text = $"Read the PCM with PCM Hammer, then save the file in {project.ReadsFolder} and open it here.";
        this.tune.UseProfileRequested += path =>
            this.ShellStatus.Text = $"Open the logging profile {path} in PCM Logger (or the full PcmSuite).";

        // Closing must not lose unsaved edits.
        this.Closing += async (_, e) =>
        {
            if (this.closeConfirmed || !this.tune.IsDirty)
            {
                return;
            }

            e.Cancel = true;
            this.Tabs.SelectedIndex = TuneTab;
            if (!await this.tuneView.ConfirmDiscardAsync())
            {
                return;
            }

            this.closeConfirmed = true;
            this.Close();
        };
    }
}
