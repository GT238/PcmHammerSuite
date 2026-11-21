using PcmSuite.Tune;
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

public class DtcSearchTests
{
    [Theory]
    [InlineData("P0420", "P0420", true)]
    [InlineData("P0420", "0420", true)]
    [InlineData("P0420", "p04", true)]
    [InlineData("P0420", "catalyst", true)]
    [InlineData("P0420", "MAF", false)]
    [InlineData("P1101", "P11", true)]          // matched by code even without a description
    [InlineData("P1101", "catalyst", false)]
    [InlineData("400", "40", false)]            // an RPM axis label is not a trouble code
    [InlineData("P0420", "", false)]
    [InlineData(null, "P0420", false)]
    public void Matches_ByCodeOrDescription(string? label, string? text, bool expected) =>
        Assert.Equal(expected, DtcCatalog.Matches(label, text));

    [Fact]
    public void TableMentions_OnlyWhenARowIsACodeThatMatches()
    {
        TableDefinition dtc = new() { YLabels = new List<string> { "P0101", "P0420", "P1101" } };
        TableDefinition ve = new() { XLabels = new List<string> { "400", "800" }, YLabels = new List<string> { "15", "20" } };

        Assert.True(DtcCatalog.TableMentions(dtc, "P0420"));
        Assert.True(DtcCatalog.TableMentions(dtc, "catalyst"));
        Assert.False(DtcCatalog.TableMentions(dtc, "P9"));
        Assert.False(DtcCatalog.TableMentions(ve, "40"));
        Assert.False(DtcCatalog.TableMentions(dtc, "P"));       // one character is too vague
    }

    [Fact]
    public void IsCode_ShapeOnly()
    {
        Assert.True(DtcCatalog.IsCode("P1101"));
        Assert.True(DtcCatalog.IsCode("U0107 x"));
        Assert.False(DtcCatalog.IsCode("Main VE"));
        Assert.False(DtcCatalog.IsCode("1500"));
    }
}

public class TroubleCodeListTests
{
    private static ValueEditorViewModel Editor(int columns, params string[] rowLabels) =>
        new(new TableDefinition
        {
            Rows = rowLabels.Length,
            Columns = columns,
            ElementBits = 8,
            XLabels = new List<string> { "0.00" },
            YLabels = rowLabels.ToList(),
        }, new byte[64], null);

    [Fact]
    public void SingleColumnOfCodes_HidesThePlaceholderColumnHeading()
    {
        ValueEditorViewModel editor = Editor(1, "P0101", "P0102", "P0103");
        Assert.True(editor.IsTroubleCodeList);
        Assert.False(editor.HasColumnLabels);
        Assert.True(editor.HasRowLabels);
    }

    [Fact]
    public void OtherTables_KeepTheirHeadings()
    {
        Assert.True(Editor(3, "P0101", "P0102").HasColumnLabels);          // several columns: the headings mean something
        Assert.True(Editor(1, "15", "20", "25").HasColumnLabels);          // one column but not a code list
        Assert.False(Editor(1, "15", "20", "25").IsTroubleCodeList);
    }
}
