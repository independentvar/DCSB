using DCSB.Business;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace DCSB.Tests
{
    [TestClass]
    public class UpdateManagerTests
    {
        [TestMethod]
        public void ParseVersion_TagWithVPrefix_ReturnsVersion()
        {
            Assert.AreEqual(new Version(4, 2, 1, 0), UpdateManager.ParseVersion("v4.2.1.0"));
        }

        [TestMethod]
        public void ParseVersion_BareVersionTag_ReturnsVersion()
        {
            Assert.AreEqual(new Version(4, 2, 0, 0), UpdateManager.ParseVersion("4.2.0.0"));
        }

        [TestMethod]
        public void ParseVersion_TagWithSurroundingText_ReturnsVersion()
        {
            Assert.AreEqual(new Version(10, 0, 0, 1), UpdateManager.ParseVersion("DCSB v10.0.0.1-beta"));
        }

        [TestMethod]
        [ExpectedException(typeof(FormatException))]
        public void ParseVersion_TagWithoutFourPartVersion_Throws()
        {
            UpdateManager.ParseVersion("v4.2.1");
        }

        [TestMethod]
        [ExpectedException(typeof(FormatException))]
        public void ParseVersion_TagWithNoVersion_Throws()
        {
            UpdateManager.ParseVersion("latest");
        }
    }
}
