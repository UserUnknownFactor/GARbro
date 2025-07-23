using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GARbro.GUI.Strings;
using GARbro.GUI.Properties;
using GameRes;
using System.Text;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Controls.Primitives;

namespace GARbro.GUI
{
    // Class to handle animated images
    public class AnimatedImageViewer : Image
    {
        private List<BitmapSource> frames = new List<BitmapSource>();
        private List<int> frameDelays = new List<int>();
        private int currentFrameIndex = 0;
        private DispatcherTimer animationTimer;
        private bool isAnimated = false;

        public AnimatedImageViewer()
        {
            animationTimer = new DispatcherTimer();
            animationTimer.Tick += OnFrameChange;
            Unloaded += (s, e) => StopAnimation();
        }

        public void LoadImage(BitmapSource image)
        {
            StopAnimation();
            frames.Clear();
            frameDelays.Clear();
            isAnimated = false;
            Source = image;
        }

        public void LoadAnimatedImage(List<BitmapSource> imageFrames, List<int> delays)
        {
            if (imageFrames == null || imageFrames.Count == 0)
                return;

            StopAnimation();
            frames = imageFrames;
            frameDelays = delays ?? Enumerable.Repeat(100, frames.Count).ToList();
            isAnimated = frames.Count > 1;
            currentFrameIndex = 0;

            // Show first frame
            Source = frames[0];

            if (isAnimated)
                StartAnimation();
        }

        private void OnFrameChange(object sender, EventArgs e)
        {
            if (!isAnimated || frames.Count <= 1)
                return;

            currentFrameIndex = (currentFrameIndex + 1) % frames.Count;
            Source = frames[currentFrameIndex];

            // Set the timer for the next frame
            animationTimer.Interval = TimeSpan.FromMilliseconds(frameDelays[currentFrameIndex]);
        }

        public void StartAnimation()
        {
            if (isAnimated && frames.Count > 1)
            {
                animationTimer.Interval = TimeSpan.FromMilliseconds(frameDelays[currentFrameIndex]);
                animationTimer.Start();
            }
        }

        public void StopAnimation()
        {
            animationTimer.Stop();
        }

        public void Reset()
        {
            StopAnimation();
            frames.Clear();
            frameDelays.Clear();
            isAnimated = false;
            Source = null;
        }
    }

    // Video player control for the preview pane
    public class VideoPreviewControl : Grid
    {
        private MediaElement mediaPlayer;
        private Button playPauseButton;
        private Slider timelineSlider;
        private TextBlock timeDisplay;
        private DispatcherTimer timerVideoTime;
        private bool isDragging = false;
        private bool isPlaying = false;
        private VideoData currentVideo;

        public VideoPreviewControl()
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            mediaPlayer = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = Stretch.Uniform,
                IsMuted = false,
                Volume = 1.0
            };
            mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

