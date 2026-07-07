namespace DCSB
{
    // Thin shim so the MVVM OpenCloseWindowBehavior (which instantiates a window
    // through its parameterless constructor) can open the overlay in edit mode.
    // All the drag/resize/save behaviour lives in OverlayWindow.
    public class OverlayEditWindow : OverlayWindow
    {
        public OverlayEditWindow() : base(true)
        {
        }
    }
}
