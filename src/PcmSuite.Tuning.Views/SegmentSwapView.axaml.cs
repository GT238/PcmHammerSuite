using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

public partial class SegmentSwapView : OverlayPage
{
    private readonly TuneViewModel tune;
    private readonly SegmentSwapViewModel model;

    public SegmentSwapView()
    {
        // Only for the XAML designer.
        this.tune = new TuneViewModel();
        this.model = new SegmentSwapViewModel("(none)", Array.Empty<byte>());
        this.InitializeComponent();
    }

    public SegmentSwapView(TuneViewModel tune, SegmentSwapViewModel model)
    {
        this.tune = tune;
        this.model = model;
        this.InitializeComponent();
        this.DataContext = model;
    }

    private async void OnChooseDonor(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the donor BIN",
            AllowMultiple = false,
            SuggestedStartLocation = await PickerStart.StockBinsAsync(top),
            FileTypeFilter = new[] { new FilePickerFileType("BIN files") { Patterns = new[] { "*.bin", "*.BIN" } } },
        });

        string? path = files.Count == 1 ? files[0].TryGetLocalPath() : null;
        if (path != null)
        {
            await this.model.LoadDonorAsync(path);
        }
    }

    private void OnSelectDifferent(object? sender, RoutedEventArgs e) => this.model.SelectDifferent();

    private void OnSelectNone(object? sender, RoutedEventArgs e) => this.model.SelectNone();

    private void OnClose(object? sender, RoutedEventArgs e) => this.RequestClose();

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<SegmentInfo> picked = this.model.Selected;
        if (picked.Count == 0 || this.model.Donor == null)
        {
            this.model.Status = "Tick at least one segment first.";
            return;
        }

        // Different (or unidentifiable) operating systems and identity segments are never copied without an explicit yes.
        SwapReview? review = this.model.Review();
        if (review != null)
        {
            if (this.Confirm == null)
            {
                this.model.Status = "Not copied: " + review.Title + ". " + review.Message;
                return;
            }

            if (!await this.Confirm(review.Title, review.Message, review.Danger ? "Copy despite the mismatch" : "Copy anyway"))
            {
                return;
            }
        }

        await this.tune.ApplySegmentsAsync(this.model.Donor, picked, this.model.DonorName);
        this.RequestClose();
    }
}
