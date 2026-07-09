using DCSB.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Xml.Serialization;

namespace DCSB.Business
{
    public class ConfigurationManager : IDisposable
    {
        private const string DirectoryName = "DCSB";
        private const string FileName = "config.xml";
        private const string TempFileName = "config_tmp.xml";
        private const string BackupFileName = "config_backup.xml";
        private readonly string ConfigPath;
        private readonly string TempConfigPath;
        private readonly string BackupConfigPath;

        private const int SaveDelay = 1000;
        private const int MaxSaveAttempts = 5;

        private readonly object _saveLock = new object();
        private Timer _timer;
        private ConfigurationModel _model;
        private int _failedSaveAttempts;
        private bool _suppressSave;

        public ConfigurationManager()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), DirectoryName))
        {
        }

        public ConfigurationManager(string configDirectory)
        {
            ConfigPath = Path.Combine(configDirectory, FileName);
            TempConfigPath = Path.Combine(configDirectory, TempFileName);
            BackupConfigPath = Path.Combine(configDirectory, BackupFileName);
        }

        private void SaveCallback(object state)
        {
            lock (_saveLock)
            {
                if (_timer == null)
                {
                    return;
                }

                try
                {
                    WriteConfiguration();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    // config_tmp.xml may be locked by another DCSB instance or an antivirus scan;
                    // an unhandled exception on the timer thread would crash the whole app
                    if (++_failedSaveAttempts < MaxSaveAttempts)
                    {
                        _timer.Change(SaveDelay, Timeout.Infinite);
                        return;
                    }
                }

                _timer.Dispose();
                _timer = null;
            }
        }

        private void WriteConfiguration()
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));

            if (!File.Exists(TempConfigPath))
            {
                CreateFile(TempConfigPath);
            }
            using (FileStream stream = File.Open(TempConfigPath, FileMode.Truncate))
            {
                serializer.Serialize(stream, _model);
            }

            if (File.Exists(ConfigPath))
            {
                File.Replace(TempConfigPath, ConfigPath, BackupConfigPath, true);
            }
            else
            {
                File.Move(TempConfigPath, ConfigPath);
            }

            Debug.WriteLine("Saved configuration");
        }

        public ConfigurationModel Load()
        {
            if (!File.Exists(ConfigPath))
            {
                // brand-new install: no config yet, so the setup wizard should run once.
                // Mark the flag as specified so it is written out (as false) from now on.
                ConfigurationModel fresh = new ConfigurationModel();
                fresh.SetupCompletedSpecified = true;
                return fresh;
            }

            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel result;

            using (FileStream stream = File.Open(ConfigPath, FileMode.Open))
            {
                try
                {
                    result = (ConfigurationModel)serializer.Deserialize(stream);
                    // A config written before the wizard existed has no <SetupCompleted>
                    // element, so it deserializes with SetupCompletedSpecified == false.
                    // These are existing users with a working setup - migrate them to
                    // "completed" so the wizard never interrupts them.
                    if (!result.SetupCompletedSpecified)
                    {
                        result.SetupCompleted = true;
                        result.SetupCompletedSpecified = true;
                    }
                    return result;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
            }

            MoveCorruptedConfig(ConfigPath);
            return new ConfigurationModel();
        }

        public void Save(ConfigurationModel model)
        {
            lock (_saveLock)
            {
                // a restore has taken over the config file and the app is on its way to
                // restarting; don't let a late change write the old model back over it
                if (_suppressSave)
                {
                    return;
                }
                _model = model;
                _failedSaveAttempts = 0;
                if (_timer == null)
                {
                    _timer = new Timer(SaveCallback, null, SaveDelay, Timeout.Infinite);
                }
            }
        }

        // Writes the given configuration (settings plus every preset, sound key and
        // shortcut binding) to a user-chosen file. Throws on I/O failure so the caller
        // can report it.
        public void Backup(ConfigurationModel model, string destinationPath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            using (FileStream stream = File.Create(destinationPath))
            {
                serializer.Serialize(stream, model);
            }
        }

        // Loads a backup file, writes it over the live config.xml and stops any further
        // saves. The caller is expected to restart the app so it reloads the restored
        // configuration cleanly; suppressing saves keeps the pending debounced write (or
        // the flush on shutdown) from clobbering the restored file with the old model.
        // Throws if the backup file is missing or not a valid configuration.
        public ConfigurationModel Restore(string backupPath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel restored;
            using (FileStream stream = File.Open(backupPath, FileMode.Open))
            {
                restored = (ConfigurationModel)serializer.Deserialize(stream);
            }

            lock (_saveLock)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
                _suppressSave = true;
                _model = restored;
                WriteConfiguration();
            }
            return restored;
        }

        private void CreateFile(string filePath)
        {
            FileSecurity security = new FileSecurity();
            SecurityIdentifier securityIdentifier = new SecurityIdentifier("S-1-1-0");
            FileSystemAccessRule rule = new FileSystemAccessRule(securityIdentifier, FileSystemRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow);
            security.AddAccessRule(rule);

            string directoryPath = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            FileInfo fileInfo = new FileInfo(filePath);
            fileInfo.Create(FileMode.Create, FileSystemRights.FullControl, FileShare.None, 1, FileOptions.None, security).Close();
        }

        private void MoveCorruptedConfig(string filePath)
        {
            string newPath = filePath.Replace(".xml", $"_corrupted_{DateTime.Now.Ticks}.xml");
            try
            {
                File.Move(filePath, newPath);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        public void Dispose()
        {
            lock (_saveLock)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                    try
                    {
                        WriteConfiguration();
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
            }
        }
    }
}
