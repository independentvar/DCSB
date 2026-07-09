using CommunityToolkit.Mvvm.ComponentModel;
using DCSB.Business;
using DCSB.Models;
using DCSB.SoundPlayer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace DCSB.ViewModels
{
    // Drives the first-run setup wizard. The four steps are, in order:
    //   1. detect a virtual audio cable (or point the user at the download),
    //   2. auto-configure the known-good routing through the existing device setters,
    //   3. verify by playing a test tone and metering both outputs plus the cable's
    //      own recording endpoint, so a broken cable is caught here instead of in a call,
    //   4. tell the user to select the cable's recording device in Discord/their game.
    // Device changes are applied through the parent ViewModel's setters so the running
    // engines and the saved config stay exactly in sync with the normal settings page.
    public class WizardViewModel : ObservableObject
    {
        public const int FirstStep = 1;
        public const int LastStep = 4;

        // VB-Audio's official download page for the free Virtual Cable.
        private const string CableDownloadUrl = "https://vb-audio.com/Cable/";

        // a peak above this counts as "signal present"; the beep plays near 0.6 so it
        // clears this easily, while idle noise and silence stay below it
        private const float SignalThreshold = 0.02f;

        // how long a single test runs before the verdict is evaluated - long enough to
        // cover the beep plus the cable's capture latency and buffering
        private static readonly TimeSpan TestDuration = TimeSpan.FromMilliseconds(1200);

        private static readonly string[] CableRenderHints = { "cable input", "vb-audio", "vb audio", "virtual" };
        private static readonly string[] CableCaptureHints = { "cable output", "vb-audio", "vb audio", "virtual" };

        private readonly ViewModel _parent;
        private readonly ConfigurationModel _configurationModel;
        private readonly SoundManager _soundManager;

        private readonly DispatcherTimer _testTimer;
        private bool _autoConfigured;

        // recording endpoint matching the detected cable (e.g. CABLE Output); null when
        // no cable capture device is present, which the verify step reports explicitly
        private string _cableCaptureName;

        private float _primaryPeak;
        private float _secondaryPeak;
        private float _cablePeak;

        public WizardViewModel(ViewModel parent, ConfigurationModel configurationModel, SoundManager soundManager)
        {
            _parent = parent;
            _configurationModel = configurationModel;
            _soundManager = soundManager;

            // level events arrive on audio threads; WPF marshals scalar-binding
            // PropertyChanged itself, matching the microphone meter pattern
            _soundManager.PrimaryLevelChanged += (s, level) => UpdateLevel(ref _primaryPeak, level, "PrimaryLevel");
            _soundManager.SecondaryLevelChanged += (s, level) => UpdateLevel(ref _secondaryPeak, level, "SecondaryLevel");
            _soundManager.CableProbeLevelChanged += (s, level) => UpdateLevel(ref _cablePeak, level, "CableProbeLevel");

            _testTimer = new DispatcherTimer { Interval = TestDuration };
            _testTimer.Tick += (s, e) => FinishTest();

            _currentStep = FirstStep;
            Recheck();
        }

        private void UpdateLevel(ref float peakField, float level, string propertyName)
        {
            if (level > peakField) peakField = level;
            SetLevel(propertyName, level);
        }

        private float _primaryLevel;
        private float _secondaryLevel;
        private float _cableProbeLevel;

        public double PrimaryLevel { get { return _primaryLevel; } }
        public double SecondaryLevel { get { return _secondaryLevel; } }
        public double CableProbeLevel { get { return _cableProbeLevel; } }

        private void SetLevel(string propertyName, float value)
        {
            switch (propertyName)
            {
                case "PrimaryLevel": _primaryLevel = value; break;
                case "SecondaryLevel": _secondaryLevel = value; break;
                case "CableProbeLevel": _cableProbeLevel = value; break;
            }
            OnPropertyChanged(propertyName);
        }

        // ---- navigation ----

        private int _currentStep;
        public int CurrentStep
        {
            get { return _currentStep; }
            private set
            {
                _currentStep = value;
                OnPropertyChanged("CurrentStep");
                OnPropertyChanged("StepTitle");
                OnPropertyChanged("Step1Visibility");
                OnPropertyChanged("Step2Visibility");
                OnPropertyChanged("Step3Visibility");
                OnPropertyChanged("Step4Visibility");
                OnPropertyChanged("BackVisibility");
                OnPropertyChanged("NextButtonText");
            }
        }

        public string StepTitle
        {
            get
            {
                switch (_currentStep)
                {
                    case 1: return "Step 1 of 4  •  Virtual audio cable";
                    case 2: return "Step 2 of 4  •  Configure audio routing";
                    case 3: return "Step 3 of 4  •  Test the setup";
                    default: return "Step 4 of 4  •  Point Discord at the cable";
                }
            }
        }

        public Visibility Step1Visibility { get { return _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility Step2Visibility { get { return _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility Step3Visibility { get { return _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility Step4Visibility { get { return _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed; } }

        public Visibility BackVisibility { get { return _currentStep > FirstStep ? Visibility.Visible : Visibility.Collapsed; } }
        public string NextButtonText { get { return _currentStep >= LastStep ? "Finish" : "Next"; } }

        public ICommand NextCommand { get { return new RelayCommand(GoNext, CanGoNext); } }
        private bool CanGoNext()
        {
            // can't leave step 1 until a cable is present - the rest of the wizard
            // assumes one exists
            return _currentStep != 1 || CableDetected;
        }
        private void GoNext()
        {
            if (_currentStep >= LastStep)
            {
                Finish();
                return;
            }

            if (_currentStep == 3)
            {
                StopTest();
            }

            CurrentStep++;

            if (_currentStep == 2)
            {
                ApplyAutoConfigureOnce();
            }
        }

        public ICommand BackCommand { get { return new RelayCommand(GoBack, () => _currentStep > FirstStep); } }
        private void GoBack()
        {
            if (_currentStep == 3)
            {
                StopTest();
            }
            if (_currentStep > FirstStep)
            {
                CurrentStep--;
            }
        }

        public ICommand SkipCommand { get { return new RelayCommand(Finish); } }

        private void Finish()
        {
            // closing the window triggers ClosingCommand, which records completion and
            // tears down any running test
            _parent.ApplicationStateModel.WizardOpened = false;
        }

        // Runs when the wizard window closes, however it was closed (Finish, Skip or the
        // window's X). Records that setup has run so it never auto-shows again, and
        // stops any test still in progress.
        public ICommand ClosingCommand { get { return new RelayCommand(OnClosing); } }
        private void OnClosing()
        {
            StopTest();
            _configurationModel.SetupCompleted = true;
        }

        // ---- step 1: cable detection ----

        private bool _cableDetected;
        public bool CableDetected
        {
            get { return _cableDetected; }
            private set
            {
                _cableDetected = value;
                OnPropertyChanged("CableDetected");
                OnPropertyChanged("CableMissing");
            }
        }
        public bool CableMissing { get { return !_cableDetected; } }

        private string _detectedCableName;
        public string DetectedCableName
        {
            get { return _detectedCableName; }
            private set
            {
                _detectedCableName = value;
                OnPropertyChanged("DetectedCableName");
            }
        }

        public ICommand RecheckCommand { get { return new RelayCommand(Recheck); } }
        private void Recheck()
        {
            DetectedCableName = DetectCable(_soundManager.EnumerateDevices(), CableRenderHints);
            _cableCaptureName = DetectCable(_soundManager.EnumerateInputDevices(), CableCaptureHints);
            CableDetected = DetectedCableName != null;
            OnPropertyChanged("CableCaptureName");

            // re-probed alongside the devices: the control panel appears on disk the
            // moment the user installs VB-Cable mid-wizard
            _cableControlPanelPath = FindCableControlPanel();
            OnPropertyChanged("HasCableControlPanel");

            // devices may have appeared after a fresh VB-Cable install - refresh the
            // dropdowns' choices and re-evaluate whether Next is now allowed
            OnPropertyChanged("AvailableOutputs");
            OnPropertyChanged("AvailableInputs");
            CommandManager.InvalidateRequerySuggested();
        }

        public ICommand OpenCableDownloadCommand { get { return new RelayCommand(OpenCableDownload); } }
        private void OpenCableDownload()
        {
            try
            {
                Process.Start(new ProcessStartInfo(CableDownloadUrl) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        // Picks the best cable device by friendly name. Prefers a plain "CABLE Input"
        // over multi-channel ("16ch") variants, which is the exact choice users get
        // wrong; among equals it takes the shortest name (fewest extra tokens).
        private static string DetectCable(IEnumerable<string> names, string[] hints)
        {
            List<string> candidates = names
                .Where(name => !string.IsNullOrEmpty(name)
                    && name != AudioPlaybackEngine.DisabledDeviceName
                    && name != AudioPlaybackEngine.DefaultDeviceName
                    && name != DCSB.SoundPlayer.MicrophoneInput.DefaultDeviceName
                    && hints.Any(hint => name.ToLowerInvariant().Contains(hint)))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates
                .OrderBy(name => IsMultiChannelVariant(name) ? 1 : 0)
                .ThenBy(name => name.Length)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        private static bool IsMultiChannelVariant(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower.Contains("16ch") || lower.Contains("16 ch") || lower.Contains("hi-fi");
        }

        // ---- step 2: auto-configure ----

        public ICollection<string> AvailableOutputs { get { return _parent.AvailableOutputs; } }
        public ICollection<string> AvailableInputs { get { return _parent.AvailableInputs; } }

        // The three selections delegate to the parent ViewModel's setters, so choosing a
        // device here drives the real engines and is persisted, and the settings page
        // stays in sync.
        public string PrimaryOutput
        {
            get { return _parent.PrimaryOutput; }
            set { _parent.PrimaryOutput = value; OnPropertyChanged("PrimaryOutput"); }
        }
        public string SecondaryOutput
        {
            get { return _parent.SecondaryOutput; }
            set { _parent.SecondaryOutput = value; OnPropertyChanged("SecondaryOutput"); }
        }
        public string MicrophoneInput
        {
            get { return _parent.MicrophoneInput; }
            set { _parent.MicrophoneInput = value; OnPropertyChanged("MicrophoneInput"); }
        }

        // Applies the known-good routing once, the first time step 2 is reached: the
        // user's own speakers as the first output (so they hear their sounds), the
        // detected cable as the second output, and the default microphone captured in.
        // Overrides the user later makes in the dropdowns are kept if they navigate back.
        private void ApplyAutoConfigureOnce()
        {
            if (_autoConfigured)
            {
                return;
            }
            _autoConfigured = true;

            PrimaryOutput = AudioPlaybackEngine.DefaultDeviceName;
            if (DetectedCableName != null)
            {
                SecondaryOutput = DetectedCableName;
            }
            MicrophoneInput = DCSB.SoundPlayer.MicrophoneInput.DefaultDeviceName;
        }

        // ---- step 3: verify ----

        private bool _isTesting;
        public bool IsTesting
        {
            get { return _isTesting; }
            private set
            {
                _isTesting = value;
                OnPropertyChanged("IsTesting");
            }
        }

        public ICommand TestCommand { get { return new RelayCommand(StartTest, () => !_isTesting); } }
        private void StartTest()
        {
            if (_isTesting)
            {
                return;
            }

            _primaryPeak = 0;
            _secondaryPeak = 0;
            _cablePeak = 0;
            VerdictText = null;

            // resolve the cable's recording endpoint fresh each run in case devices changed
            _cableCaptureName = DetectCable(_soundManager.EnumerateInputDevices(), CableCaptureHints);
            OnPropertyChanged("CableCaptureName");
            if (_cableCaptureName != null)
            {
                _soundManager.StartCableProbe(_cableCaptureName);
            }

            IsTesting = true;
            _soundManager.PlayTestSound();
            _testTimer.Start();
        }

        // Ends the test window and turns the metered peaks into a plain-language verdict.
        private void FinishTest()
        {
            StopTest();

            bool secondaryEnabled = SecondaryOutput != AudioPlaybackEngine.DisabledDeviceName
                && !string.IsNullOrEmpty(SecondaryOutput);

            if (!secondaryEnabled)
            {
                SetVerdict(false, "The second output is disabled. Go back and choose your virtual cable as the second output.");
            }
            else if (_secondaryPeak < SignalThreshold)
            {
                SetVerdict(false, "No sound reached the second output. The cable may be in use by another app, or the wrong device is selected.");
            }
            else if (_cableCaptureName == null)
            {
                SetVerdict(false, "Couldn't find the cable's recording device (e.g. CABLE Output). Make sure VB-Cable is fully installed.");
            }
            else if (_cablePeak < SignalThreshold)
            {
                SetVerdict(false, "The sound reached the cable, but nothing came back out of " + _cableCaptureName +
                    ". The virtual cable looks broken or misinstalled - reinstall VB-Cable and reboot.");
            }
            else
            {
                SetVerdict(true, "Sound reached " + _cableCaptureName +
                    ". Discord and your games will hear DCSB through the cable." +
                    (_primaryPeak < SignalThreshold ? " (No level on your speakers - that's fine if the first output is disabled.)" : string.Empty));
            }
        }

        private void StopTest()
        {
            _testTimer.Stop();
            _soundManager.StopCableProbe();
            _soundManager.Stop();
            IsTesting = false;
        }

        private string _verdictText;
        public string VerdictText
        {
            get { return _verdictText; }
            private set
            {
                _verdictText = value;
                OnPropertyChanged("VerdictText");
                OnPropertyChanged("HasVerdict");
            }
        }
        public bool HasVerdict { get { return !string.IsNullOrEmpty(_verdictText); } }

        private bool _testPassed;
        public bool TestPassed
        {
            get { return _testPassed; }
            private set
            {
                _testPassed = value;
                OnPropertyChanged("TestPassed");
            }
        }

        private void SetVerdict(bool passed, string text)
        {
            TestPassed = passed;
            VerdictText = text;
        }

        // ---- step 4 ----

        // The exact recording device the user must select in Discord/their game; naming
        // it removes the guesswork from the one step no app can do for them.
        public string CableCaptureName
        {
            get { return _cableCaptureName ?? "your virtual cable's output (e.g. CABLE Output)"; }
        }

        // VB-Cable's own control panel, where its internal latency ("Max Latency") can
        // be lowered - the cable's buffering is the biggest remaining chunk of voice
        // delay once DCSB itself runs at the OS minimum. Probed rather than hardcoded
        // shown, so the tip's button only appears when it can actually work.
        private string FindCableControlPanel()
        {
            Environment.SpecialFolder[] roots =
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            };
            foreach (Environment.SpecialFolder root in roots)
            {
                string folder = Environment.GetFolderPath(root);
                if (string.IsNullOrEmpty(folder))
                {
                    continue;
                }
                string path = Path.Combine(folder, "VB", "CABLE", "VBCABLE_ControlPanel.exe");
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        private string _cableControlPanelPath;
        public bool HasCableControlPanel { get { return _cableControlPanelPath != null; } }

        public ICommand OpenCableControlPanelCommand { get { return new RelayCommand(OpenCableControlPanel); } }
        private void OpenCableControlPanel()
        {
            if (_cableControlPanelPath == null)
            {
                return;
            }
            ProcessStartInfo startInfo = new ProcessStartInfo(_cableControlPanelPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_cableControlPanelPath),
                // changing the cable's latency writes machine-wide driver settings,
                // which silently fail without elevation - ask for it up front
                Verb = "runas"
            };
            try
            {
                Process.Start(startInfo);
            }
            catch (Exception e)
            {
                // UAC declined (or elevation unavailable): still open the panel
                // read-only rather than doing nothing
                Debug.WriteLine(e);
                try
                {
                    startInfo.Verb = null;
                    Process.Start(startInfo);
                }
                catch (Exception inner)
                {
                    Debug.WriteLine(inner);
                }
            }
        }
    }
}
