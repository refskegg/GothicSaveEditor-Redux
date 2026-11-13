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

    [Fact]
    public void Roundtrip_SyntheticBinSafe_PatchesExactVariableOffset_AndPreservesRestByteIdentical()
    {
        // Build a minimal valid ZenGin BIN_SAFE SAVEDAT.SAV with:
        // - ASCII header ending in END\n
        // - BinSafe header (version=2, objectCount=1, hashTableOffset)
        // - 4 header scalars (day, hour, min, numInfos)
        // - 1 dialog (told=0, "DIA_TEST")
        // - 1 MIS topic ("TestQuest", status=1, section=0, numEntries=0)
        // - 2 variables: MIS_ARREST=1 and TEST_COUNTER=42
        // - Trailing hash table
        using var ms = new MemoryStream();
        byte[] asciiHeader = Encoding.ASCII.GetBytes(
            "ZenGin Archive\nver 1\nzCArchiverBinSafe\nBIN_SAFE\nsaveGame 1\ndate 13.11.2026 10:00:00\nuser Test\nEND\n");
        ms.Write(asciiHeader);

        int binHeaderPos = (int)ms.Position;
        ms.Write(new byte[12]); // placeholder for version, objectCount, hashTableOffset

        void WriteHashInt(uint hash, int val)
        {
            ms.WriteByte(0x12);
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, hash);
            ms.Write(b);
            ms.WriteByte(0x02);
            BinaryPrimitives.WriteInt32LittleEndian(b, val);
            ms.Write(b);
        }

        void WriteHashBool(uint hash, int val)
        {
            ms.WriteByte(0x12);
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, hash);
            ms.Write(b);
            ms.WriteByte(0x06);
            BinaryPrimitives.WriteInt32LittleEndian(b, val);
            ms.Write(b);
        }

        void WriteHashEnum(uint hash, int val)
        {
            ms.WriteByte(0x12);
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, hash);
            ms.Write(b);
            ms.WriteByte(0x11);
            BinaryPrimitives.WriteInt32LittleEndian(b, val);
            ms.Write(b);
        }

        void WriteHashString(uint hash, string s)
        {
            ms.WriteByte(0x12);
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, hash);
            ms.Write(b);
            ms.WriteByte(0x01);
            byte[] strBytes = Encoding.ASCII.GetBytes(s);
            Span<byte> lenBuf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(lenBuf, (ushort)strBytes.Length);
            ms.Write(lenBuf);
            ms.Write(strBytes);
        }

        // Header scalars
        WriteHashInt(0, 1);
        WriteHashInt(1, 12);
        WriteHashInt(2, 0);
        WriteHashInt(3, 1);

        // Dialog
        WriteHashBool(4, 0);
        WriteHashString(5, "DIA_TEST");

        // MIS (1 topic)
        WriteHashInt(6, 1);
        WriteHashString(7, "TestQuest");
        WriteHashEnum(8, 0);
        WriteHashEnum(9, 1);
        WriteHashInt(10, 0);

        // Variables
        WriteHashInt(11, 2); // numSymbols
        WriteHashString(12, "MIS_ARREST");
        WriteHashInt(13, 1); // count
        int misArrestValueOffset = (int)ms.Position + 6;
        WriteHashInt(14, 1);

        WriteHashString(15, "TEST_COUNTER");
        WriteHashInt(16, 1); // count
        WriteHashInt(17, 42);

        int hashTableOffset = (int)ms.Position;
        ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // empty hash table count=0

        byte[] data = ms.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(binHeaderPos, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(binHeaderPos + 4, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(binHeaderPos + 8, 4), hashTableOffset);

        string filePath = Path.Combine(_tempDir, "SYNTH_SAVEDAT.SAV");
        File.WriteAllBytes(filePath, data);

        // Load, change MIS_ARREST from 1 -> 3, save
        var saveGame = BinSafeReader.ReadSaveGame(filePath);
        var misArrest = saveGame.VariablesList.Single(v => v.FullName == "MIS_ARREST");
        Assert.Equal(1, misArrest.Value);
        Assert.Equal(misArrestValueOffset, misArrest.Position);

        misArrest.Value = 3;
        SaveDatWriter.Save(saveGame, resetModifiedFlags: false);

        byte[] after = File.ReadAllBytes(filePath);
        Assert.Equal(data.Length, after.Length);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(after.AsSpan(misArrestValueOffset, 4)));

        // Every other byte must be identical
        for (int i = 0; i < data.Length; i++)
        {
            if (i >= misArrestValueOffset && i < misArrestValueOffset + 4)
                continue;
            Assert.Equal(data[i], after[i]);
        }

        // Reload and verify
        var reloaded = BinSafeReader.ReadSaveGame(filePath);
        Assert.Equal(3, reloaded.VariablesList.Single(v => v.FullName == "MIS_ARREST").Value);
        Assert.Equal(42, reloaded.VariablesList.Single(v => v.FullName == "TEST_COUNTER").Value);
    }
}
