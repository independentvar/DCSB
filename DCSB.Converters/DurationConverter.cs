using System;
using System.Globalization;
using System.Windows.Data;

namespace DCSB.Converters
{
    public class DurationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan duration)
            {
                return string.Format("{0}:{1:00}", (int)duration.TotalMinutes, duration.Seconds);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
