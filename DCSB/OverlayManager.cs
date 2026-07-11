using System;
using System.Windows.Threading;
using DCSB.Utils;
using DCSB.ViewModels;

namespace DCSB
{
    // Shows the sound overlay while a fullscreen application (a game) is in the
    // foreground and hides it otherwise. Polling once a second mirrors the seekbar
    // watchdog: there is no window event on this side when another process goes
    // fullscreen or closes.
    public class OverlayManager : IDisposable
    {
        private readonly ViewModel _viewModel;
        private readonly DispatcherTimer _timer;
        private OverlayWindow _window;

        public OverlayManager(ViewModel viewModel)
        {
            _viewModel = viewModel;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (sender, e) => Update();
            _timer.Start();
        }

        private void Update()
        {
            DisplayOption enable = _viewModel.ConfigurationModel.Enable;
            bool soundOverlayEnabled = _viewModel.ConfigurationModel.OverlayEnabled
                && (enable == DisplayOption.Sounds || enable == DisplayOption.Both);
            bool soundOverlayVisible = soundOverlayEnabled
                && _viewModel.ConfigurationModel.SelectedPreset.SoundCollection.Count > 0;
            bool fullscreen = FullscreenDetector.TryGetFullscreenAppBounds(
                out int left, out int top, out int width, out int height);
            bool administratorWarning = fullscreen
                && _viewModel.NotAdministrator == System.Windows.Visibility.Visible
                && FullscreenDetector.IsElevatedFullscreenAppForeground();

            _viewModel.SoundOverlayVisibility = soundOverlayVisible
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            _viewModel.AdministratorOverlayWarningVisibility = administratorWarning
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            if (fullscreen && (soundOverlayVisible || administratorWarning))
            {
                if (_window == null)
                {
                    _window = new OverlayWindow { DataContext = _viewModel };
                }
                _window.ShowOver(left, top, width, height);
            }
            else if (_window != null && _window.IsVisible)
            {
                _window.Hide();
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            if (_window != null)
            {
                _window.Close();
                _window = null;
            }
        }
    }
}
