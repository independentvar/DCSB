using NAudio.Midi;
using NAudio;
using System;
using System.Collections.Generic;
using System.Threading;

namespace DCSB.Input
{
    public enum MidiInputMessageKind
    {
        Note,
        ControlChange
    }

    public sealed class MidiInputEventArgs : EventArgs
    {
        public int Channel { get; }
        public MidiInputMessageKind Kind { get; }
        public int Number { get; }

        public MidiInputEventArgs(int channel, MidiInputMessageKind kind, int number)
        {
            Channel = channel;
            Kind = kind;
            Number = number;
        }
    }

    public sealed class MidiInput : IDisposable
    {
        public const string DisabledDeviceName = "Disabled";
        private readonly object _sync = new object();
        private readonly Timer _reconnectTimer;
        private MidiIn _midiIn;
        private string _deviceName;
        private bool _disposed;

        public event EventHandler<MidiInputEventArgs> MessageReceived;

        public MidiInput(string deviceName)
        {
            _deviceName = NormalizeDeviceName(deviceName);
            _reconnectTimer = new Timer(_ => EnsureConnected(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }

        public static IReadOnlyList<string> EnumerateDevices()
        {
            List<string> devices = new List<string> { DisabledDeviceName };
            try
            {
                for (int i = 0; i < MidiIn.NumberOfDevices; i++)
                    devices.Add(MidiIn.DeviceInfo(i).ProductName);
            }
            catch (MmException) { }
            return devices;
        }

        public void SelectDevice(string deviceName)
        {
            lock (_sync)
            {
                _deviceName = NormalizeDeviceName(deviceName);
                CloseDevice();
            }
            EnsureConnected();
        }

        private static string NormalizeDeviceName(string deviceName)
        {
            return string.IsNullOrWhiteSpace(deviceName) || deviceName == DisabledDeviceName ? null : deviceName;
        }

        private void EnsureConnected()
        {
            lock (_sync)
            {
                if (_disposed || _deviceName == null)
                {
                    CloseDevice();
                    return;
                }

                int deviceNumber = FindDevice(_deviceName);
                if (deviceNumber < 0)
                {
                    CloseDevice();
                    return;
                }
                if (_midiIn != null)
                    return;

                try
                {
                    _midiIn = new MidiIn(deviceNumber);
                    _midiIn.MessageReceived += OnMessageReceived;
                    _midiIn.ErrorReceived += OnErrorReceived;
                    _midiIn.Start();
                }
                catch (MmException)
                {
                    CloseDevice();
                }
            }
        }

        private static int FindDevice(string name)
        {
            try
            {
                for (int i = 0; i < MidiIn.NumberOfDevices; i++)
                    if (string.Equals(MidiIn.DeviceInfo(i).ProductName, name, StringComparison.Ordinal)) return i;
            }
            catch (MmException) { }
            return -1;
        }

        private void OnMessageReceived(object sender, MidiInMessageEventArgs e)
        {
            if (e.MidiEvent is NoteEvent note && note.CommandCode == MidiCommandCode.NoteOn && note.Velocity > 0)
                MessageReceived?.Invoke(this, new MidiInputEventArgs(note.Channel - 1, MidiInputMessageKind.Note, note.NoteNumber));
            // Many pad controllers send CC value 0 on release; treat that like
            // NoteOff so one press produces exactly one trigger.
            else if (e.MidiEvent is ControlChangeEvent cc && cc.ControllerValue > 0)
                MessageReceived?.Invoke(this, new MidiInputEventArgs(cc.Channel - 1, MidiInputMessageKind.ControlChange, (int)cc.Controller));
        }

        private void OnErrorReceived(object sender, MidiInMessageEventArgs e)
        {
            lock (_sync) CloseDevice();
        }

        private void CloseDevice()
        {
            MidiIn midiIn = _midiIn;
            _midiIn = null;
            if (midiIn == null) return;
            midiIn.MessageReceived -= OnMessageReceived;
            midiIn.ErrorReceived -= OnErrorReceived;
            try { midiIn.Stop(); } catch (MmException) { }
            midiIn.Dispose();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _reconnectTimer.Dispose();
                CloseDevice();
            }
        }
    }
}
