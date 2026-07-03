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
                return new ConfigurationModel();
            }

            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel result;

            using (FileStream stream = File.Open(ConfigPath, FileMode.Open))
            {
                try
                {
                    result = (ConfigurationModel)serializer.Deserialize(stream);
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
                _model = model;
                _failedSaveAttempts = 0;
                if (_timer == null)
                {
                    _timer = new Timer(SaveCallback, null, SaveDelay, Timeout.Infinite);
                }
            }
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

            File.Create(filePath, 1, FileOptions.None, security).Close();
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
