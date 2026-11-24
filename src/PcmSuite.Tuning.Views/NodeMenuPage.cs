using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>
/// What a right-click (or press and hold) on a table in the list offers: open, pin, tag, edit its definition, copy its name or address.
/// Shown as one of the Tuner's own pages, like the other questions, so it looks the same on every platform and on a phone.
/// </summary>
public sealed class NodeMenuPage : OverlayPage
{
    private readonly ParameterNode node;
    private readonly TuneViewModel vm;
    private readonly Func<string, Task> copy;
    private readonly Func<Task> editDefinition;

    public NodeMenuPage(ParameterNode node, TuneViewModel vm, Func<string, Task> copy, Func<Task> editDefinition)
    {
        this.node = node;
        this.vm = vm;
        this.copy = copy;
        this.editDefinition = editDefinition;
        this.ShowActions();
    }

    public override bool StretchHeight => false;

    private void ShowActions()
    {
        StackPanel list = this.Page(this.node.Name);

        // A folder has nothing to open or pin; it only offers its name.
        if (!this.node.IsCategory)
        {
            list.Children.Add(this.Item("Open", () => this.vm.SelectedNode = this.node));
        }

        if (this.node.Table != null)
        {
            list.Children.Add(this.Item(this.node.IsPinned ? "Unpin" : "Pin", () =>
            {
                bool pinned = !this.node.IsPinned;
                this.vm.SelectedNode = this.node;
                this.vm.SelectedPinned = pinned;
            }));

            Button tag = this.Item("Tag as...", null);
            tag.Click += (_, _) => this.ShowTags();
            list.Children.Add(tag);

            Button define = this.Item("Edit definition...", null);
            define.Click += async (_, _) =>
            {
                this.RequestClose();
                await this.editDefinition();
            };
            list.Children.Add(define);
        }

        list.Children.Add(this.Item("Copy name", () => _ = this.copy(this.node.Name)));
        if (this.node.Table != null)
        {
            list.Children.Add(this.Item("Copy address", () => _ = this.copy($"0x{this.node.Table.Address:X6}")));
        }

        this.AddCancel(list);
    }

    private void ShowTags()
    {
        StackPanel list = this.Page("Tag " + this.node.Name);
        foreach (string choice in TuneViewModel.TagChoices)
        {
            string value = choice;
            list.Children.Add(this.Item(value == "(none)" ? "Remove tag" : value, () =>
            {
                this.vm.SelectedNode = this.node;
                this.vm.SelectedTag = value;
            }));
        }

        this.AddCancel(list);
    }

    private StackPanel Page(string title)
    {
        StackPanel list = new() { Spacing = 6 };
        list.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });
        this.Content = new ScrollViewer { Content = list, MaxHeight = 560 };
        return list;
    }

    /// <summary>A full-width button. With an action, pressing it does that and closes the page.</summary>
    private Button Item(string text, Action? action)
    {
        Button button = new()
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        if (action != null)
        {
            button.Click += (_, _) =>
            {
                this.RequestClose();
                action();
            };
        }

        return button;
    }

    private void AddCancel(StackPanel list)
    {
        Button cancel = new() { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        cancel.Click += (_, _) => this.RequestClose();
        list.Children.Add(cancel);
    }
}
