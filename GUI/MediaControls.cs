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

        private readonly Path _pauseIcon;
        private readonly Path _cycleIcon;
        private readonly Path _autoIcon;

        private readonly Ellipse _pauseEllipse;
        private readonly Ellipse _cycleEllipse;
        private readonly Ellipse _autoEllipse;

        private readonly Brush _pauseActiveColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x80));  // brighter pastel orange
        private readonly Brush _cycleActiveColor = new SolidColorBrush(Color.FromRgb(0xCC, 0x99, 0xE6));  // brighter pastel purple
        private readonly Brush _autoActiveColor = new SolidColorBrush(Color.FromRgb(0x80, 0xE6, 0x80));   // brighter pastel green

        private readonly Brush _inactiveColor = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)); // gray

        public MediaControl(StackPanel controlPanel, Button pauseButton, Button stopButton, 
                          Button cycleButton, Button autoButton, Slider volumeSlider)
        {
            _controlPanel = controlPanel;
            _pauseButton = pauseButton;
            _stopButton = stopButton;
            _cycleButton = cycleButton;
            _autoButton = autoButton;
            _volumeSlider = volumeSlider;

            ExtractButtonElements(_pauseButton, out _pauseIcon, out _pauseEllipse);
            ExtractButtonElements(_cycleButton, out _cycleIcon, out _cycleEllipse);
            ExtractButtonElements(_autoButton, out _autoIcon, out _autoEllipse);
        }

        private void ExtractButtonElements(Button button, out Path icon, out Ellipse ellipse)
        {
            icon = null;
            ellipse = null;

            if (button?.Content is Viewbox viewbox)
            {
                if (viewbox.Child is Canvas canvas)
                {
                    foreach (var child in canvas.Children)
                    {
                        if (child is Path path)
                            icon = path;
                        else if (child is Ellipse ell)
                            ellipse = ell;
                    }
                }
            }
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
            if (_pauseEllipse != null)
                _pauseEllipse.Fill = isPaused ? _inactiveColor : _pauseActiveColor;

            _autoButton.ToolTip = isAutoPlaying ? guiStrings.TooltipAutoOn : guiStrings.TooltipAutoOff;
            if (_autoEllipse != null)
                _autoEllipse.Fill = isAutoPlaying ? _autoActiveColor : _inactiveColor;

            _cycleButton.ToolTip = isAutoCycling ? guiStrings.TooltipCycleOn : guiStrings.TooltipCycleOff;
            if (_cycleEllipse != null)
                _cycleEllipse.Fill = isAutoCycling ? _cycleActiveColor : _inactiveColor;
        }

        public double Volume
        {
            get => _volumeSlider.Value;
            set => _volumeSlider.Value = value;
        }
    }
}