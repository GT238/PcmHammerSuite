using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>
/// Golden tests: known XDFs must keep producing the same tables. These files are not in the repository; each test is
/// skipped when its file is not found in the XDF or Bin folders or the Tuner's XDF library.
/// </summary>
public class XdfParserTests
{
    private const string P01Xdf = "12212156 - 2002 512k V1.xdf";
    private const string P59Xdf = "12587603 -Latest Release.xdf";

    private static List<TableDefinition> Load(string name) => XdfParser.Parse(TestData.Xdf(name)!);

    [RealXdfFact(P01Xdf)]
    public void P01_Xdf_HasTheExpectedTables()
    {
        List<TableDefinition> tables = Load(P01Xdf);
        Assert.Equal(363, tables.Count);
        Assert.Empty(XdfParser.ParsePatches(TestData.Xdf(P01Xdf)!));
    }

    [RealXdfFact(P01Xdf)]
    public void P01_MainVeTable_HasTheExpectedShapeAxesAndScaling()
    {
        TableDefinition ve = Load(P01Xdf).Single(t => t.Name.StartsWith("B0101 - Main VE Table"));

        Assert.Equal(0x8360u, ve.Address);
        Assert.Equal(19, ve.Rows);
        Assert.Equal(20, ve.Columns);
        Assert.Equal(16, ve.ElementBits);
        Assert.Equal(4, ve.DecimalPlaces);
        Assert.Equal("X*0.0001953125", ve.ScalingEquation);
        Assert.Equal("Airflow", ve.Category);

        // RPM across the top, MAP down the side, both with real labels from the XDF.
        Assert.True(ve.XLabelsAreReal);
        Assert.True(ve.YLabelsAreReal);
        Assert.Equal(20, ve.XLabels.Count);
        Assert.Equal(19, ve.YLabels.Count);
        Assert.Equal("400", ve.XLabels[0]);
        Assert.Equal("8000", ve.XLabels[^1]);
        Assert.Equal("15", ve.YLabels[0]);
        Assert.Equal("105", ve.YLabels[^1]);
    }

    [RealXdfFact(P59Xdf)]
    public void P59_Xdf_HasTheExpectedTablesAndPatches()
    {
        string path = TestData.Xdf(P59Xdf)!;
        Assert.Equal(1439, XdfParser.Parse(path).Count);
        Assert.Equal(2, XdfParser.ParsePatches(path).Count);
    }

    [RealXdfFact(P59Xdf)]
    public void P59_MainVeTable_UsesACrossReferenceInItsScaling()
    {
        TableDefinition ve = Load(P59Xdf).First(t => t.Name.StartsWith("B0101 - Main VE Table"));

        Assert.Equal(0x8442u, ve.Address);
        Assert.Contains("ref:'B0104 - Cylinder Volume constant'", ve.ScalingEquation);
        Assert.Equal(3, ve.DecimalPlaces);
        Assert.Equal("400", ve.YLabels[0]);
        Assert.Equal("15", ve.XLabels[0]);
    }

    [Fact]
    public void ProtectedXdf_IsRefusedCleanly_NotParsedAsGarbage()
    {
        string path = Path.Combine(Path.GetTempPath(), "pcmsuite-protected-" + Guid.NewGuid().ToString("N") + ".xdf");
        try
        {
            // The header TunerPro writes on a protected (encrypted) XDF, followed by bytes that are not XML.
            File.WriteAllBytes(path, new byte[] { 0x05, 0x22, 0x97, 0x01, 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x13, 0x37 });
            Assert.ThrowsAny<Exception>(() => XdfParser.Parse(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── A real BIN with its XDF: the numbers must come out in a believable range ───────────────

    [RealBinFact("Impala_LS2_P1_Modified_v3.bin")]
    public void P01_Bin_MainVeTable_ReadsBelievableValues()
    {
        if (TestData.Xdf(P01Xdf) == null)
        {
            return;   // needs both files; the XDF half has its own skipped test
        }

        byte[] bin = File.ReadAllBytes(TestData.Bin("Impala_LS2_P1_Modified_v3.bin")!);
        TableDefinition ve = Load(P01Xdf).Single(t => t.Name.StartsWith("B0101 - Main VE Table"));

        for (int r = 0; r < ve.Rows; r++)
        {
            for (int c = 0; c < ve.Columns; c++)
            {
                double value = ve.GetScaledValue(bin, r, c);
                Assert.InRange(value, 0.3, 3.5);   // grams*kelvin/kPa for an LS engine
            }
        }

        // Editing a cell through the table's own methods changes exactly that cell and reads back the same.
        byte[] copy = (byte[])bin.Clone();
        double wanted = ve.GetScaledValue(copy, 5, 5) * 1.05;
        ve.SetRawValue(copy, 5, 5, ve.InverseScale(wanted));
        Assert.Equal(wanted, ve.GetScaledValue(copy, 5, 5), 3);
        Assert.Equal(1, ve.CountChanges(copy, bin));
    }
}
