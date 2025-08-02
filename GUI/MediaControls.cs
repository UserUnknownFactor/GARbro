using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using GARbro.GUI.Strings;

namespace GARbro.GUI
{
    /// <summary>
    /// Manages media playback controls UI that are already defined in XAML.
    /// </summary>
    public class MediaControl
    {
        private readonly StackPanel _controlPanel;
        private readonly Button _pauseButton;
        private readonly Button _stopButton;
        private readonly Button _cycleButton;
        private readonly Button _autoButton;
        private readonly Slider _volumeSlider;
        
        // Icon paths for button states
        private readonly Path _pauseIcon;
        private readonly Path _cycleIcon;
        private readonly Path _autoIcon;

        public MediaControl(StackPanel controlPanel, Button pauseButton, Button stopButton, 
                          Button cycleButton, Button autoButton, Slider volumeSlider)
        {
            _controlPanel = controlPanel;
            _pauseButton = pauseButton;
            _stopButton = stopButton;
            _cycleButton = cycleButton;
            _autoButton = autoButton;
            _volumeSlider = volumeSlider;

            // Get icon references from buttons
            _pauseIcon = _pauseButton.Content as Path;
            _cycleIcon = _cycleButton.Content as Path;
            _autoIcon = _autoButton.Content as Path;
        }

        /// <summary>
        /// Show or hide the media control panel.
        /// </summary>
        public void SetVisibility(Visibility visibility)
        {
            _controlPanel.Visibility = visibility;
        }

        /// <summary>
        /// Configure controls for specific media type.
        /// </summary>
        public void ConfigureForMediaType(MediaType mediaType)  // Now MediaType is public
        {
            switch (mediaType)
            {
                case MediaType.Audio:
                    _controlPanel.Visibility = Visibility.Visible;
                    _cycleButton.Visibility = Visibility.Visible;
                    _autoButton.Visibility = Visibility.Visible;
                    break;
                case MediaType.Video:
                    _controlPanel.Visibility = Visibility.Visible;
                    _cycleButton.Visibility = Visibility.Collapsed;
                    _autoButton.Visibility = Visibility.Collapsed;
                    break;
                case MediaType.None:
                    _controlPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        /// <summary>
        /// Update button states and tooltips.
        /// </summary>
        public void UpdateButtonStates(bool isPaused, bool isAutoPlaying, bool isAutoCycling)
        {
            _pauseButton.ToolTip = isPaused ? guiStrings.TooltipResume : guiStrings.TooltipPause;
            if (_pauseIcon != null)
                _pauseIcon.Fill = isPaused ? Brushes.Orange : Brushes.DarkOrange;

            _autoButton.ToolTip = isAutoPlaying ? guiStrings.TooltipAutoOn : guiStrings.TooltipAutoOff;
            if (_autoIcon != null)
                _autoIcon.Fill = isAutoPlaying ? Brushes.LimeGreen : Brushes.SeaGreen;

            _cycleButton.ToolTip = isAutoCycling ? guiStrings.TooltipCycleOn : guiStrings.TooltipCycleOff;
            if (_cycleIcon != null)
                _cycleIcon.Fill = isAutoCycling ? Brushes.Magenta : Brushes.DarkMagenta;
        }

        public double Volume
        {
            get => _volumeSlider.Value;
            set => _volumeSlider.Value = value;
        }
    }
}