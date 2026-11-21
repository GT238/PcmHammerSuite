using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using PCMHammerAvalonia.ViewModels;

namespace PcmSuite;

/// <summary>
/// PCM Hammer's desktop flasher layout in a control (see the note in the XAML). Keeps the two log boxes scrolled to the
/// newest line while you are at the bottom, as the flasher's own views do.
/// </summary>
public partial class FlashDesktopView : UserControl
{
    private MainWindowViewModel? model;

    public FlashDesktopView()
    {
        this.InitializeComponent();
        this.DataContextChanged += (_, _) => this.Attach();
    }

    private void Attach()
    {
        if (this.model != null)
        {
            this.model.PropertyChanged -= this.OnChanged;
        }

        this.model = this.DataContext as MainWindowViewModel;
        if (this.model != null)
        {
            this.model.PropertyChanged += this.OnChanged;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        ScrollViewer? scroll = e.PropertyName switch
        {
            nameof(MainWindowViewModel.LogText) => this.ResultsScroll,
            nameof(MainWindowViewModel.DebugLogText) => this.DebugScroll,
            _ => null,
        };

        if (scroll == null)
        {
            return;
        }

        // Only follow the log when the reader is already at (or near) the end, so scrolling up to read is not undone.
        bool atEnd = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 24;
        if (atEnd)
        {
            Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
        }
    }
}
