// File: MainWindow.xaml.cs

using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfScreenHelper;

namespace RandomSlideshow
{
    public partial class MainWindow : Window
    {
        private string selectedFolder;
        private ObservableCollection<string> imageFiles = new ObservableCollection<string>();

        private bool isImageEnlarged = false; // Tracks the state of the image
        private Thickness originalMargin; // Stores the original margin of the image

        private DispatcherTimer slideshowTimer;
        private Random random;
        private Screen[] monitors;  // Replace with WpfScreenHelper's Screen class
        private FullscreenSlideshowWindow fullscreenWindow;
        private BitmapImage nextImageBuffer; // Buffer to hold the preloaded image
        private bool isImageLoading = false; // Prevents double-loading
        private string currentImageFilePath; // Store the currently displayed image file path
       
        // Variables for minimum width and height
        private int minWidth = 500;
        private int minHeight = 500;

        private string orientationFilter = "All"; // Tracks the selected orientation filter

        private bool isSlideshowRunning = false; // Tracks whether the slideshow is running

        // Import SetThreadExecutionState from kernel32.dll
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        [Flags]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }


        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        public MainWindow()
        {
            InitializeComponent();
            random = new Random();
            slideshowTimer = new DispatcherTimer();
            slideshowTimer.Interval = TimeSpan.FromSeconds(3);
            slideshowTimer.Tick += SlideshowTimer_Tick;

            LoadMonitors(); this.Closing += MainWindow_Closing;  // Attach the closing event handler
        }

