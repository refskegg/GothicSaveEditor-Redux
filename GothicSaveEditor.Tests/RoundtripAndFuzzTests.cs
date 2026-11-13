using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GothicSaveEditor.Core.Primitives;
using GothicSaveEditor.Core.Readers;
using GothicSaveEditor.Core.Utils;
using GothicSaveEditor.Core.Writers;
using GothicSaveEditor.Models;

namespace GothicSaveEditor.Tests;

public class RoundtripAndFuzzTests : IDisposable
{
    private readonly string _tempDir;

    public RoundtripAndFuzzTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GSE_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* ignore */ }
        }
    }

    private string CopyOdysseeSaveToTemp()
    {
        var root = SnapshotTests.FindRepoRoot() ?? throw new InvalidOperationException("Repo root not found");
        var src = Path.Combine(root, "TestData", "Odyssee_TestSave", "SAVEDAT.SAV");
        var dst = Path.Combine(_tempDir, "SAVEDAT.SAV");
        File.Copy(src, dst, overwrite: true);
        return dst;
    }

    [Fact]
    public void Roundtrip_UnchangedSave_IsByteIdenticalSha256()
    {
        var root = SnapshotTests.FindRepoRoot();
        if (root == null) return;
        var src = Path.Combine(root, "TestData", "Odyssee_TestSave", "SAVEDAT.SAV");
        if (!File.Exists(src)) return;

        string tempFile = CopyOdysseeSaveToTemp();
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(tempFile));

        var saveGame = BinSafeReader.ReadSaveGame(tempFile);
        SaveDatWriter.Save(saveGame);

        byte[] afterHash = SHA256.HashData(File.ReadAllBytes(tempFile));
        Assert.Equal(originalHash, afterHash);
    }

    [Fact]
    public void Roundtrip_ModifiedScalarAndArray_PatchesExactBytesAndReloads()
    {
        var root = SnapshotTests.FindRepoRoot();
        if (root == null) return;
        var src = Path.Combine(root, "TestData", "Odyssee_TestSave", "SAVEDAT.SAV");
        if (!File.Exists(src)) return;

        string tempFile = CopyOdysseeSaveToTemp();
        byte[] originalBytes = File.ReadAllBytes(tempFile);

        var saveGame = BinSafeReader.ReadSaveGame(tempFile);

        var scalar = saveGame.VariablesList.First(v => !v.FullName.Contains('['));
        var arrayElem = saveGame.VariablesList.First(v => v.FullName.EndsWith("[1]", StringComparison.Ordinal));

        int newScalarVal = scalar.Value + 12345;
        int newArrayVal = arrayElem.Value + 777;

        scalar.Value = newScalarVal;
        arrayElem.Value = newArrayVal;

        SaveDatWriter.Save(saveGame);

        byte[] newBytes = File.ReadAllBytes(tempFile);
        Assert.Equal(originalBytes.Length, newBytes.Length);

        // Verify ONLY the 4+4=8 patched bytes differ
        var expectedDiffPositions = new HashSet<int>();
        for (int i = 0; i < 4; i++)
        {
            expectedDiffPositions.Add(scalar.Position + i);
            expectedDiffPositions.Add(arrayElem.Position + i);
        }

        for (int i = 0; i < originalBytes.Length; i++)
        {
            if (expectedDiffPositions.Contains(i))
                continue;
            Assert.Equal(originalBytes[i], newBytes[i]);
        }

        Assert.Equal(newScalarVal, BinaryPrimitives.ReadInt32LittleEndian(newBytes.AsSpan(scalar.Position, 4)));
        Assert.Equal(newArrayVal, BinaryPrimitives.ReadInt32LittleEndian(newBytes.AsSpan(arrayElem.Position, 4)));

        // Reload and verify
        var reloaded = BinSafeReader.ReadSaveGame(tempFile);
        Assert.Equal(newScalarVal, reloaded.VariablesList.First(v => v.FullName == scalar.FullName).Value);
        Assert.Equal(newArrayVal, reloaded.VariablesList.First(v => v.FullName == arrayElem.FullName).Value);
    }

    [Fact]
    public void Save_DetectsExternalModificationOnDisk()
    {
        var root = SnapshotTests.FindRepoRoot();
        if (root == null) return;
        var src = Path.Combine(root, "TestData", "Odyssee_TestSave", "SAVEDAT.SAV");
        if (!File.Exists(src)) return;

        string tempFile = CopyOdysseeSaveToTemp();
        var saveGame = BinSafeReader.ReadSaveGame(tempFile);

        // Modify a byte externally
        using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.Seek(0, SeekOrigin.Begin);
            int b = fs.ReadByte();
            fs.Seek(0, SeekOrigin.Begin);
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        Assert.Throws<IOException>(() => SaveDatWriter.Save(saveGame));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(98)]
    [InlineData(110)]
    [InlineData(500)]
    [InlineData(50000)]
    public void Fuzz_TruncatedSave_ThrowsInvalidDataException(int length)
    {
        var root = SnapshotTests.FindRepoRoot();
        if (root == null) return;
        var src = Path.Combine(root, "TestData", "Odyssee_TestSave", "SAVEDAT.SAV");
        if (!File.Exists(src)) return;

        byte[] full = File.ReadAllBytes(src);
        int sliceLen = Math.Min(length, full.Length);

        var reader = new BinSafeReader();
        Assert.Throws<InvalidDataException>(() => reader.ParseBytes(full.AsSpan(0, sliceLen)));
    }

    [Fact]
    public void Fuzz_CorruptedTagOrType_ThrowsInvalidDataException()
    {
        var root = SnapshotTests.FindRepoRoot();
        if (root == null) return;
        var src = Path.Combine(root, "TestData", "Odyssee_TestSave", "SAVEDAT.SAV");
        if (!File.Exists(src)) return;

        byte[] bytes = File.ReadAllBytes(src);
        // Corrupt the first tag byte right after the 98+12 header
        bytes[110] = 0xFE;

        var reader = new BinSafeReader();
        var ex = Assert.Throws<InvalidDataException>(() => reader.ParseBytes(bytes));
        Assert.Contains("0xFE", ex.Message);
    }

    [Fact]
    public void VariablesComparer_HandlesHugeNumbersWithoutOverflow()
    {
        var a = new GothicVariable("VAR_99999999999999999999999999", 0, 0);
        var b = new GothicVariable("VAR_100000000000000000000000000", 0, 0);
        var c = new GothicVariable("VAR_2", 0, 0);

        var list = new List<GothicVariable> { b, a, c };
        list.Sort(VariablesComparer.Instance);

        Assert.Equal("VAR_2", list[0].FullName);
        Assert.Equal("VAR_99999999999999999999999999", list[1].FullName);
        Assert.Equal("VAR_100000000000000000000000000", list[2].FullName);
    }

    [Theory]
    [InlineData(1252, new byte[] { 0xE4, 0xF6, 0xFC, 0xDF, 0x80, 0x84, 0x93 }, "äöüß€„“")]
    [InlineData(1250, new byte[] { 0xB9, 0xE6, 0xEA, 0xB3, 0xF1, 0xF3, 0x9C, 0x9F, 0xBF }, "ąćęłńóśźż")]
    [InlineData(1251, new byte[] { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2 }, "Привет")]
    public void EncodingHelper_DecodesCodePagesCorrectly(int codePage, byte[] raw, string expected)
    {
        var enc = EncodingHelper.GetEncoding(codePage);
        string actual = enc.GetString(raw);
        Assert.Equal(expected, actual);
    }
}
