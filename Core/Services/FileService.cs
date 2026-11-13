using System;
using System.IO;
using GothicSaveEditor.Core.Utils;
using Microsoft.Win32;

namespace GothicSaveEditor.Core.Services
{
    public static class FileService
    {
        private const string FileName = "SAVEDAT";
        private const string DefaultExt = "SAVEDAT";
        private const string Filter = "Gothic Savegames|SAVEDAT.SAV";

        public static string? PickGothicFolder()
        {
            var dialog = new OpenFolderDialog
            {
                InitialDirectory = Directory.Exists(Options.GamePath) ? Options.GamePath : null
            };

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        public static string? ImportSave()
        {
            var openFileDialog = new OpenFileDialog
            {
                InitialDirectory = Directory.Exists(Options.GamePath) ? Options.GamePath : null,
                FileName = FileName,
                DefaultExt = DefaultExt,
                Filter = Filter,
                RestoreDirectory = true
            };
            return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : null;
        }

        public static string? ExportSave()
        {
            var saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = Directory.Exists(Options.GamePath) ? Options.GamePath : null,
                FileName = FileName,
                DefaultExt = DefaultExt,
                Filter = Filter,
                RestoreDirectory = true
            };
            return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : null;
        }

        public static string? ExportVariables()
        {
            var saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory),
                DefaultExt = "txt",
                Filter = "Text Files|*.txt",
                RestoreDirectory = false
            };
            return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : null;
        }
    }
}
