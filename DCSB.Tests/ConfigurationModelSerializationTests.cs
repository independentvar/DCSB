using DCSB.Models;
using DCSB.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Xml.Serialization;

namespace DCSB.Tests
{
    [TestClass]
    public class ConfigurationModelSerializationTests
    {
        private static ConfigurationModel RoundTrip(ConfigurationModel model)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, model);
                stream.Position = 0;
                return (ConfigurationModel)serializer.Deserialize(stream);
            }
        }

        [TestMethod]
        public void RoundTrip_PreservesScalarSettingsAndPresets()
        {
            ConfigurationModel model = new ConfigurationModel
            {
                Volume = 55,
                SecondaryDeviceVolume = 70,
                Overlap = true,
                SecondaryOutput = "Headphones",
                Enable = DisplayOption.Counters
            };
            Preset preset = new Preset { Name = "Speedrun" };
            preset.CounterCollection.Add(new Counter { Name = "Deaths", Increment = 5, Format = "Deaths: {0}", Count = 17 });
            model.PresetCollection.Add(preset);

            ConfigurationModel loaded = RoundTrip(model);

            Assert.AreEqual(55, loaded.Volume);
            Assert.AreEqual(70, loaded.SecondaryDeviceVolume);
            Assert.IsTrue(loaded.Overlap);
            Assert.AreEqual("Headphones", loaded.SecondaryOutput);
            Assert.AreEqual(DisplayOption.Counters, loaded.Enable);
            Assert.AreEqual(1, loaded.PresetCollection.Count);
            Assert.AreEqual("Speedrun", loaded.PresetCollection[0].Name);
            Assert.AreEqual(1, loaded.PresetCollection[0].CounterCollection.Count);
            Counter loadedCounter = loaded.PresetCollection[0].CounterCollection[0];
            Assert.AreEqual("Deaths", loadedCounter.Name);
            Assert.AreEqual(5, loadedCounter.Increment);
            Assert.AreEqual("Deaths: {0}", loadedCounter.Format);
            // Count is [XmlIgnore]: it is persisted via the counter's output file, not the config
            Assert.AreEqual(0, loadedCounter.Count);
        }

        [TestMethod]
        public void SoundPressAgainBehavior_DefaultsToPauseAndRoundTrips()
        {
            Sound defaultSound = new Sound();
            Assert.AreEqual(PressAgainBehavior.Pause, defaultSound.PressAgainBehavior);

            ConfigurationModel model = new ConfigurationModel();
            Preset preset = new Preset { Name = "Sounds" };
            preset.SoundCollection.Add(new Sound { Name = "Toggle", PressAgainBehavior = PressAgainBehavior.Stop });
            model.PresetCollection.Add(preset);

            ConfigurationModel loaded = RoundTrip(model);

            Assert.AreEqual(PressAgainBehavior.Stop, loaded.PresetCollection[0].SoundCollection[0].PressAgainBehavior);
        }

        [TestMethod]
        public void Deserialize_LegacySoundWithoutPressAgainBehavior_DefaultsToPause()
        {
            string oldConfig = "<?xml version=\"1.0\"?><ConfigurationModel><PresetCollection><Preset><SoundCollection><Sound><Name>Legacy</Name></Sound></SoundCollection></Preset></PresetCollection></ConfigurationModel>";
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel loaded;
            using (StringReader reader = new StringReader(oldConfig))
            {
                loaded = (ConfigurationModel)serializer.Deserialize(reader);
            }

            Assert.AreEqual(PressAgainBehavior.Pause, loaded.PresetCollection[0].SoundCollection[0].PressAgainBehavior);
        }

        [TestMethod]
        public void RoundTrip_PreservesAutoAssignSettings()
        {
            ConfigurationModel model = new ConfigurationModel
            {
                AutoAssignKeys = false,
                AutoAssignKeySet = AutoAssignKeySet.Numpad
            };

            ConfigurationModel loaded = RoundTrip(model);

            Assert.IsFalse(loaded.AutoAssignKeys);
            Assert.AreEqual(AutoAssignKeySet.Numpad, loaded.AutoAssignKeySet);
        }

        [TestMethod]
        public void Deserialize_ConfigWithoutAutoAssignElements_UsesDefaults()
        {
            // configs written by older versions have no auto-assign elements;
            // they must load with the feature enabled on the number row
            string oldConfig = "<?xml version=\"1.0\"?><ConfigurationModel><Volume>80</Volume></ConfigurationModel>";
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel loaded;
            using (StringReader reader = new StringReader(oldConfig))
            {
                loaded = (ConfigurationModel)serializer.Deserialize(reader);
            }

            Assert.AreEqual(80, loaded.Volume);
            Assert.IsTrue(loaded.AutoAssignKeys);
            Assert.AreEqual(AutoAssignKeySet.NumberRow, loaded.AutoAssignKeySet);
        }

        [TestMethod]
        public void RoundTrip_PreservesMicrophoneSettings()
        {
            ConfigurationModel model = new ConfigurationModel
            {
                MicrophoneInput = "Headset Microphone",
                MicrophoneVolume = 150
            };

            ConfigurationModel loaded = RoundTrip(model);

            Assert.AreEqual("Headset Microphone", loaded.MicrophoneInput);
            Assert.AreEqual(150, loaded.MicrophoneVolume);
        }

        [TestMethod]
        public void Deserialize_ConfigWithoutMicrophoneElements_UsesDefaults()
        {
            // configs written by older versions have no microphone elements; the
            // microphone must stay disabled (no capture without opting in) at unity gain
            string oldConfig = "<?xml version=\"1.0\"?><ConfigurationModel><Volume>80</Volume></ConfigurationModel>";
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel loaded;
            using (StringReader reader = new StringReader(oldConfig))
            {
                loaded = (ConfigurationModel)serializer.Deserialize(reader);
            }

            Assert.IsNull(loaded.MicrophoneInput);
            Assert.AreEqual(100, loaded.MicrophoneVolume);
        }

        [TestMethod]
        public void RoundTrip_PreservesMicrophoneMuteSettings()
        {
            ConfigurationModel model = new ConfigurationModel { MicrophoneMuted = true };
            model.SoundShortcuts.MuteMicrophone.Keys.Add(VKey.F8);

            ConfigurationModel loaded = RoundTrip(model);

            Assert.IsTrue(loaded.MicrophoneMuted);
            Assert.AreEqual(1, loaded.SoundShortcuts.MuteMicrophone.Keys.Count);
            Assert.AreEqual(VKey.F8, loaded.SoundShortcuts.MuteMicrophone.Keys[0]);
        }

        [TestMethod]
        public void Deserialize_ConfigWithoutMicrophoneMuteElements_UsesDefaults()
        {
            // configs written by older versions have no mute elements; the microphone
            // must load unmuted with no mute keybind
            string oldConfig = "<?xml version=\"1.0\"?><ConfigurationModel><Volume>80</Volume></ConfigurationModel>";
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel loaded;
            using (StringReader reader = new StringReader(oldConfig))
            {
                loaded = (ConfigurationModel)serializer.Deserialize(reader);
            }

            Assert.IsFalse(loaded.MicrophoneMuted);
            Assert.AreEqual(0, loaded.SoundShortcuts.MuteMicrophone.Keys.Count);
        }

        [TestMethod]
        public void RoundTrip_PreservesNoiseSuppressionMode()
        {
            ConfigurationModel model = new ConfigurationModel { NoiseSuppressionMode = NoiseSuppressionMode.HighQuality };

            ConfigurationModel loaded = RoundTrip(model);

            Assert.AreEqual(NoiseSuppressionMode.HighQuality, loaded.NoiseSuppressionMode);
        }

        [TestMethod]
        public void Deserialize_ConfigWithoutNoiseSuppressionElement_UsesDefault()
        {
            // configs written by older versions have no noise suppression element;
            // the feature must load switched off
            string oldConfig = "<?xml version=\"1.0\"?><ConfigurationModel><Volume>80</Volume></ConfigurationModel>";
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel loaded;
            using (StringReader reader = new StringReader(oldConfig))
            {
                loaded = (ConfigurationModel)serializer.Deserialize(reader);
            }

            Assert.AreEqual(NoiseSuppressionMode.Disabled, loaded.NoiseSuppressionMode);
        }

        [TestMethod]
        public void Deserialize_LegacyNoiseSuppressionBool_MigratesToFastMode()
        {
            // 4.22.x wrote noise suppression as a bool driving rnnoise; that element
            // must map onto the Fast mode (and false must stay Disabled)
            string enabledConfig = "<?xml version=\"1.0\"?><ConfigurationModel><NoiseSuppression>true</NoiseSuppression></ConfigurationModel>";
            string disabledConfig = "<?xml version=\"1.0\"?><ConfigurationModel><NoiseSuppression>false</NoiseSuppression></ConfigurationModel>";
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel enabled, disabled;
            using (StringReader reader = new StringReader(enabledConfig))
            {
                enabled = (ConfigurationModel)serializer.Deserialize(reader);
            }
            using (StringReader reader = new StringReader(disabledConfig))
            {
                disabled = (ConfigurationModel)serializer.Deserialize(reader);
            }

            Assert.AreEqual(NoiseSuppressionMode.Fast, enabled.NoiseSuppressionMode);
            Assert.AreEqual(NoiseSuppressionMode.Disabled, disabled.NoiseSuppressionMode);

            // the migrated config must be written back with the new element only
            XmlSerializer writer = new XmlSerializer(typeof(ConfigurationModel));
            using (StringWriter written = new StringWriter())
            {
                writer.Serialize(written, enabled);
                string xml = written.ToString();
                StringAssert.Contains(xml, "<NoiseSuppressionMode>Fast</NoiseSuppressionMode>");
                Assert.IsFalse(xml.Contains("<NoiseSuppression>"), "legacy bool element must not be re-written");
            }
        }

        [TestMethod]
        public void RoundTrip_PreservesOverlaySettings()
        {
            ConfigurationModel model = new ConfigurationModel { OverlayEnabled = false, OverlayOpacity = 40 };

            ConfigurationModel loaded = RoundTrip(model);

            Assert.IsFalse(loaded.OverlayEnabled);
            Assert.AreEqual(40, loaded.OverlayOpacity);
        }

        [TestMethod]
        public void Deserialize_ConfigWithoutOverlayElements_UsesDefaults()
        {
            // configs written by older versions have no overlay elements;
            // the overlay must be on by default at full opacity
            string oldConfig = "<?xml version=\"1.0\"?><ConfigurationModel><Volume>80</Volume></ConfigurationModel>";
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel loaded;
            using (StringReader reader = new StringReader(oldConfig))
            {
                loaded = (ConfigurationModel)serializer.Deserialize(reader);
            }

            Assert.IsTrue(loaded.OverlayEnabled);
            Assert.AreEqual(100, loaded.OverlayOpacity);
        }

        [TestMethod]
        public void RoundTrip_PreservesPerPresetOverlayGeometry()
        {
            ConfigurationModel model = new ConfigurationModel();
            model.PresetCollection.Add(new Preset
            {
                Name = "Boss rush",
                OverlayPositionX = 0.25,
                OverlayPositionY = 0.1,
                OverlayWidth = 640,
                OverlayHeight = 72,
                OverlayCustomized = true
            });

            ConfigurationModel loaded = RoundTrip(model);

            Preset loadedPreset = loaded.PresetCollection[0];
            Assert.AreEqual(0.25, loadedPreset.OverlayPositionX);
            Assert.AreEqual(0.1, loadedPreset.OverlayPositionY);
            Assert.AreEqual(640, loadedPreset.OverlayWidth);
            Assert.AreEqual(72, loadedPreset.OverlayHeight);
            Assert.IsTrue(loadedPreset.OverlayCustomized);
        }

        [TestMethod]
        public void Deserialize_PresetWithoutOverlayElements_UsesDefaults()
        {
            // presets written before per-preset overlay geometry existed have no
            // overlay elements; each must default to a centred 520x56 box
            string oldConfig = "<?xml version=\"1.0\"?><ConfigurationModel>" +
                "<PresetCollection><Preset><Name>Old</Name></Preset></PresetCollection>" +
                "</ConfigurationModel>";
            XmlSerializer serializer = new XmlSerializer(typeof(ConfigurationModel));
            ConfigurationModel loaded;
            using (StringReader reader = new StringReader(oldConfig))
            {
                loaded = (ConfigurationModel)serializer.Deserialize(reader);
            }

            Preset preset = loaded.PresetCollection[0];
            Assert.AreEqual("Old", preset.Name);
            Assert.AreEqual(0.5, preset.OverlayPositionX);
            Assert.AreEqual(0.0, preset.OverlayPositionY);
            Assert.AreEqual(520, preset.OverlayWidth);
            Assert.AreEqual(56, preset.OverlayHeight);
            // not customized, so the live overlay uses the automatic content-sized bar
            Assert.IsFalse(preset.OverlayCustomized);
        }

        [TestMethod]
        public void RoundTrip_DoesNotSerializeXmlIgnoredSelectedPreset()
        {
            ConfigurationModel model = new ConfigurationModel();
            model.PresetCollection.Add(new Preset { Name = "One" });
            model.PresetCollection.Add(new Preset { Name = "Two" });
            model.SelectedPresetIndex = 1;

            ConfigurationModel loaded = RoundTrip(model);

            // SelectedPreset is [XmlIgnore]; it must be derived from the persisted index
            Assert.AreEqual(1, loaded.SelectedPresetIndex);
            Assert.AreEqual("Two", loaded.SelectedPreset.Name);
        }
    }
}
