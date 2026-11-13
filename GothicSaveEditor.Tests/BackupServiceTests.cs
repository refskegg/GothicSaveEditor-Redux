using System;
using System.IO;
using System.Linq;
using System.Threading;
using GothicSaveEditor.Core.Services;

namespace GothicSaveEditor.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _backupsRoot;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GSE_BackupTests_" + Guid.NewGuid().ToString("N"));
        _backupsRoot = Path.Combine(_tempDir, "Backups");
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

    [Fact]
    public void MakeBackup_CopiesEntireSlotFolder_AndRotatesOldBackups()
    {
        string modSavesDir = Path.Combine(_tempDir, "Saves_Odyssee", "savegame1");
        Directory.CreateDirectory(modSavesDir);

        string saveDat = Path.Combine(modSavesDir, "SAVEDAT.SAV");
        string saveInfo = Path.Combine(modSavesDir, "SAVEINFO.SAV");
        string worldSav = Path.Combine(modSavesDir, "NEWWORLD.SAV");

        File.WriteAllText(saveDat, "savedat");
        File.WriteAllText(saveInfo, "saveinfo");
        File.WriteAllText(worldSav, "world");

        // Create 4 backups with maxBackups = 2
        for (int i = 0; i < 4; i++)
        {
            BackupService.MakeBackup(saveDat, maxBackups: 2, backupsRootOverride: _backupsRoot);
            Thread.Sleep(15);
        }

        string modBackupDir = Path.Combine(_backupsRoot, "Saves_Odyssee");
        Assert.True(Directory.Exists(modBackupDir));

        var remaining = Directory.GetDirectories(modBackupDir);
        Assert.Equal(2, remaining.Length);

        foreach (var dir in remaining)
        {
            Assert.True(File.Exists(Path.Combine(dir, "SAVEDAT.SAV")));
            Assert.True(File.Exists(Path.Combine(dir, "SAVEINFO.SAV")));
            Assert.True(File.Exists(Path.Combine(dir, "NEWWORLD.SAV")));
        }
    }

    [Fact]
    public void MakeBackup_Throws_WhenSourceMissing()
    {
        string missing = Path.Combine(_tempDir, "nonexistent", "SAVEDAT.SAV");
        Assert.Throws<FileNotFoundException>(() =>
            BackupService.MakeBackup(missing, backupsRootOverride: _backupsRoot));
    }
}
