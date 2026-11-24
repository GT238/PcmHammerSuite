using System.Text;
using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>The definition editor's checks: what is accepted, what is refused and what only gets a warning.</summary>
public class DefinitionEditorTests
{
    private static byte[] Bin() => new byte[0x1000];

    private static DefinitionDraft Good() => new()
    {
        Name = "Test VE",
        Category = "Airflow",
        Address = "0x200",
        Rows = "3",
        Columns = "4",
        ElementBits = 16,
        Decimals = "2",
        Equation = "X*0.25",
    };

    private static List<TableDefinition> None => new();

    [Fact]
    public void AGoodDraft_BuildsTheTable()
    {
        Assert.True(Good().TryBuild(Bin(), None, out TableDefinition? t, out List<string> errors, out List<string> warnings));

        Assert.Empty(errors);
        Assert.Empty(warnings);
        Assert.Equal(0x200u, t!.Address);
        Assert.Equal(3, t.Rows);
        Assert.Equal(4, t.Columns);
        Assert.Equal("X*0.25", t.ScalingEquation);
        Assert.True(t.IsBigEndian);
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("zz", "address")]
    [InlineData("0", "address")]
    public void BadNameOrAddress_IsRefused(string value, string field)
    {
        DefinitionDraft d = Good();
        if (field == "name") d.Name = value; else d.Address = value;

        Assert.False(d.TryBuild(Bin(), None, out _, out List<string> errors, out _));
        Assert.Single(errors);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("5000")]
    public void BadRowCount_IsRefused(string rows)
    {
        DefinitionDraft d = Good();
        d.Rows = rows;

        Assert.False(d.TryBuild(Bin(), None, out _, out List<string> errors, out _));
        Assert.Contains(errors, e => e.StartsWith("Rows"));
    }

    [Fact]
    public void AnEquationThatDoesNotParse_IsRefused()
    {
        DefinitionDraft d = Good();
        d.Equation = "X*(0.25";

        Assert.False(d.TryBuild(Bin(), None, out _, out List<string> errors, out _));
        Assert.Contains(errors, e => e.Contains("equation"));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("x*0.25")]
    [InlineData("(X-128)*0.5")]
    [InlineData("X/4+10")]
    [InlineData("(X*0.0001953125)*(28.9647/ref:'Cylinder volume')")]
    [InlineData("")]
    public void CommonEquations_AreAccepted(string equation)
    {
        Assert.True(TableDefinition.CheckEquation(equation, out string error), error);
    }

    [Fact]
    public void ANameAnotherTableHas_IsRefused_ButItsOwnNameIsFine()
    {
        TableDefinition existing = new() { Name = "Test VE", Address = 0x800, ElementBits = 8 };
        DefinitionDraft taken = Good();
        Assert.False(taken.TryBuild(Bin(), new[] { existing }, out _, out List<string> errors, out _));
        Assert.Contains(errors, e => e.Contains("already called"));

        // Editing that table keeps its own name without complaint.
        DefinitionDraft own = DefinitionDraft.From(existing);
        Assert.True(own.TryBuild(Bin(), new[] { existing }, out _, out _, out _));
    }

    [Fact]
    public void RunningPastTheEndOfTheBin_IsAWarning_NotAnError()
    {
        DefinitionDraft d = Good();
        d.Address = "0xFF8";

        Assert.True(d.TryBuild(Bin(), None, out _, out _, out List<string> warnings));
        Assert.Contains(warnings, w => w.Contains("past the end"));
    }

    [Fact]
    public void OverlappingAnotherTable_IsAWarning()
    {
        TableDefinition other = new() { Name = "Other", Address = 0x204, Rows = 2, Columns = 2, ElementBits = 16 };

        Assert.True(Good().TryBuild(Bin(), new[] { other }, out _, out _, out List<string> warnings));
        Assert.Contains(warnings, w => w.Contains("overlap") && w.Contains("Other"));
    }

    [Fact]
    public void TypedHeadings_MustMatchTheCount()
    {
        DefinitionDraft d = Good();
        d.X.Mode = AxisMode.Labels;
        d.X.Labels = "400, 800, 1200";

        Assert.False(d.TryBuild(Bin(), None, out _, out List<string> errors, out _));
        Assert.Contains(errors, e => e.StartsWith("Column headings") && e.Contains("4"));

        d.X.Labels = "400 800 1200 1600";
        Assert.True(d.TryBuild(Bin(), None, out TableDefinition? t, out _, out _));
        Assert.Equal(new[] { "400", "800", "1200", "1600" }, t!.XLabels);
        Assert.True(t.XLabelsAreReal);
    }