        // Method to prevent the system from going to sleep or turning off the display
        private void PreventSleep()
        {
            // Use SetThreadExecutionState to prevent sleep and keep the display on
            SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED);
        }

        // Method to allow sleep after the slideshow ends or the window is closed
        private void AllowSleep()
        {
            // Reset the execution state so the system can sleep again
            SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
        }

        // Event handler when radio button selection changes
        private void OnRadioButtonChecked(object sender, RoutedEventArgs e)
        {
            if (LandscapeRadioButton != null && LandscapeRadioButton.IsChecked == true)
            {
                orientationFilter = "Landscape";
            }
            else if (VerticalRadioButton != null && VerticalRadioButton.IsChecked == true)
            {
                orientationFilter = "Vertical";
            }
            else
            {
                orientationFilter = "All";
            }
        }

        // Event handler to close the fullscreen window when MainWindow is closing
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            AllowSleep();
            if (fullscreenWindow != null)
            {
                fullscreenWindow.Close();  // Close the fullscreen window if it's open
            }

            Application.Current.Shutdown();  // Ensure the entire application exits
        }

        // Load monitor information and populate ComboBox
        private void LoadMonitors()
        {
            monitors = Screen.AllScreens.ToArray();  // Get all the monitors using WpfScreenHelper
            MonitorComboBox.ItemsSource = monitors.Select(m => m.DeviceName).ToList();
        }


        // Event handler for when the timer ticks (i.e., time to change the image)
        private async void SlideshowTimer_Tick(object sender, EventArgs e)
        {
            if (nextImageBuffer != null)
            {
                // Display the preloaded image
                SlideshowImage.Source = nextImageBuffer;

                if (fullscreenWindow == null)
                    StartFullscreenSlideshow(nextImageBuffer);
                else
                {
                    fullscreenWindow.scaleToFill = ScaleImageCheckBox.IsChecked == true;
                    fullscreenWindow.DisplayImage(nextImageBuffer);
                };
                UpdateFilePathDisplay(currentImageFilePath);
                // Start preloading the next image
                PreloadNextImage();
            }

            //await DisplayRandomImageAsync();
        }
        // Preload the next image asynchronously
        private async Task PreloadNextImage()
        {
            if (isImageLoading) return; // Avoid multiple simultaneous image loading tasks
            isImageLoading = true;

            BitmapImage nextImage = null;

            try
            {
                // Keep searching until a valid image is found that matches the filter
                nextImage = await Task.Run(() =>
                {
                    string randomImageFile = null;
                    BitmapImage image = null;
                    while (image == null)
                    {
                        randomImageFile = GetRandomImageFile();
                        if (!string.IsNullOrEmpty(randomImageFile))
                        {
                            var bitmap = new BitmapImage();

                            try
                            {
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(randomImageFile, UriKind.Absolute);
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; // Ignore color profile issues
                                bitmap.EndInit();
                                bitmap.Freeze(); // Make the image freezable for better thread safety
                            }
                            catch (NotSupportedException)
                            {
                                Debug.WriteLine($"The image format is not supported: {randomImageFile}");
                                continue;
                                //return null; // Skip this image and try the next one
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error loading image: {ex.Message}");
                                continue;
                                //return null; // Handle other exceptions gracefully
                            }
                            try
                            {
                                var adjustedBitmap = ApplyRotationIfNeeded(bitmap); // Apply EXIF rotation
                                                                                    // Only return the image if it passes the orientation filter
                                if (ShouldDisplayImage(adjustedBitmap))
                                {
                                    image = adjustedBitmap;
                                }
                                else
                                {
                                    image = null;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error loading image: {ex.Message}");
                                continue;
                                //return null; // Handle other exceptions gracefully
                            }
                        }
                    }
                    Debug.WriteLine(randomImageFile);
                    currentImageFilePath = randomImageFile;
                    return image;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading image: {ex.Message}");
            }

            // Set the buffered image
            nextImageBuffer = nextImage;

            isImageLoading = false;
        }

        // Start the fullscreen slideshow on the selected monitor
        private void StartFullscreenSlideshow(BitmapImage image)
        {
            var selectedMonitorIndex = MonitorComboBox.SelectedIndex;
            if (selectedMonitorIndex < 0 || selectedMonitorIndex >= monitors.Length)
            {
                //MessageBox.Show("Please select a valid monitor.");
                return;
            }

            var selectedMonitor = monitors[selectedMonitorIndex];

            // Create the FullscreenSlideshowWindow, passing the checkbox state
            bool scaleImageToFill = ScaleImageCheckBox.IsChecked == true;
            fullscreenWindow = new FullscreenSlideshowWindow(image, scaleImageToFill);

            // Move and size the window based on the monitor's working area
            fullscreenWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            fullscreenWindow.Left = selectedMonitor.WpfWorkingArea.Left;
            fullscreenWindow.Top = selectedMonitor.WpfWorkingArea.Top;
            fullscreenWindow.Width = selectedMonitor.WpfWorkingArea.Width;
            fullscreenWindow.Height = selectedMonitor.WpfWorkingArea.Height;

            // Show the fullscreen window
            fullscreenWindow.Show();
            fullscreenWindow.WindowState = WindowState.Maximized;
        }

        // Method to check and apply rotation if the image has EXIF metadata
        private BitmapImage ApplyRotationIfNeeded(BitmapImage image)
        {
            try
            {
                // Create a BitmapFrame to read metadata
                var bitmapFrame = BitmapFrame.Create(image.UriSource, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
                BitmapMetadata metadata = (BitmapMetadata)bitmapFrame.Metadata;

                if (metadata != null && metadata.ContainsQuery("System.Photo.Orientation"))
                {
                    // Read the orientation metadata
                    if (metadata.GetQuery("System.Photo.Orientation") is ushort orientation)
                    {
                        return ApplyRotation(bitmapFrame, orientation);
                    }
                }

                // No rotation needed
                return ConvertBitmapFrameToBitmapImage(bitmapFrame);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying rotation: {ex.Message}");
                return image; // Return the original image if there is an error
            }
        }

        // Method to apply rotation based on EXIF orientation
        private BitmapImage ApplyRotation(BitmapFrame bitmapFrame, ushort orientation)
        {
            int rotationAngle = orientation switch
            {
                6 => 90,  // Rotated 90 degrees
                3 => 180, // Rotated 180 degrees
                8 => 270, // Rotated 270 degrees
                _ => 0    // Default orientation, no rotation
            };

            // Rotate the image if needed
            if (rotationAngle == 0)
            {
                return ConvertBitmapFrameToBitmapImage(bitmapFrame); // No rotation required
            }

            // Rotate the bitmap frame
            var transformedBitmap = new TransformedBitmap(bitmapFrame, new RotateTransform(rotationAngle));

            // Convert the transformed bitmap back to a BitmapImage
            return ConvertTransformedBitmapToBitmapImage(transformedBitmap);
        }

        // Convert a BitmapFrame to BitmapImage
        private BitmapImage ConvertBitmapFrameToBitmapImage(BitmapFrame bitmapFrame)
        {
            BitmapImage bitmapImage = new BitmapImage();
            using (MemoryStream memoryStream = new MemoryStream())
            {
                BitmapEncoder encoder = new PngBitmapEncoder(); // Use a suitable encoder
                encoder.Frames.Add(bitmapFrame);
                encoder.Save(memoryStream);

                memoryStream.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Freeze for performance
            }
            return bitmapImage;
        }

        // Convert a TransformedBitmap back to BitmapImage
        private BitmapImage ConvertTransformedBitmapToBitmapImage(TransformedBitmap transformedBitmap)
        {
            BitmapImage bitmapImage = new BitmapImage();
            using (MemoryStream memoryStream = new MemoryStream())
            {
                BitmapEncoder encoder = new PngBitmapEncoder(); // Use a suitable encoder
                encoder.Frames.Add(BitmapFrame.Create(transformedBitmap));
                encoder.Save(memoryStream);

                memoryStream.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Freeze for performance
            }
            return bitmapImage;
        }
        // Function to check if the image matches the selected filter
        private bool ShouldDisplayImage(BitmapImage bitmap)
        {
            if (bitmap.PixelWidth < minWidth || bitmap.PixelHeight < minHeight)
                return false;

            if (orientationFilter == "All") return true;

            var isLandscape = bitmap.PixelWidth > bitmap.PixelHeight;
            var isPortrait = bitmap.PixelHeight > bitmap.PixelWidth;

            return (orientationFilter == "Landscape" && isLandscape) ||
                   (orientationFilter == "Vertical" && isPortrait);
        }
        // Updated method to get a random image from the preloaded list
        private string GetRandomImageFile()
        {
            if (imageFiles.Count == 0) return null;
            int randomIndex = new Random().Next(imageFiles.Count);
            return imageFiles[randomIndex];
        }


        // Method to browse for the folder
        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Use OpenFileDialog to simulate folder selection
            var dialog = new OpenFileDialog
            {
                Filter = "Folder Selection|*.none",
                CheckFileExists = false,
                FileName = "Select Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                selectedFolder = Path.GetDirectoryName(dialog.FileName);
                //StartSlideshow();
                RefreshImageList();
                //PreloadNextImage();
            }
        }

        // Event handler for the start/stop slideshow button
        private void StartStopSlideshowButton_Click(object sender, RoutedEventArgs e)
        {
            if (isSlideshowRunning)
            {
                StopSlideshow();
            }
            else
            {
                StartSlideshow();
            }
        }

        // Start the slideshow
        private void StartSlideshow()
        {           

            if (string.IsNullOrEmpty(selectedFolder) || !Directory.Exists(selectedFolder))
            {
                MessageBox.Show("Please select a valid folder.");
                return;
            }

            isSlideshowRunning = true;
            StartShowButton.Content = "Stop Slideshow"; // Update button text
            PreventSleep(); // Prevent the system from sleeping
            //await PreloadNextImage();

            // Call the tick handler manually to trigger the first tick immediately
            SlideshowTimer_Tick(null, EventArgs.Empty);
            slideshowTimer.Start(); // Start the slideshow timer
        }

        // Stop the slideshow
        private void StopSlideshow()
        {
            isSlideshowRunning = false;
            StartShowButton.Content = "Start Slideshow"; // Update button text
            AllowSleep(); // Allow the system to sleep again
            slideshowTimer.Stop(); // Stop the slideshow timer

            // Optionally, hide or reset the fullscreen window when the slideshow stops.
            if (fullscreenWindow != null)
            {
                fullscreenWindow.Close();
                fullscreenWindow = null;
            }
        }

        // Event handler for when the delay textbox is updated
        private void DelayTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (slideshowTimer == null)
                return;
            if (double.TryParse(DelayTextBox.Text, out double delayInSeconds) && delayInSeconds > 0)
            {
                slideshowTimer.Interval = TimeSpan.FromSeconds(delayInSeconds); // Update the interval dynamically
            }
            else
            {
                // Show an error or reset to a default value if the input is invalid
                MessageBox.Show("Please enter a valid number greater than 0.");
                DelayTextBox.Text = "3"; // Reset to default value if input is invalid
            }
        }

        private IEnumerable<string> SafeEnumerateFiles(string path, string[] extensions)
        {
            var files = new List<string>();

            try
            {
                // Add files with the desired extensions
                files.AddRange(Directory.EnumerateFiles(path)
                                         .Where(file => extensions.Contains(Path.GetExtension(file).ToLower())));

                // Recursively get files from subdirectories
                foreach (var directory in Directory.EnumerateDirectories(path))
                {
                    files.AddRange(SafeEnumerateFiles(directory, extensions));  // Recursively add files from subdirectories
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories or files that cannot be accessed
            }

            return files;
        }

        private async void RefreshImageList()
        {
            // Disable the refresh button to prevent multiple clicks during the refresh process.
            RefreshButton.IsEnabled = false;
            StartShowButton.IsEnabled = false;
            FileEnumerationProgressBar.Value = 0;
            ProgressLabel.Content = "Starting enumeration...";

            if (string.IsNullOrEmpty(selectedFolder) || !Directory.Exists(selectedFolder))
            {
                MessageBox.Show("Please select a valid folder.");
                RefreshButton.IsEnabled = true;
                return;
            }

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".jxr", ".hdp", ".wdp", ".jif", ".jifi", ".jpe", ".jfi" };
            var progress = new Progress<int>(value =>
            {
                FileEnumerationProgressBar.Value = value;
                ProgressLabel.Content = $"Enumerated {value}%";
            });

            try
            {
                imageFiles.Clear();
                await Task.Run(() => EnumerateFilesWithProgress(selectedFolder, extensions, progress));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while refreshing the image list: {ex.Message}");
            }
            finally
            {
                await PreloadNextImage();
                RefreshButton.IsEnabled = true;
                StartShowButton.IsEnabled = true;
                ProgressLabel.Content = "Enumeration complete.";
                FileEnumerationProgressBar.Value = 100;

                if (imageFiles.Count == 0)
                {
                    MessageBox.Show("No images found in the selected folder.");
                }
            }
        }

        // Method to enumerate files and report progress
        private void EnumerateFilesWithProgress(string path, string[] extensions, IProgress<int> progress)
        {
            var files = SafeEnumerateFiles(path, extensions).ToList();
            int totalFiles = files.Count;
            int processedFiles = 0;

            foreach (var file in files)
            {
                imageFiles.Add(file);
                processedFiles++;

                // Calculate progress percentage
                int percentage = (int)((double)processedFiles / totalFiles * 100);
                progress.Report(percentage);
            }
        }


        // Event handler for the Refresh button to reload the image list
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshImageList();
        }

        // Method to update the displayed file path
        private void UpdateFilePathDisplay(string filePath)
        {
            currentImageFilePath = filePath;
            FilePathHyperlink.Inlines.Clear();
            FilePathHyperlink.Inlines.Add(new Run(filePath));
        }

        // Event handler for the Hyperlink click
        private void FilePathHyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(currentImageFilePath))
            {
                try
                {
                    // Extract the folder path
                    string folderPath = Path.GetDirectoryName(FilePathTextBlock.Text);

                    if (folderPath != null)
                    {
                        // Open File Explorer to the folder containing the file
                        Process.Start("explorer.exe", $"/select,\"{FilePathTextBlock.Text}\"");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to open the file location: {ex.Message}");
                }
            }
        }

        // Event handler for width and height text box input validation
        private void MinSizeTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if ((MinWidthTextBox == null)||(MinHeightTextBox == null))
            {
                return;
            }
            int temp = 0;
            // Validate minimum width
            if (!int.TryParse(MinWidthTextBox.Text, out temp) || minWidth < 0)
            {
                MessageBox.Show("Please enter a valid positive number for minimum width.");
                MinWidthTextBox.Text = minWidth.ToString();
            }
            minWidth = temp;

            // Validate minimum height
            if (!int.TryParse(MinHeightTextBox.Text, out temp) || minHeight < 0)
            {
                MessageBox.Show("Please enter a valid positive number for minimum height.");
                MinHeightTextBox.Text = minHeight.ToString();
            }
            minHeight = temp;
        }
        private void AlwaysOnTopCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            this.Topmost = true;
        }

        private void AlwaysOnTopCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            this.Topmost = false;
        }

        private void SlideshowImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!isImageEnlarged)
            {
                // Save the original properties if this is the first click
                originalMargin = SlideshowImage.Margin;

                // Enlarge the image to cover the entire window
                SlideshowImage.Margin = new Thickness(0);

                isImageEnlarged = true; // Update the state
            }
            else
            {
                // Restore the original properties
                SlideshowImage.Margin = originalMargin;

                isImageEnlarged = false; // Update the state
            }
        }

        private void ScaleImageCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Set Stretch to UniformToFill when checked
            SlideshowImage.Stretch = Stretch.UniformToFill;
        }

        private void ScaleImageCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Set Stretch to Uniform when unchecked
            SlideshowImage.Stretch = Stretch.Uniform;
        }


    }

}
