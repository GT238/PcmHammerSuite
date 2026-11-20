using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PcmSuite.Tune;

/// <summary>
/// A dialog-like page shown on top of the Tune screen (not in a separate window), so the same screens work
/// in a single-view host such as a phone. The host is <see cref="TuneView.ShowOverlayAsync"/>.
/// </summary>
public abstract class OverlayPage : UserControl
{
    public event EventHandler? CloseRequested;

    /// <summary>Set by the host: shows a Yes/No question over everything and returns the answer.</summary>
    public Func<string, string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>True for pages that fill the available height (with their own scrolling area).</summary>
    public virtual bool StretchHeight => true;

    protected void RequestClose() => this.CloseRequested?.Invoke(this, EventArgs.Empty);
}

/// <summary>A short question with two buttons.</summary>
public sealed class ConfirmPage : OverlayPage
{
    public ConfirmPage(string title, string text, string yesLabel, string noLabel)
    {
        Button yes = new() { Content = yesLabel };
        Button no = new() { Content = noLabel, IsDefault = true };
        yes.Click += (_, _) =>
        {
            this.Answer = true;
            this.RequestClose();
        };
        no.Click += (_, _) => this.RequestClose();

        this.Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { yes, no },
                },
            },
        };
    }

    public bool Answer { get; private set; }

    public override bool StretchHeight => false;
}

/// <summary>A question with several buttons; <see cref="Choice"/> is the index of the one pressed (-1 if closed another way).</summary>
public sealed class ChoicePage : OverlayPage
{
    public ChoicePage(string title, string text, params string[] labels)
    {
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = new() { Content = labels[i], IsDefault = i == 0 };
            button.Click += (_, _) =>
            {
                this.Choice = index;
                this.RequestClose();
            };
            buttons.Children.Add(button);
        }

        this.Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                buttons,
            },
        };
    }

    public int Choice { get; private set; } = -1;

    public override bool StretchHeight => false;
}
