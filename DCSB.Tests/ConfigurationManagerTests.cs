using DCSB.Business;
using DCSB.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace DCSB.Tests
{
    [TestClass]
    public class ConfigurationManagerTests
    {
        private string _configDirectory;

        [TestInitialize]
        public void TestInitialize()
        {
            _configDirectory = Path.Combine(Path.GetTempPath(), "DCSB.Tests", Guid.NewGuid().ToString("N"));
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, true);
            }
        }

        private void Save(ConfigurationModel model)
        {
            // Dispose flushes the debounced save synchronously
            using (ConfigurationManager manager = new ConfigurationManager(_configDirectory))
            {
                manager.Save(model);
            }
        }

        [TestMethod]
        public void Load_WithoutConfigFile_ReturnsDefaultConfiguration()
        {
            ConfigurationManager manager = new ConfigurationManager(_configDirectory);

            ConfigurationModel loaded = manager.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(100, loaded.Volume);
            Assert.AreEqual(0, loaded.PresetCollection.Count);
        }

        [TestMethod]
        public void SaveThenLoad_RoundTripsConfiguration()
        {
            ConfigurationModel model = new ConfigurationModel
            {
                Volume = 42,
                PrimaryDeviceVolume = 63,
                Overlap = true,
                PrimaryOutput = "Primary device",
                MinimizeToTray = true,
                WindowWidth = 800,
                SelectedPresetIndex = 1
            };
            model.PresetCollection.Add(new Preset { Name = "First" });
            model.PresetCollection.Add(new Preset { Name = "Second" });

            Save(model);
            ConfigurationModel loaded = new ConfigurationManager(_configDirectory).Load();

            Assert.AreEqual(42, loaded.Volume);
            Assert.AreEqual(63, loaded.PrimaryDeviceVolume);
            Assert.IsTrue(loaded.Overlap);
            Assert.AreEqual("Primary device", loaded.PrimaryOutput);
            Assert.IsTrue(loaded.MinimizeToTray);
            Assert.AreEqual(800, loaded.WindowWidth);
            Assert.AreEqual(2, loaded.PresetCollection.Count);
            Assert.AreEqual("First", loaded.PresetCollection[0].Name);
            Assert.AreEqual("Second", loaded.PresetCollection[1].Name);
            Assert.AreEqual(1, loaded.SelectedPresetIndex);
        }

        [TestMethod]
        public void Save_OverExistingConfig_KeepsBackupAndLoadsNewValues()
        {
            Save(new ConfigurationModel { Volume = 10 });
            Save(new ConfigurationModel { Volume = 20 });

            ConfigurationModel loaded = new ConfigurationManager(_configDirectory).Load();

            Assert.AreEqual(20, loaded.Volume);
            Assert.IsTrue(File.Exists(Path.Combine(_configDirectory, "config_backup.xml")));
        }

        [TestMethod]
        public void Save_WhenTempFileIsLocked_DoesNotThrowAndRecovers()
        {
            Directory.CreateDirectory(_configDirectory);
            string tempPath = Path.Combine(_configDirectory, "config_tmp.xml");
            using (new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                // another process (second DCSB instance, antivirus) holds config_tmp.xml;
                // the flushed save must fail silently instead of crashing
                Save(new ConfigurationModel { Volume = 10 });
            }

            Save(new ConfigurationModel { Volume = 20 });

            Assert.AreEqual(20, new ConfigurationManager(_configDirectory).Load().Volume);
        }

        [TestMethod]
        public void BackupThenRestore_RoundTripsConfigurationIntoConfigFile()
        {
            string backupPath = Path.Combine(Path.GetTempPath(), "DCSB.Tests",
                Guid.NewGuid().ToString("N") + ".xml");
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            try
            {
                ConfigurationModel model = new ConfigurationModel
                {
                    Volume = 55,
                    MinimizeToTray = true
                };
                model.PresetCollection.Add(new Preset { Name = "Backed up" });

                // start from a different live config, back the model up, then restore it
                using (ConfigurationManager manager = new ConfigurationManager(_configDirectory))
                {
                    manager.Save(new ConfigurationModel { Volume = 1 });
                    manager.Backup(model, backupPath);
                    manager.Restore(backupPath);
                }

                ConfigurationModel loaded = new ConfigurationManager(_configDirectory).Load();
                Assert.AreEqual(55, loaded.Volume);
                Assert.IsTrue(loaded.MinimizeToTray);
                Assert.AreEqual(1, loaded.PresetCollection.Count);
                Assert.AreEqual("Backed up", loaded.PresetCollection[0].Name);
            }
            finally
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
        }

        [TestMethod]
        public void Restore_AfterRestoring_SuppressesFurtherSaves()
        {
            string backupPath = Path.Combine(Path.GetTempPath(), "DCSB.Tests",
                Guid.NewGuid().ToString("N") + ".xml");
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            try
            {
                using (ConfigurationManager manager = new ConfigurationManager(_configDirectory))
                {
                    manager.Backup(new ConfigurationModel { Volume = 77 }, backupPath);
                    manager.Restore(backupPath);

                    // a late change (or the flush on Dispose) must not clobber the restore
                    manager.Save(new ConfigurationModel { Volume = 99 });
                }

                Assert.AreEqual(77, new ConfigurationManager(_configDirectory).Load().Volume);
            }
            finally
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
        }

        [TestMethod]
        public void Restore_WithInvalidBackupFile_Throws()
        {
            string backupPath = Path.Combine(Path.GetTempPath(), "DCSB.Tests",
                Guid.NewGuid().ToString("N") + ".xml");
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            File.WriteAllText(backupPath, "this is not xml");
            try
            {
                ConfigurationManager manager = new ConfigurationManager(_configDirectory);
                Assert.Throws<InvalidOperationException>(() => manager.Restore(backupPath));
            }
            finally
            {
                File.Delete(backupPath);
            }
        }

        [TestMethod]
        public void Load_WithCorruptConfigFile_ReturnsDefaultsAndMovesFileAside()
        {
            Directory.CreateDirectory(_configDirectory);
            string configPath = Path.Combine(_configDirectory, "config.xml");
            File.WriteAllText(configPath, "this is not xml");

            ConfigurationModel loaded = new ConfigurationManager(_configDirectory).Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(100, loaded.Volume);
            Assert.IsFalse(File.Exists(configPath), "corrupt config.xml should have been moved aside");
            Assert.IsTrue(
                Directory.EnumerateFiles(_configDirectory, "config_corrupted_*.xml").Any(),
                "corrupt config should be preserved under config_corrupted_*.xml");
        }
    }
}
