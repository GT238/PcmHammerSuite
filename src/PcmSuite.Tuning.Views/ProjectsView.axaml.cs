using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>The list of vehicle projects: open one, make a new one, or close the current one.</summary>
public partial class ProjectsView : OverlayPage
{
    private readonly TuneViewModel? model;

    public ProjectsView()
    {
        // Only for the XAML designer.
        this.InitializeComponent();
    }

    public ProjectsView(TuneViewModel model)
    {
        this.model = model;
        this.InitializeComponent();
        this.RootText.Text = "Projects folder: " + ProjectInfo.DefaultProjectsRoot;
        this.Reload();
    }

    private void Reload()
    {
        this.List.ItemsSource = ProjectInfo.ListProjects();
        this.CloseProjectButton.IsVisible = this.model?.HasProject == true;
        this.Message.Text = ProjectInfo.ListProjects().Count == 0
            ? "No projects yet. Make one with New project..."
            : this.model?.HasProject == true ? "Open: " + this.model.ProjectTitle : string.Empty;
    }

    /// <summary>Switching projects opens another BIN, so unsaved edits must not be thrown away silently.</summary>
    private async Task<bool> CanLeaveBinAsync()
    {
        if (this.model is not { IsDirty: true } || this.Confirm == null)
        {
            return true;
        }

        return await this.Confirm("Unsaved edits", "The open BIN has edits that are not saved. Switching project opens a different BIN and loses them.", "Discard and switch");
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ProjectInfo project } && this.model != null && await this.CanLeaveBinAsync())
        {
            this.RequestClose();
            await this.model.OpenProjectAsync(project);
        }
    }

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (this.model == null || this.Host is not { } host)
        {
            return;
        }

        if (!await this.CanLeaveBinAsync())
        {
            return;
        }

        NewProjectView form = new(this.model);
        await host(form);
        if (form.Created is { } project)
        {
            this.RequestClose();
            await this.model.OpenProjectAsync(project);
            if (form.StartChoice == NewProjectView.ReadFromPcm)
            {
                this.model.RequestFlashRead(project);
            }
        }
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || this.model == null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a project folder (it contains project.json)",
            AllowMultiple = false,
        });

        string? folder = folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
        if (folder == null)
        {
            return;
        }

        ProjectInfo? project = ProjectInfo.Load(folder);
        if (project == null)
        {
            // A folder made by hand: offer to make it a project, leaving its files exactly where they are.
            if (this.Confirm == null
                || !await this.Confirm("Make this a project?", $"{folder} is not a project yet (it has no project.json). Make it one? Its files stay where they are; the project's folders are added beside them.", "Make it a project"))
            {
                return;
            }

            project = this.model.AdoptFolder(folder);
        }

        if (await this.CanLeaveBinAsync())
        {
            this.RequestClose();
            await this.model.OpenProjectAsync(project);
        }
    }

    private async void OnOpenRoot(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(ProjectInfo.DefaultProjectsRoot);
            await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(ProjectInfo.DefaultProjectsRoot));
        }
        catch (Exception ex)
        {
            this.Message.Text = "Could not open the folder: " + ex.Message;
        }
    }

    private void OnCloseProject(object? sender, RoutedEventArgs e)
    {
        this.model?.CloseProject();
        this.Reload();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => this.RequestClose();

    /// <summary>Set by the host so this page can open the new-project form over itself.</summary>
    public Func<OverlayPage, Task>? Host { get; set; }
}
