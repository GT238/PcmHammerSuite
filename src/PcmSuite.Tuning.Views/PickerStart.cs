using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>Where a file picker for a stock or reference BIN should open.</summary>
internal static class PickerStart
{
    /// <summary>The Reference BINs folder when it holds any BINs, otherwise the fallback folder (or nothing, and the system decides).</summary>
    public static async Task<IStorageFolder?> StockBinsAsync(TopLevel top, string? fallbackFolder = null)
    {
        string? folder = StockBins.HasFiles() ? StockBins.Folder : fallbackFolder;
        return folder != null && Directory.Exists(folder)
            ? await top.StorageProvider.TryGetFolderFromPathAsync(folder)
            : null;
    }
}
