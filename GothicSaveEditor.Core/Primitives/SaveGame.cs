using System.Collections.Generic;
using GothicSaveEditor.Models;

namespace GothicSaveEditor.Core.Primitives;

public sealed class SaveGame
{
    public string FilePath { get; }
    public List<GothicVariable> VariablesList { get; }
    public long FileLength { get; set; }
    public byte[] FileSha256 { get; set; }

    public SaveGame(string filePath, List<GothicVariable> varList, long fileLength = 0, byte[]? fileSha256 = null)
    {
        FilePath = filePath;
        VariablesList = varList;
        FileLength = fileLength;
        FileSha256 = fileSha256 ?? [];
    }
}