            var controlsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(5)
            };

            playPauseButton = new Button
            {
                Content = "▶",
                Width = 30,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0)
            };
            playPauseButton.Click += PlayPauseButton_Click;

            timelineSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 5, 0),
                Width = 200
            };
            timelineSlider.ValueChanged += TimelineSlider_ValueChanged;
            timelineSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(TimelineSlider_DragStarted));
            timelineSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(TimelineSlider_DragCompleted));

            timeDisplay = new TextBlock
            {
                Text = "00:00 / 00:00",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 5, 0)
            };

            controlsPanel.Children.Add(playPauseButton);
            controlsPanel.Children.Add(timelineSlider);
            controlsPanel.Children.Add(timeDisplay);

            Children.Add(mediaPlayer);
            Children.Add(controlsPanel);
            SetRow(mediaPlayer, 0);
            SetRow(controlsPanel, 1);

            timerVideoTime = new DispatcherTimer();
            timerVideoTime.Interval = TimeSpan.FromMilliseconds(500);
            timerVideoTime.Tick += TimerVideoTime_Tick;

            Unloaded += (s, e) => CleanupVideo();
        }

        public void LoadVideo(VideoData videoData)
        {
            CleanupVideo();

            try
            {
                currentVideo = videoData;

                if (!string.IsNullOrEmpty(videoData.TempFile))
                {
                    mediaPlayer.Source = new Uri(videoData.TempFile);
                }
                else
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"garbro_video_{Guid.NewGuid()}.mp4");
                    using (var fileStream = File.Create(tempFile))
                    {
                        videoData.Stream.Position = 0;
                        videoData.Stream.CopyTo(fileStream);
                    }
                    mediaPlayer.Source = new Uri(tempFile);
                }

                mediaPlayer.Play();
                isPlaying = true;
                playPauseButton.Content = "⏸";
                timerVideoTime.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CleanupVideo()
        {
            mediaPlayer.Stop();
            isPlaying = false;
            playPauseButton.Content = "▶";
            timerVideoTime.Stop();

            mediaPlayer.Source = null;

            if (currentVideo != null)
            {
                currentVideo.Dispose();
                currentVideo = null;
            }
        }

        private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            timelineSlider.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            UpdateTimeDisplay();
        }

        private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Position = TimeSpan.Zero;
            mediaPlayer.Stop();
            isPlaying = false;
            playPauseButton.Content = "▶";
            timerVideoTime.Stop();
        }

        private void MediaPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            MessageBox.Show($"Media failed to load: {e.ErrorException.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (isPlaying)
            {
                mediaPlayer.Pause();
                playPauseButton.Content = "▶";
                isPlaying = false;
            }
            else
            {
                mediaPlayer.Play();
                playPauseButton.Content = "⏸";
                isPlaying = true;
            }
        }

        private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isDragging)
            {
                UpdateTimeDisplay();
            }
        }

        private void TimelineSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            isDragging = true;
        }

        private void TimelineSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            isDragging = false;
            mediaPlayer.Position = TimeSpan.FromSeconds(timelineSlider.Value);
            UpdateTimeDisplay();
        }

        private void TimerVideoTime_Tick(object sender, EventArgs e)
        {
            if (!isDragging && mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                timelineSlider.Value = mediaPlayer.Position.TotalSeconds;
                UpdateTimeDisplay();
            }
        }

        private void UpdateTimeDisplay()
        {
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                TimeSpan currentPosition = mediaPlayer.Position;
                TimeSpan totalDuration = mediaPlayer.NaturalDuration.TimeSpan;
                timeDisplay.Text = $"{FormatTimeSpan(currentPosition)} / {FormatTimeSpan(totalDuration)}";
            }
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            return timeSpan.Hours > 0 
                ? $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}" 
                : $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        public void Stop()
        {
            mediaPlayer.Stop();
            isPlaying = false;
            playPauseButton.Content = "▶";
            timerVideoTime.Stop();
        }

        public void Pause()
        {
            if (isPlaying)
            {
                mediaPlayer.Pause();
                isPlaying = false;
                playPauseButton.Content = "▶";
            }
        }

        public void Play()
        {
            if (!isPlaying)
            {
                mediaPlayer.Play();
                isPlaying = true;
                playPauseButton.Content = "⏸";
                timerVideoTime.Start();
            }
        }
    }

    public partial class MainWindow : Window
    {
        private readonly BackgroundWorker   m_preview_worker = new BackgroundWorker();
        private PreviewFile                 m_current_preview = new PreviewFile();
        private bool                        m_preview_pending = false;
        private AnimatedImageViewer         m_animated_image_viewer;
        private VideoPreviewControl         m_video_preview;

        private UIElement m_active_viewer;
        public UIElement ActiveViewer
        {
            get { return m_active_viewer;  }
            set
            {
                if (value == m_active_viewer)
                    return;
                m_active_viewer = value;
                m_active_viewer.Visibility = Visibility.Visible;
                bool exists = false;
                foreach (UIElement c in PreviewPane.Children)
                {
                    if (c != m_active_viewer)
                        c.Visibility = Visibility.Collapsed;
                    else
                        exists = true;
                }
                if (!exists)
                    PreviewPane.Children.Add (m_active_viewer);
            }
        }

        class PreviewFile
        {
            public IEnumerable<string> Path { get; set; }
            public string Name { get; set; }
            public Entry Entry { get; set; }

            public bool IsEqual (IEnumerable<string> path, Entry entry)
            {
                return Path != null && path.SequenceEqual (Path) && Entry == entry;
            }
        }

        private void InitPreviewPane()
        {
            m_preview_worker.DoWork += (s, e) => LoadPreviewImage(e.Argument as PreviewFile);
            m_preview_worker.RunWorkerCompleted += (s, e) => {
                if (m_preview_pending)
                    RefreshPreviewPane();
            };

            // Initialize the animated image viewer
            m_animated_image_viewer = new AnimatedImageViewer();
            m_animated_image_viewer.Stretch = ImageCanvas.Stretch;
            m_animated_image_viewer.StretchDirection = ImageCanvas.StretchDirection;

            m_video_preview = new VideoPreviewControl();

            // Add to PreviewPane instead
            PreviewPane.Children.Add(m_animated_image_viewer);
            PreviewPane.Children.Add(m_video_preview);

            ActiveViewer = ImageView;
            TextView.IsWordWrapEnabled = true;
        }

        private IEnumerable<Encoding> m_encoding_list = GetEncodingList();
        public IEnumerable<Encoding> TextEncodings { get { return m_encoding_list; } }

        internal static IEnumerable<Encoding> GetEncodingList (bool exclude_utf16 = false)
        {
            var list = new HashSet<Encoding>();
            try 
            {
                list.Add(Encoding.Default);
                var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                list.Add(Encoding.GetEncoding(oem));
            } 
            catch (Exception X) 
            {
                if (X is ArgumentException || X is NotSupportedException) 
                    list.Add(Encoding.GetEncoding(20127)); //default to US-ASCII
                else 
                    throw;
            }
            list.Add (Encoding.GetEncoding (932));
            list.Add (Encoding.GetEncoding (936));
            list.Add (Encoding.UTF8);
            if (!exclude_utf16)
            {
                list.Add (Encoding.Unicode);
                list.Add (Encoding.BigEndianUnicode);
            }
            return list;
        }

        private void OnEncodingSelect (object sender, SelectionChangedEventArgs e)
        {
            var enc = this.EncodingChoice.SelectedItem as Encoding;
            if (null == enc || null == CurrentTextInput)
                return;
            TextView.CurrentEncoding = enc;
        }

        /// <summary>
        /// Display entry in preview panel
        /// </summary>
        private void PreviewEntry (Entry entry)
        {
            if (m_current_preview.IsEqual (ViewModel.Path, entry))
                return;
            UpdatePreviewPane (entry);
        }

        void RefreshPreviewPane ()
        {
            m_preview_pending = false;
            var current = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null != current)
                UpdatePreviewPane (current.Source);
            else
                ResetPreviewPane();
        }

        void ResetPreviewPane ()
        {
            ActiveViewer = ImageView;
            ImageCanvas.Source = null;
            m_animated_image_viewer.Reset();
            m_animated_image_viewer.Visibility = Visibility.Collapsed;

            m_video_preview.Stop();
            m_video_preview.Visibility = Visibility.Collapsed;

            ImageCanvas.Visibility = Visibility.Visible;
            TextView.Clear();
            CurrentTextInput = null;
        }

        bool IsPreviewPossible (Entry entry)
        {
            return "image" == entry.Type || "script" == entry.Type || "video" == entry.Type
                || (string.IsNullOrEmpty (entry.Type) && entry.Size < 0x100000);
        }

        void UpdatePreviewPane (Entry entry)
        {
            SetStatusText ("");
            var vm = ViewModel;
            m_current_preview = new PreviewFile { Path = vm.Path, Name = entry.Name, Entry = entry };
            if (!IsPreviewPossible (entry))
            {
                ResetPreviewPane();
                return;
            }

            if ("video" == entry.Type)
            {
                LoadPreviewVideo(m_current_preview);
            }
            else if ("image" != entry.Type)
            {
                LoadPreviewText (m_current_preview);
            }
            else if (!m_preview_worker.IsBusy)
            {
                m_preview_worker.RunWorkerAsync (m_current_preview);
            }
            else
            {
                m_preview_pending = true;
            }
        }

        private Stream m_current_text;
        private Stream CurrentTextInput
        {
            get { return m_current_text; }
            set
            {
                if (value == m_current_text)
                    return;
                if (null != m_current_text)
                    m_current_text.Dispose();
                m_current_text = value;
            }
        }

        void LoadPreviewText (PreviewFile preview)
        {
            Stream file = null;
            try
            {
                file = VFS.OpenBinaryStream (preview.Entry).AsStream;
                if (!TextView.IsTextFile (file))
                {
                    ResetPreviewPane();
                    return;
                }
                var enc = EncodingChoice.SelectedItem as Encoding;
                if (null == enc)
                {
                    enc = TextView.GuessEncoding (file);
                    EncodingChoice.SelectedItem = enc;
                }
                TextView.DisplayStream (file, enc);
                ActiveViewer = TextView;
                CurrentTextInput = file;
                file = null;
            }
            catch (Exception X)
            {
                ResetPreviewPane();
                SetStatusText (X.Message);
            }
            finally
            {
                if (file != null)
                    file.Dispose();
            }
        }

        void LoadPreviewVideo(PreviewFile preview)
        {
            try
            {
                using (var videoData = VFS.OpenVideo(preview.Entry))
                {
                    ImageCanvas.Visibility = Visibility.Collapsed;
                    m_animated_image_viewer.Visibility = Visibility.Collapsed;
                    m_video_preview.Visibility = Visibility.Visible;

                    ActiveViewer = ImageView;
                    m_video_preview.LoadVideo(videoData);

                    string videoInfo = $"Video: {preview.Name}";
                    if (videoData.Width > 0 && videoData.Height > 0)
                    {
                        videoInfo += $" ({videoData.Width}x{videoData.Height})";
                    }
                    if (!string.IsNullOrEmpty(videoData.Codec))
                    {
                        videoInfo += $" - {videoData.Codec}";
                    }
                    SetStatusText(videoInfo);
                }
            }
            catch (Exception X)
            {
                ResetPreviewPane();
                SetStatusText (X.Message);
            }
        }

        void LoadPreviewImage (PreviewFile preview)
        {
            try
            {
                using (var data = VFS.OpenImage (preview.Entry))
                {
                    if (data.Image is AnimatedImageData animData && animData.IsAnimated)
                    {
                        SetPreviewAnimatedImage(preview, animData.Frames, animData.FrameDelays, data.SourceFormat);
                    }
                    else
                    {
                        SetPreviewImage(preview, data.Image.Bitmap, data.SourceFormat);
                    }
                }
            }
            catch (Exception X)
            {
                Dispatcher.Invoke (ResetPreviewPane);
                SetStatusText (X.Message);
            }
        }

        void SetPreviewImage (PreviewFile preview, BitmapSource bitmap, ImageFormat format)
        {
            if (bitmap.DpiX != Desktop.DpiX || bitmap.DpiY != Desktop.DpiY)
            {
                int stride = bitmap.PixelWidth * ((bitmap.Format.BitsPerPixel + 7) / 8); 
                var pixels = new byte[stride*bitmap.PixelHeight];
                bitmap.CopyPixels (pixels, stride, 0);
                var fixed_bitmap = BitmapSource.Create (bitmap.PixelWidth, bitmap.PixelHeight,
                    Desktop.DpiX, Desktop.DpiY, bitmap.Format, bitmap.Palette, pixels, stride);
                bitmap = fixed_bitmap;
            }
            if (!bitmap.IsFrozen)
                bitmap.Freeze();
            Dispatcher.Invoke (() =>
            {
                if (m_current_preview == preview) // compare by reference
                {
                    // Hide animated viewer and video player, show static image
                    m_animated_image_viewer.Reset();
                    m_animated_image_viewer.Visibility = Visibility.Collapsed;
                    m_video_preview.Stop();
                    m_video_preview.Visibility = Visibility.Collapsed;
                    ImageCanvas.Visibility = Visibility.Visible;

                    ActiveViewer = ImageView;
                    ImageCanvas.Source = bitmap;
                    ApplyDownScaleSetting();
                    SetStatusText (string.Format (guiStrings.MsgImageSize, bitmap.PixelWidth,
                                                  bitmap.PixelHeight, bitmap.Format.BitsPerPixel, format?.Tag ?? "?"));
                }
            });
        }

        void SetPreviewAnimatedImage(PreviewFile preview, List<BitmapSource> frames, List<int> delays, ImageFormat format)
        {
            // Process all frames to ensure correct DPI
            var processedFrames = new List<BitmapSource>();
            foreach (var frame in frames)
            {
                BitmapSource processedFrame = frame;
                if (frame.DpiX != Desktop.DpiX || frame.DpiY != Desktop.DpiY)
                {
                    int stride = frame.PixelWidth * ((frame.Format.BitsPerPixel + 7) / 8);
                    var pixels = new byte[stride * frame.PixelHeight];
                    frame.CopyPixels(pixels, stride, 0);
                    processedFrame = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight,
                        Desktop.DpiX, Desktop.DpiY, frame.Format, frame.Palette, pixels, stride);
                }
                if (!processedFrame.IsFrozen)
                    processedFrame.Freeze();
                processedFrames.Add(processedFrame);
            }

            Dispatcher.Invoke(() =>
            {
                if (m_current_preview == preview)
                {
                    ActiveViewer = m_animated_image_viewer;

                    ApplyScalingToAnimatedViewer();
                    m_animated_image_viewer.LoadAnimatedImage(processedFrames, delays);

                    SetStatusText(string.Format(guiStrings.MsgImageSize, processedFrames[0].PixelWidth,
                                              processedFrames[0].PixelHeight, processedFrames[0].Format.BitsPerPixel,
                                              format?.Tag ?? "?") + $" ({frames.Count} frames)");
                }
            });
        }

        private void ApplyScalingToAnimatedViewer()
        {
            bool image_need_scale = DownScaleImage.Get<bool>();
            if (image_need_scale && m_animated_image_viewer.Source != null)
            {
                var image = m_animated_image_viewer.Source;
                image_need_scale = image.Width > ImageView.ActualWidth || image.Height > ImageView.ActualHeight;
            }

            if (image_need_scale)
            {
                m_animated_image_viewer.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode(m_animated_image_viewer, BitmapScalingMode.HighQuality);
            }
            else
            {
                m_animated_image_viewer.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode(m_animated_image_viewer, BitmapScalingMode.NearestNeighbor);
            }
        }

        /// <summary>
        /// Fit window size to image.
        /// </summary>
        private void FitWindowExec (object sender, ExecutedRoutedEventArgs e)
        {
            ImageSource image = null;
            if (ImageCanvas.Visibility == Visibility.Visible)
                image = ImageCanvas.Source;
            else if (m_animated_image_viewer.Visibility == Visibility.Visible)
                image = m_animated_image_viewer.Source;

            if (null == image)
                return;

            var width = image.Width + Settings.Default.lvPanelWidth.Value + 1;
            var height = image.Height;
            width = Math.Max (ContentGrid.ActualWidth, width);
            height = Math.Max (ContentGrid.ActualHeight, height);
            if (width > ContentGrid.ActualWidth || height > ContentGrid.ActualHeight)
            {
                ContentGrid.Width = width;
                ContentGrid.Height = height;
                this.SizeToContent = SizeToContent.WidthAndHeight;
                Dispatcher.InvokeAsync (() => {
                    this.SizeToContent = SizeToContent.Manual;
                    ContentGrid.Width = double.NaN;
                    ContentGrid.Height = double.NaN;
                }, DispatcherPriority.ContextIdle);
            }
        }

        private void SetImageScaleMode (bool scale)
        {
            if (scale)
            {
                ImageCanvas.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode (ImageCanvas, BitmapScalingMode.HighQuality);
                ImageView.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ImageView.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

                m_animated_image_viewer.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.HighQuality);
            }
            else
            {
                ImageCanvas.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode (ImageCanvas, BitmapScalingMode.NearestNeighbor);
                ImageView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                ImageView.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;

                m_animated_image_viewer.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.NearestNeighbor);
            }
        }

        private void ApplyDownScaleSetting ()
        {
            bool image_need_scale = DownScaleImage.Get<bool>();

            if (image_need_scale)
            {
                if (ImageCanvas.Source != null)
                {
                    var image = ImageCanvas.Source;
                    image_need_scale = image.Width > ImageView.ActualWidth || image.Height > ImageView.ActualHeight;
                }
                else if (m_animated_image_viewer.Source != null)
                {
                    var image = m_animated_image_viewer.Source;
                    image_need_scale = image.Width > ImageView.ActualWidth || image.Height > ImageView.ActualHeight;
                }
            }

            SetImageScaleMode (image_need_scale);
        }

        private void PreviewSizeChanged (object sender, SizeChangedEventArgs e)
        {
            ImageSource image = null;
            if (ImageCanvas.Visibility == Visibility.Visible)
                image = ImageCanvas.Source;
            else if (m_animated_image_viewer.Visibility == Visibility.Visible)
                image = m_animated_image_viewer.Source;

            if (null == image || !DownScaleImage.Get<bool>())
                return;

            SetImageScaleMode (image.Width > e.NewSize.Width || image.Height > e.NewSize.Height);
        }

        private void OnEntrySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_animated_image_viewer != null)
                m_animated_image_viewer.StopAnimation();

            if (m_video_preview != null)
                m_video_preview.Stop();
        }
    }
}
