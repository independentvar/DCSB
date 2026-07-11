using DCSB.Models;
using DCSB.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DCSB.Business
{
    public class ShortcutManager
    {
        private ApplicationStateModel _applicationStateModel;
        private ConfigurationModel _configurationModel;
        private SoundManager _soundManager;

        private Random _random;

        public ShortcutManager(ApplicationStateModel applicationStateModel, ConfigurationModel configurationModel, SoundManager soundManager)
        {
            _applicationStateModel = applicationStateModel;
            _configurationModel = configurationModel;
            _soundManager = soundManager;
            _random = new Random();
        }

        public void KeyUp(VKey key, List<VKey> pressedKeys)
        {
            if (_applicationStateModel.ModifiedBindable != null)
            {
                // Escape cancels the binding instead of being recorded, so there is a way
                // out of the "Press keys…" state without committing a key.
                if (key == VKey.ESCAPE)
                {
                    _applicationStateModel.ModifiedBindable = null;
                    return;
                }

                _applicationStateModel.ModifiedBindable.Keys.Clear();
                foreach (VKey pressedKey in pressedKeys)
                    _applicationStateModel.ModifiedBindable.Keys.Add(pressedKey);

                _applicationStateModel.ModifiedBindable = null;
            }
        }

        public void KeyDown(VKey key, List<VKey> pressedKeys)
        {
            if (_configurationModel.Enable == DisplayOption.Counters || _configurationModel.Enable == DisplayOption.Both)
            {
                Shortcut shortcut = ResolveShortcut(key, pressedKeys, new List<Shortcut>(){
                    _configurationModel.CounterShortcuts.Next,
                    _configurationModel.CounterShortcuts.Previous,
                    _configurationModel.CounterShortcuts.Increment,
                    _configurationModel.CounterShortcuts.Decrement,
                    _configurationModel.CounterShortcuts.Reset
                });
                if (shortcut != null && shortcut.Command.CanExecute(null))
                {
                    shortcut.Command.Execute(null);
                }
            }

            if (_configurationModel.Enable == DisplayOption.Sounds || _configurationModel.Enable == DisplayOption.Both)
            {
                Shortcut shortcut = ResolveShortcut(key, pressedKeys, new List<Shortcut>(){
                    _configurationModel.SoundShortcuts.Pause,
                    _configurationModel.SoundShortcuts.Continue,
                    _configurationModel.SoundShortcuts.Stop
                });
                if (shortcut != null && shortcut.Command.CanExecute(null))
                {
                    shortcut.Command.Execute(null);
                }
            }

            // the microphone leg is independent of the sounds display option (muting
            // sounds must not mute the voice), so this shortcut is always active
            Shortcut muteMicrophone = ResolveShortcut(key, pressedKeys, new List<Shortcut>() {
                _configurationModel.SoundShortcuts.MuteMicrophone
            });
            if (muteMicrophone != null && muteMicrophone.Command.CanExecute(null))
            {
                muteMicrophone.Command.Execute(null);
            }
        }

        public void KeyPress(VKey key, List<VKey> pressedKeys)
        {
            if (_configurationModel.Enable == DisplayOption.Sounds || _configurationModel.Enable == DisplayOption.Both)
            {
                Sound sound= ResolveShortcut(key, pressedKeys, _configurationModel.SelectedPreset.SoundCollection.Where(x => x.Files.Count != 0));
                if (sound != null)
                {
                    _configurationModel.SelectedPreset.SelectedSound = sound;
                    _soundManager.Toggle(sound);
                }
            }

            Preset preset = ResolveShortcut(key, pressedKeys, _configurationModel.PresetCollection);
            if (preset != null)
            {
                _configurationModel.SelectedPreset = preset;
                foreach (Counter counter in preset.CounterCollection)
                {
                    counter.ReadFromFile();
                }
            }
        }

        public void MidiMessage(int channel, MidiMessageKind kind, int number)
        {
            if (_applicationStateModel.ModifiedMidiSound != null)
            {
                Sound learningSound = _applicationStateModel.ModifiedMidiSound;
                learningSound.MidiBinding = new MidiBinding
                {
                    Channel = channel,
                    Kind = kind,
                    Number = number
                };
                learningSound.IsMidiLearning = false;
                _applicationStateModel.ModifiedMidiSound = null;
                return;
            }

            if (_configurationModel.Enable != DisplayOption.Sounds && _configurationModel.Enable != DisplayOption.Both)
                return;

            Sound sound = _configurationModel.SelectedPreset.SoundCollection.FirstOrDefault(x =>
                x.Files.Count != 0 && x.MidiBinding != null && x.MidiBinding.Matches(channel, kind, number));
            if (sound != null)
            {
                _configurationModel.SelectedPreset.SelectedSound = sound;
                _soundManager.Toggle(sound);
            }
        }

        private T ResolveShortcut<T>(VKey key, IEnumerable<VKey> pressedKeys, IEnumerable<T> items) where T : IBindable
        {
            return items.Where(x => x.Keys.Contains(key) && x.Keys.All(y => pressedKeys.Contains(y))).OrderBy(x => x.Keys.Count).LastOrDefault();
        }
    }
}
