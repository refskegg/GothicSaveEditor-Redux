using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;

namespace GothicSaveEditor.Core.Services;

public static class BackupService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static string DefaultBackupsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GothicSaveEditor",
        "Backups");

    public static string BackupsFolder { get; set; } = DefaultBackupsRoot;

    public static int MaxBackupsPerSlot { get; set; } = 10;

    public static void OpenBackupsFolder()
    {
        if (!Directory.Exists(BackupsFolder))
            Directory.CreateDirectory(BackupsFolder);

        Process.Start(new ProcessStartInfo
        {
            FileName = BackupsFolder,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Creates a full folder backup of the savegame slot directory containing <paramref name="saveDatPath"/>
    /// and rotates old backups beyond <paramref name="maxBackups"/>.
    /// Throws on failure so callers abort saving.
    /// </summary>
    public static string MakeBackup(string saveDatPath, int? maxBackups = null, string? backupsRootOverride = null)
    {
        if (string.IsNullOrWhiteSpace(saveDatPath) || !File.Exists(saveDatPath))
            throw new FileNotFoundException("Cannot create backup: savegame file not found.", saveDatPath);

        string root = backupsRootOverride ?? BackupsFolder;
        string slotDir = Path.GetDirectoryName(Path.GetFullPath(saveDatPath))
            ?? throw new InvalidOperationException("Cannot determine savegame slot directory.");
        var slotInfo = new DirectoryInfo(slotDir);
        string slotName = SanitizeSegment(slotInfo.Name);
        string modName = SanitizeSegment(slotInfo.Parent?.Name ?? "Default");

        string modBackupDir = Path.Combine(root, modName);
        Directory.CreateDirectory(modBackupDir);

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss_fff");
        string destDir = Path.Combine(modBackupDir, $"{slotName}_{timestamp}");
        if (Directory.Exists(destDir))
            destDir = Path.Combine(modBackupDir, $"{slotName}_{timestamp}_{Guid.NewGuid().ToString("N")[..6]}");

        CopyDirectory(slotDir, destDir);

        int keep = maxBackups ?? MaxBackupsPerSlot;
        if (keep > 0)
            RotateBackups(modBackupDir, slotName, keep);

        return destDir;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            // Skip any leftover .tmp files
            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(destDir, fileName), overwrite: true);
        }

        foreach (string subDir in Directory.EnumerateDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(subDir);
            CopyDirectory(subDir, Path.Combine(destDir, dirName));
        }
    }

    private static void RotateBackups(string modBackupDir, string slotName, int keepCount)
    {
        string prefix = slotName + "_";
        var slotBackups = new DirectoryInfo(modBackupDir)
            .GetDirectories()
            .Where(d => d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Name, StringComparer.Ordinal)
            .ToList();

        for (int i = keepCount; i < slotBackups.Count; i++)
        {
            try
            {
                slotBackups[i].Delete(recursive: true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to delete old backup directory {0}", slotBackups[i].FullName);
            }
        }
    }

    public static int BackupsCount
    {
        get
        {
            if (!Directory.Exists(BackupsFolder))
                return 0;

            // Count backup slot folders (2nd level: Backups/<Mod>/<Slot_Timestamp>) plus any legacy .SAV files
            int folderCount = 0;
            foreach (var modDir in Directory.EnumerateDirectories(BackupsFolder))
                folderCount += Directory.EnumerateDirectories(modDir).Count();

            int legacySavCount = Directory.EnumerateFiles(BackupsFolder, "*.SAV").Count();
            return folderCount + legacySavCount;
        }
    }

    public static float BackupsSize
    {
        get
        {
            if (!Directory.Exists(BackupsFolder))
                return 0f;

            long totalBytes = 0;
            foreach (var file in Directory.EnumerateFiles(BackupsFolder, "*", SearchOption.AllDirectories))
            {
                try { totalBytes += new FileInfo(file).Length; }
                catch { /* ignore transient */ }
            }

            return (float)Math.Round(totalBytes / 1024f / 1024f, 2);
        }
    }

    public static bool DeleteAll()
    {
        if (!Directory.Exists(BackupsFolder))
            return true;

        bool success = true;
        foreach (var dir in Directory.EnumerateDirectories(BackupsFolder))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex)
            {
                Logger.Error(ex);
                success = false;
            }
        }

        foreach (var file in Directory.EnumerateFiles(BackupsFolder))
        {
            try { File.Delete(file); }
            catch (Exception ex)
            {
                Logger.Error(ex);
                success = false;
            }
        }

        return success;
    }

    private static string SanitizeSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Default";

        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        return new string(chars);
    }
}
