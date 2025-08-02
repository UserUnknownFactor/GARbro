using System;
using System.Windows;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using GARbro.GUI.Strings;
using GARbro.GUI.Preview;
using GameRes;

namespace GARbro.GUI
{
    public class VideoPreviewControl : Grid
    {
        private MediaElement mediaPlayer;
        private VideoData currentVideo;
        private string currentVideoFile;
        private List<string> tempFiles = new List<string>();
        private DispatcherTimer positionTimer;
        private static float lastVolume = 0.8f;

        // P/Invoke for codec detection
        [DllImport("mfplat.dll", CharSet = CharSet.Unicode)]
        private static extern int MFStartup (uint version, uint flags);

        [DllImport("mfplat.dll")]
        private static extern int MFShutdown();

        private const uint MF_VERSION = 0x00020070; // Version 2.70
        private const uint MFSTARTUP_FULL = 0;

        public string CurrentCodecInfo { get; set; }

        public VideoPreviewControl()
        {
            try
            {
                MFStartup (MF_VERSION, MFSTARTUP_FULL);
            }
            catch { }

            mediaPlayer = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = Stretch.Uniform,
                IsMuted = false,
                Volume = Math.Min(Math.Max(lastVolume, 0f), 1f),
                ScrubbingEnabled = true
            };

            // Enable hardware acceleration if available
            RenderOptions.SetBitmapScalingMode (mediaPlayer, BitmapScalingMode.HighQuality);

            mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

            Children.Add (mediaPlayer);

            positionTimer = new DispatcherTimer();
            positionTimer.Interval = TimeSpan.FromMilliseconds (100);
            positionTimer.Tick += PositionTimer_Tick;

            Unloaded += (s, e) => CleanupVideo();
        }

        public event Action<string> StatusChanged;
        public event Action<TimeSpan, TimeSpan> PositionChanged;
        public event Action MediaEnded;
        public event Action<bool> PlaybackStateChanged;

        public bool IsPlaying { get; private set; }
        public TimeSpan Position => mediaPlayer.Position;
        public TimeSpan Duration => mediaPlayer.NaturalDuration.HasTimeSpan ? mediaPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;

        private void SetVideoStatus (string text)
        {
            StatusChanged?.Invoke (text);
        }

