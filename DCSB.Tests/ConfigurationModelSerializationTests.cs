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
