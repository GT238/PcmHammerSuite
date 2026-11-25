using PcmSuite.Tune;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;
using PcmSuite.Tuning.Core.Services;
using Xunit;

namespace PcmSuite.Tests;

/// <summary>Finding where a definition's tables went in a BIN of a similar OS.</summary>
public class DefinitionMigratorTests
{
    private static TableDefinition Table(string name, int address, int rows = 4, int columns = 4, int bits = 16) => new()
    {
        Name = name, Address = (uint)address, Rows = rows, Columns = columns, ElementBits = bits,
    };

    /// <summary>A BIN of arbitrary bytes, with a flat run of zeros at the end (where nothing can be recognised by its content).</summary>
    private static byte[] Source()
    {
        byte[] bin = new byte[0x1200];
        new Random(1234).NextBytes(bin);
        Array.Clear(bin, 0xE00, 0x400);
        return bin;
    }

    /// <summary>The same BIN with ten bytes inserted at 0x500 (everything after it moves up by 10) and two cells of one table changed.</summary>
    private static byte[] Target(byte[] source)
    {
        byte[] bin = new byte[source.Length];
        Array.Copy(source, 0, bin, 0, 0x500);
        new Random(99).NextBytes(bin.AsSpan(0x500, 10));
        Array.Copy(source, 0x500, bin, 0x50A, source.Length - 0x50A);
        bin[0x80A + 4] ^= 0xFF;     // two cells of the table that starts at 0x800 (now 0x80A) were tuned
        bin[0x80A + 9] ^= 0x55;
        return bin;
    }

    private static List<TableDefinition> Tables() => new()
    {
        Table("before", 0x100),
        Table("also before", 0x400),
        Table("tiny between", 0x600, 1, 1, 16),     // between a table that did not move and one that did: unknowable
        Table("after tuned", 0x800),
        Table("tiny inside", 0xA00, 1, 1, 16),      // between two that moved by the same amount
        Table("after", 0xC00),
        Table("flat", 0xE00),                        // all zeros: matches everywhere
    };

