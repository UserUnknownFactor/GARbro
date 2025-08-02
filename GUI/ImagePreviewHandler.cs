using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Windows.Threading;

using GameRes;
using GARbro.GUI.Strings;

namespace GARbro.GUI.Preview
{
    public class ImagePreviewHandler : PreviewHandlerBase
    {
        private readonly          MainWindow _mainWindow;
        private readonly               Image _imageCanvas;          // This is the static image viewer
        private readonly ImagePreviewControl _animatedImageViewer;  // This is for animated images
        
        public override bool IsActive => _imageCanvas.Visibility == Visibility.Visible || 
                                         _animatedImageViewer.Visibility == Visibility.Visible;

        public ImagePreviewHandler(MainWindow mainWindow, Image imageCanvas, ImagePreviewControl animatedImageViewer)
        {
            _mainWindow = mainWindow;
            _imageCanvas = imageCanvas;
            _animatedImageViewer = animatedImageViewer;
        }

        public override void LoadContent(PreviewFile preview)
        {
            try
            {

                using (var data = VFS.OpenImage(preview.Entry))
                {
                    if (data.Image is AnimatedImageData animData && animData.IsAnimated)
                        SetAnimatedImage(preview, data);
                    else
                        SetStaticImage(preview, data);
                }
            }
            catch (Exception X)
            {
                _mainWindow.Dispatcher.Invoke(() => Reset());
                _mainWindow.SetFileStatus(X.Message);
                //_mainWindow.SetFileStatus(Localization._T(X.Message));
            }
        }

        private void SetStaticImage(PreviewFile preview, IImageDecoder image)
        {
            var bitmap = PrepareBitmap(image.Image.Bitmap);
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.ShowImagePreview();
                _imageCanvas.Source = bitmap;
                _mainWindow.ApplyDownScaleSetting();

                _mainWindow.SetPreviewStatus((image.SourceFormat?.Tag ?? "?") + 
                    ((IImageComment)image.Info)?.GetComment() ?? "");
            });
        }

        private void SetAnimatedImage(PreviewFile preview, IImageDecoder image)
        {
            var animation = image.Image as AnimatedImageData;
            var frames = animation.Frames;
            var delays = animation.FrameDelays;
            var processedFrames = new List<BitmapSource>();
            foreach (var frame in frames)
                processedFrames.Add(PrepareBitmap(frame));

            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.ShowAnimatedImagePreview(_animatedImageViewer);
                _mainWindow.ApplyScalingToAnimatedViewer();
                _animatedImageViewer.LoadAnimatedImage(processedFrames, delays);

                _mainWindow.SetPreviewStatus((image.SourceFormat?.Tag ?? "?") + 
                    ((IImageComment)image.Info)?.GetComment() ?? "");
            });
        }

        private BitmapSource PrepareBitmap(BitmapSource bitmap)
        {
            if (bitmap.DpiX != Desktop.DpiX || bitmap.DpiY != Desktop.DpiY)
            {
                int stride = bitmap.PixelWidth * ((bitmap.Format.BitsPerPixel + 7) / 8);
                var pixels = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels(pixels, stride, 0);
                bitmap = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight,
                    Desktop.DpiX, Desktop.DpiY, bitmap.Format, bitmap.Palette, pixels, stride);
            }
            if (!bitmap.IsFrozen)
                bitmap.Freeze();
            return bitmap;
        }

        public override void Reset()
        {
            _imageCanvas.Source = null;
            _animatedImageViewer.Reset();
            _animatedImageViewer.Visibility = Visibility.Collapsed;
            _imageCanvas.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Custom control for displaying animated images with frame support
    /// </summary>
    public class ImagePreviewControl : Image
    {
        private List<BitmapSource> frames = new List<BitmapSource>();
        private List<int> frameDelays = new List<int>();
        private int currentFrameIndex = 0;
        private DispatcherTimer animationTimer;
        private bool isAnimated = false;

        public ImagePreviewControl()
        {
            animationTimer = new DispatcherTimer();
            animationTimer.Tick += OnFrameChange;
            Unloaded += (s, e) => StopAnimation();
        }

        /// <summary>
        /// Load a static image
        /// </summary>
        public void LoadImage(BitmapSource image)
        {
            StopAnimation();
            frames.Clear();
            frameDelays.Clear();
            isAnimated = false;
            Source = image;
        }

        /// <summary>
        /// Load an animated image with multiple frames
        /// </summary>
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

        /// <summary>
        /// Start animation playback
        /// </summary>
        public void StartAnimation()
        {
            if (isAnimated && frames.Count > 1)
            {
                animationTimer.Interval = TimeSpan.FromMilliseconds(frameDelays[currentFrameIndex]);
                animationTimer.Start();
            }
        }

        /// <summary>
        /// Stop animation playback
        /// </summary>
        public void StopAnimation()
        {
            if (animationTimer != null && isAnimated && animationTimer.IsEnabled)
                animationTimer.Stop();
        }

        /// <summary>
        /// Reset the control to initial state
        /// </summary>
        public void Reset()
        {
            StopAnimation();
            frames.Clear();
            frameDelays.Clear();
            isAnimated = false;
            Source = null;
        }
    }
}