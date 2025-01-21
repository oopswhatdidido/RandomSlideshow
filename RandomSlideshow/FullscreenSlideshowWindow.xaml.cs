// File: FullscreenSlideshowWindow.xaml.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfScreenHelper;

namespace RandomSlideshow
{
    public partial class FullscreenSlideshowWindow : Window
    {
        public bool scaleToFill;

        public FullscreenSlideshowWindow(BitmapImage image, bool scaleToFill)
        {
            InitializeComponent();
            this.scaleToFill = scaleToFill;

            // Load the image as a BitmapFrame to read metadata
            //var bitmapFrame = BitmapFrame.Create(image.UriSource, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            //var rotatedImage = ApplyRotation(bitmapFrame);  // Apply rotation based on EXIF metadata
            DisplayImage(image);
        }       

        // Display the image in the fullscreen window
        public void DisplayImage(BitmapImage image)
        {
            if (image != null)
            {
                FullscreenImage.Source = image;

                if (scaleToFill)
                {
                    FullscreenImage.Stretch = Stretch.UniformToFill;  // Stretch to fill the screen
                    FullscreenImage.HorizontalAlignment = HorizontalAlignment.Center;
                    FullscreenImage.VerticalAlignment = VerticalAlignment.Center;
                }
                else
                {
                    FullscreenImage.Stretch = Stretch.Uniform;  // Show at original size
                    FullscreenImage.HorizontalAlignment = HorizontalAlignment.Center;
                    FullscreenImage.VerticalAlignment = VerticalAlignment.Center;
                }
            }
        }
    }
}