    [Fact]
    public void HeadingsFromTheBin_BuildAnAxisSource_AndShowTheBinsNumbers()
    {
        byte[] bin = Bin();
        for (int i = 0; i < 4; i++)
        {
            bin[0x100 + i * 2] = (byte)((1600 * (i + 1)) >> 8);
            bin[0x101 + i * 2] = (byte)(1600 * (i + 1));
        }

        DefinitionDraft d = Good();
        d.X.Mode = AxisMode.FromBin;
        d.X.Address = "0x100";
        d.X.Equation = "X*0.25";

        Assert.True(d.TryBuild(bin, None, out TableDefinition? t, out List<string> errors, out _), string.Join("; ", errors));
        Assert.Equal(4, t!.XAxis!.Count);
        ValueEditorViewModel editor = new(t, bin, null);
        Assert.Equal("400", editor.ColumnLabel(0));
        Assert.Equal("1600", editor.ColumnLabel(3));
    }

    [Fact]
    public void HeadingsFromTheBin_NeedAnAddress()
    {
        DefinitionDraft d = Good();
        d.Y.Mode = AxisMode.FromBin;

        Assert.False(d.TryBuild(Bin(), None, out _, out List<string> errors, out _));
        Assert.Contains(errors, e => e.StartsWith("Row headings"));
    }

    [Fact]
    public void AFlag_NeedsASingleValue_AndAMaskThatFits()
    {
        DefinitionDraft d = Good();
        d.Mask = "0x01";
        Assert.False(d.TryBuild(Bin(), None, out _, out List<string> errors, out _));
        Assert.Contains(errors, e => e.Contains("single value"));

        d.Rows = "1";
        d.Columns = "1";
        d.ElementBits = 8;
        d.Mask = "0x100";
        Assert.False(d.TryBuild(Bin(), None, out _, out errors, out _));
        Assert.Contains(errors, e => e.Contains("wider"));

        d.Mask = "0x80";
        Assert.True(d.TryBuild(Bin(), None, out TableDefinition? t, out _, out _));
        Assert.Equal(0x80u, t!.Mask);
    }

    [Fact]
    public void EditingATable_KeepsWhatTheEditorDoesNotShow()
    {
        TableDefinition original = new()
        {
            Name = "Odd", Address = 0x300, Rows = 2, Columns = 1, ElementBits = 32, SubSubCategory = "Deep", OutputType = 3, MajorStrideBits = 64,
            RowAddressOverrides = new Dictionary<int, uint> { [0] = 0x300, [1] = 0x340 },
        };
        DefinitionDraft d = DefinitionDraft.From(original);
        Assert.True(d.HasSpecialLayout);
        d.Units = "counts";

        Assert.True(d.TryBuild(Bin(), new[] { original }, out TableDefinition? t, out _, out _));
        Assert.Equal("Deep", t!.SubSubCategory);
        Assert.Equal(3, t.OutputType);
        Assert.Equal(64, t.MajorStrideBits);
        Assert.Equal(0x340u, t.RowAddressOverrides![1]);
        Assert.Equal("counts", t.ZUnits);
    }

    [Fact]
    public void FromATable_AndBackWithNoChange_IsTheSameTable()
    {
        TableDefinition original = Good().TryBuild(Bin(), None, out TableDefinition? built, out _, out _) ? built! : throw new InvalidOperationException();
        original.XAxis = new AxisSource { Address = 0x100, Count = 4, ElementBits = 16, ScalingEquation = "X*0.25" };
        original.YLabels = new List<string> { "15", "20", "25" };
        original.YLabelsAreReal = true;

        Assert.True(DefinitionDraft.From(original).TryBuild(Bin(), new[] { original }, out TableDefinition? back, out _, out _));

        Assert.Equal(original.Address, back!.Address);
        Assert.Equal(original.ScalingEquation, back.ScalingEquation);
        Assert.Equal(0x100u, back.XAxis!.Address);
        Assert.Equal(original.YLabels, back.YLabels);
        Assert.True(back.YLabelsAreReal);
    }
}

/// <summary>Applying an edit in the Tuner, keeping it as "my definition", and getting it back for the next BIN.</summary>
[Collection("ScratchEnvironment")]
public class DefinitionEditorFlowTests
{
    private const string Xdf = """
        <?xml version="1.0"?>
        <XDFFORMAT version="1.60">
          <XDFHEADER><flags>0x1</flags><deftitle>test</deftitle><CATEGORY index="0x0" name="Airflow" /></XDFHEADER>
          <XDFTABLE uniqueid="0x1" flags="0x30">
            <title>First</title>
            <CATEGORYMEM index="0" category="1" />
            <XDFAXIS id="x" uniqueid="0x0"><EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" /><indexcount>2</indexcount><LABEL index="0" value="1" /><LABEL index="1" value="2" /><MATH equation="X"><VAR id="X" /></MATH></XDFAXIS>
            <XDFAXIS id="y" uniqueid="0x0"><EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" /><indexcount>1</indexcount><LABEL index="0" value="1" /><MATH equation="X"><VAR id="X" /></MATH></XDFAXIS>
            <XDFAXIS id="z"><EMBEDDEDDATA mmedaddress="0x200" mmedelementsizebits="8" mmedrowcount="1" mmedcolcount="2" mmedmajorstridebits="0" mmedminorstridebits="0" /><decimalpl>0</decimalpl><MATH equation="X"><VAR id="X" /></MATH></XDFAXIS>
          </XDFTABLE>
        </XDFFORMAT>
        """;

