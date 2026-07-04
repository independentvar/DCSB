using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace DCSB.Models
{
    public class Counter : ObservableObject, ICloneable
    {
        private string _name;
        [XmlElement(Order = 1)]
        public string Name {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged("Name");
            }
        }

        private string _file;
        [XmlElement(Order = 4)]
        public string File
        {
            get { return _file; }
            set
            {
                _file = value;
                OnPropertyChanged("File");
                ReadFromFile();
                // if the file content could not be parsed, don't overwrite it with the
                // current (likely stale) count - that would destroy the user's data
                if (Error == null)
                {
                    WriteToFile();
                }
            }
        }

        private string _format;
        [XmlElement(Order = 3)]
        public string Format
        {
            get { return _format; }
            set
            {
                _format = value;
                OnPropertyChanged("Format");
                WriteToFile();
            }
        }

        private int _count;
        [XmlIgnore]
        public int Count
        {
            get { return _count; }
            set
            {
                _count = value;
                OnPropertyChanged("Count");
                WriteToFile();
            }
        }

        private int _increment;
        [XmlElement(Order = 2)]
        public int Increment
        {
            get { return _increment; }
            set
            {
                _increment = value;
                OnPropertyChanged("Increment");
            }
        }

        private string _error;
        [XmlIgnore]
        public string Error
        {
            get { return _error; }
            set
            {
                _error = value;
                OnPropertyChanged("Error");
            }
        }

        public Counter()
        {
            Format = "{0}";
            Increment = 1;
        }

        public object Clone()
        {
            return new Counter() { Name = Name, Increment = Increment, Format = Format, File = File, Count = Count };
        }

        public void WriteToFile()
        {
            Error = null;
            if (System.IO.File.Exists(File))
            {
                string formatted;
                try
                {
                    formatted = string.Format(Format, Count);
                    System.IO.File.WriteAllText(File, formatted);
                }
                catch (FormatException)
                {
                    Error = string.Format("Format '{0}' is invalid.", Format);
                }
                catch (UnauthorizedAccessException)
                {
                    Error = string.Format("Unauthorized to access file '{0}'.", File);
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                }
            }
            else
            {
                Error = string.Format("File '{0}' does not exist.", File);
            }
        }

        public void ReadFromFile()
        {
            Error = null;
            if (System.IO.File.Exists(File))
            {
                string fileContent;
                try
                {
                    fileContent = System.IO.File.ReadAllText(File);

                    if (fileContent == "")
                    {
                        return;
                    }

                    // escape the format so literal regex metacharacters (e.g. "({0})") don't
                    // break the pattern; allow a sign (Count can go negative via decrement)
                    // and trailing whitespace (external editors often append a newline)
                    string pattern = "^" + Regex.Escape(Format).Replace(@"\{0}", @"(?<count>-?\d+)") + @"\s*$";
                    Regex regex = new Regex(pattern);
                    Match match = regex.Match(fileContent);

                    if (!match.Success)
                    {
                        Error = string.Format("Format '{0}' does not match file {1} content.", Format, File);
                        return;
                    }

                    Count = int.Parse(match.Groups["count"].Value);
                }
                catch (UnauthorizedAccessException)
                {
                    Error = string.Format("Unauthorized to access file '{0}'.", File);
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                }
            }
            else
            {
                Error = string.Format("File '{0}' does not exist.", File);
            }
        }
    }
}
