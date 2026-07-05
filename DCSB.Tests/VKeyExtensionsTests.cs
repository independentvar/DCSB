using DCSB.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DCSB.Tests
{
    [TestClass]
    public class VKeyExtensionsTests
    {
        [TestMethod]
        public void ToDisplayString_NumberRowKeys_AreBareDigits()
        {
            Assert.AreEqual("0", VKey.KEY_0.ToDisplayString());
            Assert.AreEqual("1", VKey.KEY_1.ToDisplayString());
            Assert.AreEqual("9", VKey.KEY_9.ToDisplayString());
        }

        [TestMethod]
        public void ToDisplayString_LetterKeys_AreBareLetters()
        {
            Assert.AreEqual("A", VKey.KEY_A.ToDisplayString());
            Assert.AreEqual("M", VKey.KEY_M.ToDisplayString());
            Assert.AreEqual("Z", VKey.KEY_Z.ToDisplayString());
        }

        [TestMethod]
        public void ToDisplayString_NumpadKeys_UseNumPrefix()
        {
            Assert.AreEqual("Num 0", VKey.NUMPAD0.ToDisplayString());
            Assert.AreEqual("Num 3", VKey.NUMPAD3.ToDisplayString());
            Assert.AreEqual("Num 9", VKey.NUMPAD9.ToDisplayString());
            Assert.AreEqual("Num +", VKey.ADD.ToDisplayString());
            Assert.AreEqual("Num .", VKey.DECIMAL.ToDisplayString());
        }

        [TestMethod]
        public void ToDisplayString_ModifiersAndNamedKeys_AreFriendly()
        {
            Assert.AreEqual("Shift", VKey.SHIFT.ToDisplayString());
            Assert.AreEqual("Ctrl", VKey.CONTROL.ToDisplayString());
            Assert.AreEqual("Alt", VKey.MENU.ToDisplayString());
            Assert.AreEqual("Caps Lock", VKey.CAPITAL.ToDisplayString());
            Assert.AreEqual("Tab", VKey.TAB.ToDisplayString());
            Assert.AreEqual("Enter", VKey.RETURN.ToDisplayString());
            Assert.AreEqual("Backspace", VKey.BACK.ToDisplayString());
            Assert.AreEqual("Print Screen", VKey.SNAPSHOT.ToDisplayString());
        }

        [TestMethod]
        public void ToDisplayString_FunctionAndUnmappedKeys_KeepEnumName()
        {
            Assert.AreEqual("F1", VKey.F1.ToDisplayString());
            Assert.AreEqual("F24", VKey.F24.ToDisplayString());
            Assert.AreEqual("ATTN", VKey.ATTN.ToDisplayString());
        }

        [TestMethod]
        public void ToDisplayString_MouseButtons_AreNamedClicks()
        {
            Assert.AreEqual("Left Click", VKey.LBUTTON.ToDisplayString());
            Assert.AreEqual("Right Click", VKey.RBUTTON.ToDisplayString());
            Assert.AreEqual("Mouse 4", VKey.XBUTTON1.ToDisplayString());
        }
    }
}