        public void LoadVideo (VideoData videoData)
        {
            bool isSameVideo = currentVideo != null && 
                ((!string.IsNullOrEmpty(videoData.TempFile) && currentVideo.TempFile == videoData.TempFile) ||
                (string.IsNullOrEmpty(videoData.TempFile) && currentVideo.FileName == videoData.FileName));

            if (isSameVideo)
            {
                mediaPlayer.Position = TimeSpan.Zero;
                mediaPlayer.Play();
                positionTimer.Start();
                return;
            }

            CleanupVideo();
            try
            {
                currentVideo = videoData;

                if (!string.IsNullOrEmpty (videoData.TempFile) && File.Exists (videoData.TempFile))
                {
                    currentVideoFile = videoData.TempFile;
                    if (currentVideoFile.StartsWith (Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                        tempFiles.Add (currentVideoFile);
                }
                else
                    currentVideoFile = videoData.FileName;

                mediaPlayer.Source = new Uri (currentVideoFile);
                UpdateCodecInfo (videoData);
                SetVideoStatus (CurrentCodecInfo);
            }
            catch (Exception ex)
            {
                ShowError(Localization.Format("Error loading video: {0}", ex.Message));
                TryAlternativePlayer (currentVideoFile);
            }
        }

        private string GetVideoExtension (VideoData videoData)
        {
            if (!string.IsNullOrEmpty (videoData.Codec))
            {
                var codec = videoData.Codec.ToLower();
                if (codec.Contains ("h264") || codec.Contains ("avc"))
                    return ".mp4";
                else if (codec.Contains("vp8") || codec.Contains ("vp9"))
                    return ".webm";
                else if (codec.Contains("wmv"))
                    return ".wmv";
            }
            return ".mp4"; // Default to MP4
        }

        private void UpdateCodecInfo (VideoData videoData)
        {
            string video_name = videoData.FileName ?? Localization._T("Stream");
            CurrentCodecInfo = Localization.Format("VideoCodecInfo", video_name, videoData.Codec, videoData.Width, videoData.Height, videoData.FrameRate);
        }

        private void TryAlternativePlayer (string videoFile)
        {
            var result = MessageBox.Show(
                Localization._T("VideoPlaybackErrorText"),
                Localization._T("VideoPlaybackError"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start (new ProcessStartInfo
                    {
                        FileName = videoFile,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ShowError(Localization.Format("Failed to open external player: {}", ex.Message));
                }
            }
        }

        private void ShowError (string message)
        {
            MessageBox.Show (message, Localization._T("VideoPlaybackError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void CleanupVideo()
        {
            Stop();
            positionTimer.Stop();

            mediaPlayer.Source = null;

            if (currentVideo != null)
            {
                currentVideo.Dispose();
                currentVideo = null;
            }

            foreach (var file in tempFiles)
            {
                try
                {
                    if (File.Exists (file))
                        File.Delete (file);
                }
                catch { }
            }
            tempFiles.Clear();
        }

        private void MediaPlayer_MediaOpened (object sender, RoutedEventArgs e)
        {
            // Auto-play on load
            Play();
        }

        private void MediaPlayer_MediaEnded (object sender, RoutedEventArgs e)
        {
            Stop();
            MediaEnded?.Invoke();
        }

        private void MediaPlayer_MediaFailed (object sender, ExceptionRoutedEventArgs e)
        {
            string errorMsg = Localization._T("Media failed to load");
            if (e.ErrorException != null)
            {
                errorMsg += $": {e.ErrorException.Message}";
                // Check for common codec issues
                if (e.ErrorException.HResult == unchecked((int)0xC00D11B1))
                    errorMsg += Localization._T("AdditionalCodecsNeeded");
            }

            ShowError (errorMsg);

            if (!string.IsNullOrEmpty (currentVideoFile))
                TryAlternativePlayer (currentVideoFile);
        }

        private void PositionTimer_Tick (object sender, EventArgs e)
        {
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                PositionChanged?.Invoke (mediaPlayer.Position, mediaPlayer.NaturalDuration.TimeSpan);
            }
        }

        public void Play()
        {
            mediaPlayer.Play();
            IsPlaying = true;
            positionTimer.Start();
            PlaybackStateChanged?.Invoke (true);
        }

        public void Pause()
        {
            mediaPlayer.Pause();
            IsPlaying = false;
            positionTimer.Stop();
            PlaybackStateChanged?.Invoke (false);
        }

        public void Stop()
        {
            mediaPlayer.Stop();
            mediaPlayer.Position = TimeSpan.Zero;
            IsPlaying = false;
            positionTimer.Stop();
            PlaybackStateChanged?.Invoke (false);
        }

        public void Seek (TimeSpan position)
        {
            mediaPlayer.Position = position;
        }

        public void SetVolume (double volume)
        {
            mediaPlayer.Volume = Math.Max (0, Math.Min (1, volume));
            lastVolume = (float)volume;
        }

        ~VideoPreviewControl()
        {
            try
            {
                MFShutdown();
            }
            catch { }
        }
    }

    public class VideoPreviewHandler : PreviewHandlerBase
    {
        private readonly MainWindow _mainWindow;
        private readonly VideoPreviewControl _videoControl;

        public override bool IsActive => _videoControl.Visibility == Visibility.Visible;

        public bool IsPlaying => _videoControl.IsPlaying;

        public VideoPreviewHandler(MainWindow mainWindow, VideoPreviewControl videoControl)
        {
            _mainWindow = mainWindow;
            _videoControl = videoControl;
        }

        public override void LoadContent(PreviewFile preview)
        {
            try
            {
                _mainWindow.SetFileStatus(Localization._T("Loading video..."));
                VideoData videoData = null;
                using (var input = VFS.OpenBinaryStream(preview.Entry))
                {
                    videoData = VideoFormat.Read(input);
                }

                // Let MainWindow handle the UI updates
                _mainWindow.ShowVideoPreview(_videoControl);
                _videoControl.LoadVideo(videoData);
                _mainWindow.SetFileStatus("");
            }
            catch (Exception X)
            {
                Reset();
                _mainWindow.SetPreviewStatus("");
                _mainWindow.SetFileStatus(Localization.Format("Video error: {}", X.Message));
            }
        }

        public void Play() => _videoControl.Play();
        public void Pause() => _videoControl.Pause();
        public void Stop() => _videoControl.Stop();
        public void SetVolume(double volume) => _videoControl.SetVolume(volume);

        public override void Reset()
        {
            _videoControl.Stop();
            _videoControl.Visibility = Visibility.Collapsed;
        }
    }
}