using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GothicSaveEditor.Core.Primitives;
using GothicSaveEditor.Models;

namespace GothicSaveEditor.Core.Writers;

/// <summary>
/// Atomic, integrity-checked writer for SAVEDAT.SAV.
/// </summary>
public static class SaveDatWriter
{
    public static void Save(
        SaveGame saveGame,
        string? destinationPath = null,
        IProgress<double>? progress = null,
        bool resetModifiedFlags = true)
    {
        string sourcePath = saveGame.FilePath;
        string targetPath = string.IsNullOrWhiteSpace(destinationPath) ? sourcePath : destinationPath;
        bool isInPlace = string.Equals(
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(targetPath),
            StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source savegame file not found.", sourcePath);

        // Verify source file hasn't changed on disk since loading
        if (saveGame.FileLength > 0 && saveGame.FileSha256.Length == 32)
        {
            var info = new FileInfo(sourcePath);
            if (info.Length != saveGame.FileLength)
                throw new IOException("The savegame file size changed on disk since it was loaded.");

            byte[] currentHash = ComputeSha256(sourcePath);
            if (!currentHash.AsSpan().SequenceEqual(saveGame.FileSha256))
                throw new IOException("The savegame file contents changed on disk since it was loaded.");
        }

        var modifiedVars = saveGame.VariablesList.Where(v => v.Modified).ToList();
        string? targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
            Directory.CreateDirectory(targetDir);

        string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            progress?.Report(0.1);
            File.Copy(sourcePath, tempPath, overwrite: true);

            if (modifiedVars.Count > 0)
            {
                using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Span<byte> intBuf = stackalloc byte[4];
                int total = modifiedVars.Count;

                for (int i = 0; i < total; i++)
                {
                    GothicVariable variable = modifiedVars[i];
                    if (variable.Position < 0 || variable.Position + 4 > fs.Length)
                        throw new InvalidDataException(
                            $"Variable '{variable.FullName}' position {variable.Position} is out of file bounds ({fs.Length}).");

                    fs.Seek(variable.Position, SeekOrigin.Begin);
                    BinaryPrimitives.WriteInt32LittleEndian(intBuf, variable.Value);
                    fs.Write(intBuf);

                    if ((i & 63) == 0)
                        progress?.Report(0.2 + 0.6 * (i + 1) / total);
                }

                fs.Flush(flushToDisk: true);
            }

            progress?.Report(0.9);

            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }

            if (isInPlace)
            {
                if (resetModifiedFlags)
                {
                    foreach (var variable in modifiedVars)
                        variable.SetUnModified();
                }

                var updatedInfo = new FileInfo(targetPath);
                saveGame.FileLength = updatedInfo.Length;
                saveGame.FileSha256 = ComputeSha256(targetPath);
            }

            progress?.Report(1.0);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* best effort cleanup */ }
            }
        }
    }

    public static byte[] ComputeSha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(fs);
    }
}
