using Microsoft.Win32;
using System;

namespace DCSB.Business
{
    public class OpenFileManager
    {
        public string SaveBackupFile()
        {
            SaveFileDialog fileDialog = new SaveFileDialog
            {
                Title = "Save settings backup",
                Filter = "DCSB backup (*.xml)|*.xml",
                AddExtension = true,
                DefaultExt = "xml",
                FileName = "DCSB_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xml",
                RestoreDirectory = true
            };

            bool? result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value)
            {
                return fileDialog.FileName;
            }
            return null;
        }

        public string OpenBackupFile()
        {
            OpenFileDialog fileDialog = new OpenFileDialog
            {
                Title = "Choose settings backup",
                Filter = "DCSB backup (*.xml)|*.xml|all files (*.*)|*.*",
                RestoreDirectory = true,
                Multiselect = false
            };

            bool? result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value)
            {
                return fileDialog.FileName;
            }
            return null;
        }

        public string OpenCounterFile()
        {
            OpenFileDialog fileDialog = new OpenFileDialog
            {
                Title = "Choose counter file",
                Filter = "txt files|*.txt",
                AddExtension = true,
                RestoreDirectory = true,
                Multiselect = false
            };

            bool? result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value)
            {
                return fileDialog.FileName;
            }
            return null;
        }

        public string[] OpenSoundFiles()
        {
            OpenFileDialog fileDialog = new OpenFileDialog
            {
                Title = "Choose sound file/s",
                Filter = "sound files|*.wma;*.mp3;*.wav;*.ogg;*.m4a;*.aac;*.mp4;*.aiff;*.flac|all files (*.*)|*.*",
                AddExtension = true,
                RestoreDirectory = true,
                Multiselect = true
            };

            bool? result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value)
            {
                return fileDialog.FileNames;
            }
            return null;
        }
    }
}