    [Fact]
    public void TablesThatMovedTheSame_AreFoundByTheirBytes_EvenWhenAFewCellsWereTuned()
    {
        byte[] a = Source();
        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a), Tables());

        TableMatch before = result.Matches.Single(m => m.Source.Name == "before");
        Assert.Equal(MatchKind.Content, before.Kind);
        Assert.Equal(0, before.Shift);

        TableMatch tuned = result.Matches.Single(m => m.Source.Name == "after tuned");
        Assert.Equal(MatchKind.Content, tuned.Kind);
        Assert.Equal(10, tuned.Shift);
        Assert.InRange(tuned.Similarity, 0.9, 0.99);

        Assert.Equal(10, result.Matches.Single(m => m.Source.Name == "after").Shift);
    }

    [Fact]
    public void ASmallTable_BetweenNeighboursThatMovedAlike_MovesWithThem()
    {
        byte[] a = Source();
        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a), Tables());

        TableMatch tiny = result.Matches.Single(m => m.Source.Name == "tiny inside");

        Assert.Equal(MatchKind.Inferred, tiny.Kind);
        Assert.Equal(10, tiny.Shift);
        Assert.Equal(1.0, tiny.Similarity);
    }

    [Fact]
    public void ASmallTable_WhereTheShiftChangesBetweenNeighbours_IsNotGuessed()
    {
        byte[] a = Source();
        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a), Tables());

        Assert.Equal(MatchKind.Unresolved, result.Matches.Single(m => m.Source.Name == "tiny between").Kind);
    }

    [Fact]
    public void ARepeatingTable_WhereTheShiftChanges_GoesToTheShiftThatFitsItsBytes()
    {
        // A table made of a repeating pattern fits at many shifts, so its bytes alone cannot place it. Its neighbours say it is at 0 or +10 (two tables
        // moved by each), and only +10 lines the pattern up.
        byte[] a = Source();
        for (int i = 0x580; i < 0x700; i++)
        {
            a[i] = (byte)(1 + (i - 0x580) % 4);
        }

        List<TableDefinition> tables = Tables();
        tables.Add(Table("periodic", 0x600));

        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a), tables);

        TableMatch periodic = result.Matches.Single(m => m.Source.Name == "periodic");
        Assert.Equal(MatchKind.Inferred, periodic.Kind);
        Assert.Equal(10, periodic.Shift);
        Assert.Equal(1.0, periodic.Similarity);
    }

    [Fact]
    public void AShiftOnlyOneTableHas_IsNotTrustedToPlaceOthers()
    {
        // Only one table moved by +10 here (one match, not two), so a table between it and the unmoved ones is not placed by that shift.
        byte[] a = Source();
        for (int i = 0x580; i < 0x700; i++)
        {
            a[i] = (byte)(1 + (i - 0x580) % 4);
        }

        List<TableDefinition> tables = new()
        {
            Table("before", 0x100), Table("also before", 0x400), Table("periodic", 0x600), Table("only one after", 0x800),
        };

        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a), tables);

        Assert.Equal(MatchKind.Unresolved, result.Matches.Single(m => m.Source.Name == "periodic").Kind);
    }

    private static byte[] Random(int length)
    {
        byte[] bin = new byte[length];
        new Random(5).NextBytes(bin);
        return bin;
    }

    [Fact]
    public void ATableBeyondTheLastMatch_Close_TakesItsShift_AndFar_IsNotPlaced()
    {
        byte[] a = Source();
        List<TableDefinition> tables = Tables();
        tables.Add(Table("tail", 0x1000, 1, 1, 16));   // after the last matched table ("after", moved +10), within reach

        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a), tables);

        TableMatch tail = result.Matches.Single(m => m.Source.Name == "tail");
        Assert.Equal(MatchKind.Inferred, tail.Kind);
        Assert.Equal(10, tail.Shift);

        // Much further away than the limit, the same is not assumed.
        byte[] big = Random(0x6000);
        List<TableDefinition> far = new() { Table("a", 0x100), Table("b", 0x200), Table("near", 0x1000, 1, 1, 16), Table("far", 0x5000, 1, 1, 16) };
        MigrationResult unchanged = DefinitionMigrator.Migrate(big, (byte[])big.Clone(), far);

        Assert.Equal(MatchKind.Inferred, unchanged.Matches.Single(m => m.Source.Name == "near").Kind);
        Assert.Equal(MatchKind.Unresolved, unchanged.Matches.Single(m => m.Source.Name == "far").Kind);
    }

    [Fact]
    public void ATableBeyondTheLastMatch_IsNotPlaced_WhenOnlyOneTableBacksThatShift()
    {
        byte[] big = Random(0x3000);
        List<TableDefinition> tables = new() { Table("only anchor", 0x100), Table("tail", 0x1000, 1, 1, 16) };

        MigrationResult result = DefinitionMigrator.Migrate(big, (byte[])big.Clone(), tables);

        Assert.Equal(MatchKind.Content, result.Matches.Single(m => m.Source.Name == "only anchor").Kind);
        Assert.Equal(MatchKind.Unresolved, result.Matches.Single(m => m.Source.Name == "tail").Kind);
    }

    [Fact]
    public void AFlatRunOfZeros_IsNotMatchedByContent()
    {
        byte[] a = Source();
        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a), Tables());

        Assert.NotEqual(MatchKind.Content, result.Matches.Single(m => m.Source.Name == "flat").Kind);
    }

    [Fact]
    public void TheCounts_AddUp()
    {
        byte[] a = Source();
        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a), Tables());

        Assert.Equal(Tables().Count, result.Matches.Count);
        Assert.Equal(result.Matches.Count, result.Count(MatchKind.Content) + result.Count(MatchKind.Inferred) + result.Count(MatchKind.Unresolved));
        Assert.Equal(result.Matches.Count - result.Count(MatchKind.Unresolved), result.Placed.Count());
    }

    [Fact]
    public void Place_MovesTheTableItsRowsAndItsAxes_AndSaysHowItWasPlaced()
    {
        TableDefinition t = Table("T", 0x1000);
        t.Description = "Original text.";
        t.XAxis = new AxisSource { Address = 0xFF0, Count = 4 };
        t.YAxis = new AxisSource { Address = 0xFE0, Count = 4 };
        t.RowAddressOverrides = new Dictionary<int, uint> { [0] = 0x1000, [1] = 0x2000 };

        TableDefinition moved = MigrationResult.Place(new TableMatch(t, MatchKind.Content, -10, 0.95), "12212156 - 2002 512k V1.xdf");

        Assert.Equal(0xFF6u, moved.Address);
        Assert.Equal(0xFE6u, moved.XAxis!.Address);
        Assert.Equal(0xFD6u, moved.YAxis!.Address);
        Assert.Equal(0xFF6u, moved.RowAddressOverrides![0]);
        Assert.Equal(0x1FF6u, moved.RowAddressOverrides[1]);
        Assert.Contains("12212156 - 2002 512k V1.xdf", moved.Description);
        Assert.Contains("-10", moved.Description);
        Assert.EndsWith("Original text.", moved.Description);

        // The original definition is untouched.
        Assert.Equal(0x1000u, t.Address);
        Assert.Equal(0xFF0u, t.XAxis.Address);
        Assert.Equal(0x1000u, t.RowAddressOverrides[0]);
        Assert.Equal("Original text.", t.Description);
    }

    [Fact]
    public void ATableThatWouldFallOutsideTheTarget_IsNotPlaced()
    {
        byte[] a = Source();
        List<TableDefinition> tables = new() { Table("start", 0x100), Table("end", 0x11F0, 1, 8), Table("last", 0x11FC, 1, 1, 8) };

        // The target is shorter, so a table near the end cannot be moved up into it.
        MigrationResult result = DefinitionMigrator.Migrate(a, Target(a).AsSpan(0, 0x1100).ToArray(), tables);

        Assert.All(result.Matches.Where(m => m.Source.Name != "start"), m => Assert.Equal(MatchKind.Unresolved, m.Kind));
    }

    // ── With the real files: the Impala (OS 12212156) definition moved onto the Test_Truck BIN (OS 12208322) ──

    private static readonly string Projects = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PcmSuite Projects");

    private static string? Impala => Directory.Exists(Projects)
        ? Directory.GetFiles(Projects, "Impala_LS2_P1_Modified_v1.bin", SearchOption.AllDirectories).FirstOrDefault()
        : null;

    private static string? Truck => Directory.Exists(Projects)
        ? Directory.GetFiles(Projects, "pcm_read_20260408_140451.bin", SearchOption.AllDirectories).FirstOrDefault()
        : null;

    [Fact]
    public void RealFiles_ImpalaDefinition_MovedOntoTheTruckBin_FindsTheKnownShifts()
    {
        string? xdf = TestData.Xdf("12212156 - 2002 512k V1.xdf");
        if (Impala == null || Truck == null || xdf == null)
        {
            return;   // the real files are only on the developer's machine
        }

        List<TableDefinition> tables = XdfParser.Parse(xdf);
        MigrationResult result = DefinitionMigrator.Migrate(File.ReadAllBytes(Impala), File.ReadAllBytes(Truck), tables);

        // Measured when this was written: about 45 tables are found by their bytes, with the shifts 0, -2, -10 and -292 along the file;
        // many more sit between neighbours that moved alike.
        Assert.InRange(result.Count(MatchKind.Content), 35, 120);
        Assert.InRange(result.Count(MatchKind.Inferred), 100, 300);
        HashSet<int> shifts = result.Matches.Where(m => m.Kind == MatchKind.Content).Select(m => m.Shift).ToHashSet();
        Assert.Contains(0, shifts);
        Assert.Contains(-10, shifts);
    }
}

