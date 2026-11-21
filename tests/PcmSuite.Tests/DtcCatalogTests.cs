using PcmSuite.Tuning.Core.Models;
using Xunit;

namespace PcmSuite.Tests;

public class DtcCatalogTests
{
    [Theory]
    [InlineData("P0101", "MAF")]
    [InlineData("p0420", "Catalyst")]
    [InlineData("  P0171  ", "lean")]
    [InlineData("P0101 extra words", "MAF")]
    public void KnownCodes_AreDescribed(string label, string expected) =>
        Assert.Contains(expected, DtcCatalog.Describe(label), StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("P1101")]   // GM-specific: deliberately not described
    [InlineData("P0999")]
    [InlineData("Main VE Table")]
    [InlineData("400")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownLabels_GiveNothing(string? label) => Assert.Null(DtcCatalog.Describe(label));

    [Fact]
    public void Catalog_HasNoDuplicatesAndOnlyGenericCodes()
    {
        // A duplicate would have thrown when the catalog was built, so reaching here means there are none.
        Assert.True(DtcCatalog.Count > 150);
        foreach (string code in new[] { "P0016", "P0300", "P0700", "P2101", "U0107" })
        {
            Assert.NotNull(DtcCatalog.Describe(code));
        }

        // Generic codes only: P1xxx and P3xxx are manufacturer-specific.
        for (int n = 0x1000; n < 0x2000; n++)
        {
            Assert.Null(DtcCatalog.Describe("P" + n.ToString("X4")));
        }
    }
}
