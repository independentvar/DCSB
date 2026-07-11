using DCSB.Business;
using DCSB.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DCSB.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class SoundErrorTests
    {
        [TestMethod]
        public void CommonPlaybackFailuresHaveHumanReadableMessages()
        {
            Assert.AreEqual(
                "File is missing — was it moved or deleted?",
                SoundManager.GetUserFriendlySoundError(new FileNotFoundException()));
            Assert.AreEqual(
                "This file's format isn't supported.",
                SoundManager.GetUserFriendlySoundError(new InvalidDataException()));
            Assert.AreEqual(
                "Output device unavailable.",
                SoundManager.GetUserFriendlySoundError(new COMException()));
        }

        [TestMethod]
        public void AddingCorruptSoundFileShowsFormatErrorImmediately()
        {
            string file = Path.Combine(Path.GetTempPath(), $"dcsb-corrupt-{Guid.NewGuid():N}.mp3");
            File.WriteAllText(file, "this is really a text file");
            Func<string, string> previousValidator = Sound.FileValidator;
            try
            {
                Sound.FileValidator = SoundManager.ValidateSoundFile;
                Sound sound = new Sound();

                sound.Files.Add(file);

                StringAssert.Contains(sound.Error, Path.GetFileName(file));
                StringAssert.Contains(sound.Error, "This file's format isn't supported.");
            }
            finally
            {
                Sound.FileValidator = previousValidator;
                File.Delete(file);
            }
        }

        [TestMethod]
        public void ChangingFilesClearsPreviousValidationError()
        {
            Func<string, string> previousValidator = Sound.FileValidator;
            string file = Path.GetTempFileName();
            try
            {
                Sound.FileValidator = _ => null;
                Sound sound = new Sound { Error = "old error" };

                sound.Files.Add(file);

                Assert.IsNull(sound.Error);
            }
            finally
            {
                Sound.FileValidator = previousValidator;
                File.Delete(file);
            }
        }

        [TestMethod]
        public void SuccessfulPlayClearsPreviousError()
        {
            ConfigurationModel configuration = new ConfigurationModel
            {
                PrimaryOutput = "Disabled",
                SecondaryOutput = "Disabled",
                MicrophoneInput = "Disabled"
            };
            using (SoundManager manager = new SoundManager(configuration))
            {
                Sound sound = new Sound { Error = "old playback failure" };
                sound.Files.Add("sound.wav");
                sound.Error = "old playback failure";

                manager.Play(sound);

                Assert.IsNull(sound.Error);
            }
        }
    }
}