    [Fact]
    public async Task AnEdit_IsKept_AsMyDefinition_AndWinsForTheNextBin()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "pcmsuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string? oldData = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        string? oldDocs = Environment.GetEnvironmentVariable("PCMSUITE_DOCS");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        Environment.SetEnvironmentVariable("PCMSUITE_DOCS", scratch);
        try
        {
            byte[] bytes = new byte[0x1000];
            Encoding.ASCII.GetBytes("12345678").CopyTo(bytes, 0x40);
            bytes[0x200] = 100;
            bytes[0x201] = 50;
            bytes[0x300] = 8;
            string bin = Path.Combine(scratch, "car.bin");
            await File.WriteAllBytesAsync(bin, bytes);
            string xdf = Path.Combine(scratch, "12345678 - original.xdf");
            await File.WriteAllTextAsync(xdf, Xdf);

            TuneViewModel tune = new();
            await tune.OpenBinAsync(bin);
            Assert.False(tune.IsBinOnly);
            TableDefinition first = tune.AllTables.Single();

            // Change the scaling of the existing table and add a new one.
            DefinitionDraft edit = DefinitionDraft.From(first);
            edit.Equation = "X*0.5";
            Assert.True(edit.TryBuild(bytes, tune.AllTables, out TableDefinition? changed, out _, out _));
            tune.ApplyDefinition(first, changed);

            DefinitionDraft add = DefinitionDraft.Blank("Airflow", 0x300);
            add.Name = "Added";
            add.Decimals = "0";
            add.ElementBits = 8;
            Assert.True(add.TryBuild(bytes, tune.AllTables, out TableDefinition? added, out _, out _));
            tune.ApplyDefinition(null, added);

            Assert.Equal(2, tune.AllTables.Count);
            Assert.Equal(new[] { "First", "Added" }, tune.AllTables.Select(t => t.Name).ToArray());
            Assert.True(tune.Status.Contains("not changed"), tune.Status);

            await tune.SaveMyDefinitionAsync();
            Assert.True(tune.Status.Contains("my definition"), tune.Status);

            // The next time this BIN is opened, the saved definition is used, not the original XDF.
            TuneViewModel again = new();
            await again.OpenBinAsync(bin);
            Assert.Contains("my definition", again.XdfName);
            Assert.Equal(2, again.AllTables.Count);
            Assert.Equal("X*0.5", again.AllTables.Single(t => t.Name == "First").ScalingEquation);
            Assert.Equal(8, again.AllTables.Single(t => t.Name == "Added").GetRawValue(bytes, 0, 0));
            Assert.Equal(Xdf, await File.ReadAllTextAsync(xdf));   // the original XDF is untouched
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", oldData);
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    [Fact]
    public async Task RenamingATable_KeepsEquationsThatPointAtIt()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "pcmsuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string? oldData = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        string? oldDocs = Environment.GetEnvironmentVariable("PCMSUITE_DOCS");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        Environment.SetEnvironmentVariable("PCMSUITE_DOCS", scratch);
        try
        {
            byte[] bytes = new byte[0x1000];
            Encoding.ASCII.GetBytes("12345678").CopyTo(bytes, 0x40);
            string bin = Path.Combine(scratch, "car.bin");
            await File.WriteAllBytesAsync(bin, bytes);
            await File.WriteAllTextAsync(Path.Combine(scratch, "12345678 - original.xdf"), Xdf);

            TuneViewModel tune = new();
            await tune.OpenBinAsync(bin);
            TableDefinition first = tune.AllTables.Single();

            DefinitionDraft other = DefinitionDraft.Blank("Airflow", 0x300);
            other.Name = "Uses first";
            other.Equation = "X*ref:'First'";
            Assert.True(other.TryBuild(bytes, tune.AllTables, out TableDefinition? user, out _, out _));
            tune.ApplyDefinition(null, user);

            DefinitionDraft rename = DefinitionDraft.From(first);
            rename.Name = "Renamed";
            Assert.True(rename.TryBuild(bytes, tune.AllTables, out TableDefinition? renamed, out _, out _));
            tune.ApplyDefinition(first, renamed);

            Assert.Equal("X*ref:'Renamed'", tune.AllTables.Single(t => t.Name == "Uses first").ScalingEquation);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", oldData);
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }
}
