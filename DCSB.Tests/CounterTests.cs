using DCSB.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace DCSB.Tests
{
    [TestClass]
    public class CounterTests
    {
        private string _filePath;

        [TestInitialize]
        public void TestInitialize()
        {
            _filePath = Path.GetTempFileName();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }

        private Counter RestoreCounter(string format)
        {
            // mimics XmlSerializer restoring a counter at startup:
            // properties are assigned in XmlElement order (Format before File)
            return new Counter { Format = format, File = _filePath };
        }

        [TestMethod]
        public void ReadFromFile_PositiveCount_IsRestored()
        {
            File.WriteAllText(_filePath, "42");

            Counter counter = RestoreCounter("{0}");

            Assert.IsNull(counter.Error);
            Assert.AreEqual(42, counter.Count);
        }

        [TestMethod]
        public void ReadFromFile_NegativeCount_IsRestored()
        {
            File.WriteAllText(_filePath, "-2");

            Counter counter = RestoreCounter("{0}");

            Assert.IsNull(counter.Error);
            Assert.AreEqual(-2, counter.Count);
            Assert.AreEqual("-2", File.ReadAllText(_filePath));
        }

        [TestMethod]
        public void ReadFromFile_TrailingNewline_IsRestored()
        {
            File.WriteAllText(_filePath, "17\r\n");

            Counter counter = RestoreCounter("{0}");

            Assert.IsNull(counter.Error);
            Assert.AreEqual(17, counter.Count);
        }

        [TestMethod]
        public void ReadFromFile_FormatWithRegexMetacharacters_IsRestored()
        {
            File.WriteAllText(_filePath, "Deaths (13)");

            Counter counter = RestoreCounter("Deaths ({0})");

            Assert.IsNull(counter.Error);
            Assert.AreEqual(13, counter.Count);
        }

        [TestMethod]
        public void SettingFile_UnparsableContent_DoesNotOverwriteFile()
        {
            File.WriteAllText(_filePath, "not a number");

            Counter counter = RestoreCounter("{0}");

            Assert.IsNotNull(counter.Error);
            Assert.AreEqual(0, counter.Count);
            Assert.AreEqual("not a number", File.ReadAllText(_filePath));
        }

        [TestMethod]
        public void SettingFile_EmptyFile_WritesCurrentCount()
        {
            Counter counter = RestoreCounter("{0}");

            Assert.IsNull(counter.Error);
            Assert.AreEqual("0", File.ReadAllText(_filePath));
        }

        [TestMethod]
        public void RoundTrip_CountSurvivesRestart()
        {
            Counter first = RestoreCounter("{0}");
            first.Count = 7;

            Counter second = RestoreCounter("{0}");

            Assert.IsNull(second.Error);
            Assert.AreEqual(7, second.Count);
        }
    }
}
