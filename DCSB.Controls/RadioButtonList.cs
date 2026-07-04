using System.Windows;
using System.Windows.Controls;

namespace DCSB.Controls
{
    public class RadioButtonList : ListBox
    {
        static RadioButtonList()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(RadioButtonList), new FrameworkPropertyMetadata(typeof(RadioButtonList)));
        }
    }
}
