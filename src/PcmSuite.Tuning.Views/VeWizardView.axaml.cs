using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace PcmSuite.Tune;

public partial class VeWizardView : OverlayPage
{
    private readonly VeWizardViewModel model;
    private readonly Func<string?>? logFolder;

    public VeWizardView()
    {
        // Only for the XAML designer.
        this.model = null!;
        this.InitializeComponent();
    }

    public VeWizardView(VeWizardViewModel model, Func<string?>? logFolder)
    {
        this.model = model;
        this.logFolder = logFolder;
        this.InitializeComponent();
        this.DataContext = model;
    }

    /// <summary>True when the wizard changed the table.</summary>
    public bool Applied { get; private set; }

    private async void OnOpenLog(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        IStorageFolder? start = null;
        string? folder = this.logFolder?.Invoke();
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            start = await top.StorageProvider.TryGetFolderFromPathAsync(folder);
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a log file",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("Log files") { Patterns = new[] { "*.csv" } } },
        });

        string? path = files.Count == 1 ? files[0].TryGetLocalPath() : null;
        if (path != null)
        {
            await this.model.LoadAsync(path);
        }
    }

    private async void OnNewestLog(object? sender, RoutedEventArgs e)
    {
        string? folder = this.logFolder?.Invoke();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            this.model.Status = "The Logger's log folder does not exist yet. Record a log in the Logger tab first.";
            return;
        }

        string? newest = new DirectoryInfo(folder).GetFiles("*.csv")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
        if (newest == null)
        {
            this.model.Status = "No log files in " + folder;
            return;
        }

        await this.model.LoadAsync(newest);
    }

    private void OnCalculate(object? sender, RoutedEventArgs e) => this.model.Calculate();

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        this.Applied = this.model.Apply() > 0;
        this.RequestClose();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => this.RequestClose();
}
