using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace PcmSuite.Tune;

public partial class ProfilesView : OverlayPage
{
    private readonly string folder = string.Empty;
    private readonly Action<string>? useProfile;

    public ProfilesView()
    {
        // Only for the XAML designer.
        this.InitializeComponent();
    }

    public ProfilesView(string folder, Action<string> useProfile)
    {
        this.folder = folder;
        this.useProfile = useProfile;
        this.InitializeComponent();
        this.FolderText.Text = "Folder: " + folder;
        this.Reload();
    }

    private void Reload()
    {
        IReadOnlyList<LogProfileInfo> profiles = LogProfileCatalog.Load(this.folder);
        this.List.ItemsSource = profiles;
        this.Empty.Text = profiles.Count == 0 ? "No profiles in the shared folder yet." : string.Empty;
    }

    private void OnUse(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LogProfileInfo profile })
        {
            this.useProfile?.Invoke(profile.Path);
            this.RequestClose();
        }
    }

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a profile to add",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Log profiles") { Patterns = new[] { "*.LogProfile" } } },
        });

        string? path = files.Count == 1 ? files[0].TryGetLocalPath() : null;
        if (path == null)
        {
            return;
        }

        try
        {
            LogProfileCatalog.Import(path, this.folder);
            this.Reload();
        }
        catch (Exception ex)
        {
            this.Empty.Text = "Could not add the profile: " + ex.Message;
        }
    }

    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(this.folder);
            await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(this.folder));
        }
        catch (Exception ex)
        {
            this.Empty.Text = "Could not open the folder: " + ex.Message;
        }
    }

    private async void OnCopyFolder(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(this.folder);
            this.Empty.Text = "Folder path copied.";
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => this.RequestClose();
}
