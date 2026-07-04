using Microsoft.Win32;

namespace DCSB.Business
{
    public class OpenFileManager
    {
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
