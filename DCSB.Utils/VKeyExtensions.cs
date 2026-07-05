using System.Collections.Generic;

namespace DCSB.Utils
{
    public static class VKeyExtensions
    {
        private static readonly Dictionary<VKey, string> _displayNames = new Dictionary<VKey, string>
        {
            { VKey.LBUTTON, "Left Click" },
            { VKey.RBUTTON, "Right Click" },
            { VKey.MBUTTON, "Middle Click" },
            { VKey.XBUTTON1, "Mouse 4" },
            { VKey.XBUTTON2, "Mouse 5" },
            { VKey.BACK, "Backspace" },
            { VKey.TAB, "Tab" },
            { VKey.CLEAR, "Clear" },
            { VKey.RETURN, "Enter" },
            { VKey.SHIFT, "Shift" },
            { VKey.CONTROL, "Ctrl" },
            { VKey.MENU, "Alt" },
            { VKey.PAUSE, "Pause" },
            { VKey.CAPITAL, "Caps Lock" },
            { VKey.ESCAPE, "Esc" },
            { VKey.SPACE, "Space" },
            { VKey.PAGE_UP, "Page Up" },
            { VKey.PAGE_DOWN, "Page Down" },
            { VKey.END, "End" },
            { VKey.HOME, "Home" },
            { VKey.LEFT, "Left" },
            { VKey.UP, "Up" },
            { VKey.RIGHT, "Right" },
            { VKey.DOWN, "Down" },
            { VKey.SNAPSHOT, "Print Screen" },
            { VKey.INSERT, "Insert" },
            { VKey.DELETE, "Delete" },
            { VKey.LWIN, "Left Win" },
            { VKey.RWIN, "Right Win" },
            { VKey.APPS, "Apps" },
            { VKey.MULTIPLY, "Num *" },
            { VKey.ADD, "Num +" },
            { VKey.SUBTRACT, "Num -" },
            { VKey.DECIMAL, "Num ." },
            { VKey.DIVIDE, "Num /" },
            { VKey.NUMLOCK, "Num Lock" },
            { VKey.SCROLL, "Scroll Lock" },
            { VKey.LSHIFT, "Left Shift" },
            { VKey.RSHIFT, "Right Shift" },
            { VKey.LCONTROL, "Left Ctrl" },
            { VKey.RCONTROL, "Right Ctrl" },
            { VKey.LMENU, "Left Alt" },
            { VKey.RMENU, "Right Alt" },
            { VKey.BROWSER_BACK, "Browser Back" },
            { VKey.BROWSER_FORWARD, "Browser Forward" },
            { VKey.BROWSER_REFRESH, "Browser Refresh" },
            { VKey.BROWSER_STOP, "Browser Stop" },
            { VKey.BROWSER_SEARCH, "Browser Search" },
            { VKey.BROWSER_FAVORITES, "Browser Favorites" },
            { VKey.BROWSER_HOME, "Browser Home" },
            { VKey.VOLUME_MUTE, "Volume Mute" },
            { VKey.VOLUME_DOWN, "Volume Down" },
            { VKey.VOLUME_UP, "Volume Up" },
            { VKey.MEDIA_NEXT_TRACK, "Media Next" },
            { VKey.MEDIA_PREV_TRACK, "Media Previous" },
            { VKey.MEDIA_STOP, "Media Stop" },
            { VKey.MEDIA_PLAY_PAUSE, "Media Play/Pause" },
            // OEM keys as on the US standard keyboard, like the enum's own comments
            { VKey.OEM_1, ";" },
            { VKey.OEM_PLUS, "=" },
            { VKey.OEM_COMMA, "," },
            { VKey.OEM_MINUS, "-" },
            { VKey.OEM_PERIOD, "." },
            { VKey.OEM_2, "/" },
            { VKey.OEM_3, "`" },
            { VKey.OEM_4, "[" },
            { VKey.OEM_5, "\\" },
            { VKey.OEM_6, "]" },
            { VKey.OEM_7, "'" },
            { VKey.OEM_102, "\\" },
        };

        public static string ToDisplayString(this VKey key)
        {
            if (key >= VKey.KEY_0 && key <= VKey.KEY_9)
            {
                return ((char)('0' + (key - VKey.KEY_0))).ToString();
            }
            if (key >= VKey.KEY_A && key <= VKey.KEY_Z)
            {
                return ((char)('A' + (key - VKey.KEY_A))).ToString();
            }
            if (key >= VKey.NUMPAD0 && key <= VKey.NUMPAD9)
            {
                return "Num " + (key - VKey.NUMPAD0);
            }
            if (_displayNames.TryGetValue(key, out string name))
            {
                return name;
            }
            // F1-F24 and anything exotic keep their enum name
            return key.ToString();
        }
    }
}
