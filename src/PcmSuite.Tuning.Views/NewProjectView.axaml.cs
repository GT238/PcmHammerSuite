using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>The form for a new vehicle project.</summary>
public partial class NewProjectView : OverlayPage
{
    private readonly TuneViewModel? model;

    public NewProjectView()
    {
        // Only for the XAML designer.
        this.InitializeComponent();
    }

    public NewProjectView(TuneViewModel model)
    {
        this.model = model;
        this.InitializeComponent();
        this.WhereText.Text = "It will be made inside " + ProjectInfo.DefaultProjectsRoot;
    }

    public const int ReadFromPcm = 0;
    public const int FromFile = 1;
    public const int Later = 2;

    /// <summary>The project that was made, or null when the form was cancelled.</summary>
    public ProjectInfo? Created { get; private set; }

    /// <summary>How the tune starts: <see cref="ReadFromPcm"/>, <see cref="FromFile"/> or <see cref="Later"/>.</summary>
    public int StartChoice { get; private set; }

    private string? startBin;

    private async void OnBrowseStart(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the BIN to start from",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("BIN files") { Patterns = new[] { "*.bin", "*.BIN" } } },
        });

        this.startBin = files.Count == 1 ? files[0].TryGetLocalPath() : null;
        this.StartPath.Text = this.startBin;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (this.model == null)
        {
            return;
        }

        ProjectInfo draft = new()
        {
            Name = (this.NameBox.Text ?? string.Empty).Trim(),
            Year = (this.YearBox.Text ?? string.Empty).Trim(),
            Make = (this.MakeBox.Text ?? string.Empty).Trim(),
            Model = (this.ModelBox.Text ?? string.Empty).Trim(),
            Engine = (this.EngineBox.Text ?? string.Empty).Trim(),
            Vin = (this.VinBox.Text ?? string.Empty).Trim(),
            Notes = (this.NotesBox.Text ?? string.Empty).Trim(),
        };

        if (draft.Name.Length == 0 && draft.Summary == "(no vehicle details)")
        {
            this.Problem.Text = "Give the project a name, or fill in the vehicle.";
            return;
        }

        int choice = this.StartFile.IsChecked == true ? FromFile : this.StartLater.IsChecked == true ? Later : ReadFromPcm;
        if (choice == FromFile && (this.startBin == null || !File.Exists(this.startBin)))
        {
            this.Problem.Text = "Choose the BIN to start from, or pick another way to start.";
            return;
        }

        try
        {
            this.Created = this.model.CreateProject(draft, choice == FromFile ? this.startBin : null);
            this.StartChoice = choice;
            this.RequestClose();
        }
        catch (Exception ex)
        {
            this.Problem.Text = "Could not create the project: " + ex.Message;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => this.RequestClose();
}
