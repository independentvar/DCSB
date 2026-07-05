using DCSB.Converters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DCSB.Tests
{
    [TestClass]
    public class PercentToOpacityConverterTests
    {
        private readonly PercentToOpacityConverter _converter = new PercentToOpacityConverter();

        [TestMethod]
        public void Convert_MapsPercentToOpacityFraction()
        {
            Assert.AreEqual(1.0, _converter.Convert(100, typeof(double), null, null));
            Assert.AreEqual(0.4, _converter.Convert(40, typeof(double), null, null));
            Assert.AreEqual(0.1, _converter.Convert(10, typeof(double), null, null));
        }

        [TestMethod]
        public void Convert_NonIntFallsBackToOpaque()
        {
            Assert.AreEqual(1.0, _converter.Convert(null, typeof(double), null, null));
            Assert.AreEqual(1.0, _converter.Convert("40", typeof(double), null, null));
        }
    }
}
