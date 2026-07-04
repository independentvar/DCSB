using CommunityToolkit.Mvvm.ComponentModel;
using System.Xml.Serialization;

namespace DCSB.Models
{
    public class OutputDevice : ObservableObject
    {
        private int _number;
        [XmlIgnore]
        public int Number
        {
            get { return _number; }
            set
            {
                _number = value;
                OnPropertyChanged("Number");
            }
        }

        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged("Name");
            }
        }

        public OutputDevice() { }

        public OutputDevice(int number, string name)
        {
            _number = number;
            _name = name;
        }
    }
}
