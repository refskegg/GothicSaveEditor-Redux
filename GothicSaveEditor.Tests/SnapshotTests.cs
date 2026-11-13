using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GothicSaveEditor.Core.Readers;
using GothicSaveEditor.Tests.Legacy;

namespace GothicSaveEditor.Tests;

public class SnapshotTests
{
    internal static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "TestData")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void LegacyReader_ProducesConsistentSnapshot_ForOdysseeSave()
    {
        var root = FindRepoRoot();
        if (root == null) return; // Skip if TestData not present (e.g. CI without local saves)

        var savePath = Path.Combine(root, "TestData", "Odyssee_TestSave", "SAVEDAT.SAV");
        if (!File.Exists(savePath)) return;

        var snapshotPath = Path.Combine(root, "TestData", "Odyssee_TestSave.legacy_snapshot.tsv");

        var vars = LegacyReferenceReader.Read(savePath, sort: false);
        Assert.NotEmpty(vars);

        var lines = vars.Select(v => $"{v.FullName}\t{v.Position}\t{v.Value}").ToArray();

        if (!File.Exists(snapshotPath))
        {
            File.WriteAllLines(snapshotPath, lines, Encoding.UTF8);
        }
        else
        {
            var expected = File.ReadAllLines(snapshotPath, Encoding.UTF8);
            Assert.Equal(expected.Length, lines.Length);
            Assert.Equal(expected, lines);
        }
    }

    [Fact]
    public void BinSafeReader_MatchesLegacySnapshot_AndFixesArrayBugC3()
    {
        var root = FindRepoRoot();
        if (root == null) return;

        var savePath = Path.Combine(root, "TestData", "Odyssee_TestSave", "SAVEDAT.SAV");
        if (!File.Exists(savePath)) return;

        var legacyVars = LegacyReferenceReader.Read(savePath, sort: false)
            .ToDictionary(v => (v.FullName, v.Position));

        var saveGame = BinSafeReader.ReadSaveGame(savePath);
        byte[] rawBytes = File.ReadAllBytes(savePath);

        Assert.NotEmpty(saveGame.VariablesList);

        // Every variable in BinSafeReader must have a valid position whose Int32 equals Value
        foreach (var v in saveGame.VariablesList)
        {
            int diskValue = BinaryPrimitives.ReadInt32LittleEndian(rawBytes.AsSpan(v.Position, 4));
            Assert.Equal(diskValue, v.Value);
        }

        // Compare scalar variables with legacy reader (excluding PC_HOBELBANKBRETT which legacy mangled with header scalars,
        // and excluding array elements where legacy had C3 + positions[0] overwrite bug).
        var legacyScalars = legacyVars.Keys
            .Where(k => !k.FullName.Contains('['))
            .ToHashSet();
        var newScalars = saveGame.VariablesList
            .Where(v => !v.FullName.Contains('['))
            .Select(v => (v.FullName, v.Position))
            .ToHashSet();

        // LegacyReferenceReader's heuristic occasionally leaked MIS (journal) topic titles
        // (located between Dialogs and "[% oCCSManager...]" at offset 800948).
        // Verify zero non-MIS scalars are missing, and zero unexpected extra scalars exist.
        var missingNonMisScalars = legacyScalars
            .Where(k => !newScalars.Contains(k) && k.Position < 800948)
            .ToList();
        var leakedMisScalars = legacyScalars
            .Where(k => !newScalars.Contains(k))
            .ToList();
        // All leaked items in legacy must be inside the MIS section (475180..800948)
        Assert.All(leakedMisScalars, k => Assert.InRange(k.Position, 475000, 800948));

        var extraScalars = newScalars.Where(k => !legacyScalars.Contains(k) && k.FullName != "PC_HOBELBANKBRETT").ToList();

        Assert.Empty(extraScalars);
        Assert.Contains(saveGame.VariablesList, v => v.FullName == "PC_HOBELBANKBRETT" && v.Position == 156);

        // Verify all array variables have strictly increasing positions
        var arrayGroups = saveGame.VariablesList
            .Where(v => v.FullName.Contains('['))
            .GroupBy(v => v.VariableName);
        foreach (var group in arrayGroups)
        {
            var ordered = group.OrderBy(v => v.Position).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i].Position > ordered[i - 1].Position);
            }
        }
    }
}
