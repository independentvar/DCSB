using CommunityToolkit.Mvvm.ComponentModel;

namespace DCSB.Models
{
    public enum MidiMessageKind
    {
        Note,
        ControlChange
    }

    public class MidiBinding : ObservableObject
    {
        private int _channel;
        public int Channel
        {
            get { return _channel; }
            set { SetProperty(ref _channel, value); OnPropertyChanged(nameof(DisplayName)); }
        }

        private MidiMessageKind _kind;
        public MidiMessageKind Kind
        {
            get { return _kind; }
            set { SetProperty(ref _kind, value); OnPropertyChanged(nameof(DisplayName)); }
        }

        private int _number;
        public int Number
        {
            get { return _number; }
            set { SetProperty(ref _number, value); OnPropertyChanged(nameof(DisplayName)); }
        }

        [System.Xml.Serialization.XmlIgnore]
        public string DisplayName => $"Ch {Channel + 1} {(Kind == MidiMessageKind.Note ? "Note" : "CC")} {Number}";

        public bool Matches(int channel, MidiMessageKind kind, int number)
        {
            return Channel == channel && Kind == kind && Number == number;
        }

        public MidiBinding Clone()
        {
            return new MidiBinding { Channel = Channel, Kind = Kind, Number = Number };
        }
    }
}