/// <summary>The whole path in the Tuner: a BIN with no XDF, a similar BIN that has one, the tables found, added, and kept as a definition.</summary>
[Collection("ScratchEnvironment")]
public class DefinitionFinderFlowTests
{
    [Fact]
    public async Task ABinWithNoXdf_GetsTheTablesOfASimilarBin_AndKeepsThemUnderItsOwnOs()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "pcmsuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string? oldData = Environment.GetEnvironmentVariable("PCMSUITE_DATA");
        string? oldDocs = Environment.GetEnvironmentVariable("PCMSUITE_DOCS");
        Environment.SetEnvironmentVariable("PCMSUITE_DATA", scratch);
        Environment.SetEnvironmentVariable("PCMSUITE_DOCS", scratch);
        try
        {
            // The reference: its own part number, an XDF named for it, and tables at known places.
            byte[] reference = new byte[0x2000];
            new Random(7).NextBytes(reference);
            System.Text.Encoding.ASCII.GetBytes("12345678").CopyTo(reference, 0x1F00);
            List<TableDefinition> tables = new()
            {
                new TableDefinition { Name = "First", Category = "Airflow", Address = 0x100, Rows = 4, Columns = 4, ElementBits = 16 },
                new TableDefinition { Name = "Second", Category = "Airflow", Address = 0x800, Rows = 4, Columns = 4, ElementBits = 16 },
            };
            await File.WriteAllBytesAsync(Path.Combine(scratch, "reference.bin"), reference);
            XdfWriter.Write(Path.Combine(scratch, "12345678 - reference.xdf"), tables, "ref", "ref", reference.Length);

            // The target: the same calibration 16 bytes further up, and a different part number that no XDF is named for.
            byte[] target = new byte[reference.Length];
            Array.Copy(reference, 0, target, 16, reference.Length - 16);
            Array.Clear(target, 0x1F08, 0x18);   // the reference's own number (moved up with the rest) is not in this BIN, and the new one ends cleanly
            System.Text.Encoding.ASCII.GetBytes("12999999").CopyTo(target, 0x1F00);
            string targetPath = Path.Combine(scratch, "target.bin");
            await File.WriteAllBytesAsync(targetPath, target);

            TuneViewModel tune = new();
            await tune.OpenBinAsync(targetPath);
            Assert.True(tune.IsBinOnly);

            FoundTables found = await tune.FindTablesAsync(Path.Combine(scratch, "reference.bin"), null);
            Assert.Equal(2, found.Result.Count(MatchKind.Content));
            Assert.Equal("12345678 - reference.xdf", found.XdfName);

            Assert.Equal(2, tune.ApplyFound(found, includeInferred: true));
            Assert.False(tune.IsBinOnly);
            Assert.Equal(0x110u, tune.AllTables.Single(t => t.Name == "First").Address);
            Assert.Equal(0x810u, tune.AllTables.Single(t => t.Name == "Second").Address);
            Assert.Contains("Placed by comparing", tune.AllTables.First().Description);

            // The values the placed table reads are the reference's values.
            TableDefinition placed = tune.AllTables.Single(t => t.Name == "First");
            TableDefinition original = tables[0];
            Assert.Equal(original.GetRawValue(reference, 1, 2), placed.GetRawValue(target, 1, 2));

            // Kept as a definition, it is named for this BIN's own part number, not the reference's.
            await tune.SaveMyDefinitionAsync();
            Assert.True(File.Exists(Path.Combine(scratch, "PcmSuite Projects", "XDFs", "12999999 - my definition.xdf"))
                        || Directory.GetFiles(scratch, "12999999 - my definition.xdf", SearchOption.AllDirectories).Length == 1, tune.Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", oldData);
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    [Fact]
    public async Task AReferenceWithNoReadableXdf_SaysSo()
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
            System.Text.Encoding.ASCII.GetBytes("12345678").CopyTo(bytes, 0x800);
            string bin = Path.Combine(scratch, "x.bin");
            await File.WriteAllBytesAsync(bin, bytes);

            TuneViewModel tune = new();
            await tune.OpenBinAsync(bin);

            await Assert.ThrowsAsync<NoReadableDefinitionException>(() => tune.FindTablesAsync(bin, null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCMSUITE_DATA", oldData);
            Environment.SetEnvironmentVariable("PCMSUITE_DOCS", oldDocs);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }
}
