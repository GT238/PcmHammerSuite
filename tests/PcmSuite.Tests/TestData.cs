using Xunit;

namespace PcmSuite.Tests;

/// <summary>Where the real BIN and XDF files live, and a way to skip tests when they are not there.</summary>
internal static class TestData
{
    /// <summary>The repository root: the folder above "tests" that holds PcmSuite.sln.</summary>
    public static string Root { get; } = FindRoot();

    public static string BinFolder => Environment.GetEnvironmentVariable("PCMSUITE_TEST_BIN") ?? Path.Combine(Root, "Bin");

    public static string XdfFolder => Environment.GetEnvironmentVariable("PCMSUITE_TEST_XDF") ?? Path.Combine(Root, "XDF");

    /// <summary>The Tuner's own XDF library, where the readable (unprotected) XDFs usually are.</summary>
    public static string XdfLibrary => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PcmSuite Projects", "XDFs");

    /// <summary>The first file in the Bin folder whose name matches the pattern, or null.</summary>
    public static string? Bin(string pattern) => First(BinFolder, pattern);

    public static string? Xdf(string pattern) => First(XdfFolder, pattern) ?? First(XdfLibrary, pattern);

    private static string? First(string folder, string pattern) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : null;

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PcmSuite.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}

/// <summary>A test that needs a real file: it shows as skipped, not passed, when the file is missing.</summary>
public sealed class RealBinFactAttribute : FactAttribute
{
    public RealBinFactAttribute(string binPattern)
    {
        if (TestData.Bin(binPattern) == null)
        {
            this.Skip = $"No BIN matching '{binPattern}' in {TestData.BinFolder}";
        }
    }
}

public sealed class RealXdfFactAttribute : FactAttribute
{
    public RealXdfFactAttribute(string xdfPattern)
    {
        if (TestData.Xdf(xdfPattern) == null)
        {
            this.Skip = $"No XDF matching '{xdfPattern}' in {TestData.XdfFolder}";
        }
    }
}
