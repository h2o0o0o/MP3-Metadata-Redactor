using System;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Configuration;

namespace Mp3MetaEditor
{
    public partial class MainWindow : Window
    {
        private byte[]? audioData;
        private byte[]? coverData;
        private string selectedAudioPath = string.Empty;
        private string selectedCoverPath = string.Empty;
        private Rect originalBounds;
        private double originalWidth;
        private double originalHeight;

        public MainWindow()
        {
            InitializeComponent();

            originalWidth = this.Width;
            originalHeight = this.Height;
            originalBounds = new Rect(this.Left, this.Top, this.Width, this.Height);
        }

        private void AudioFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "MP3 Files (*.mp3)|*.mp3|All Files (*.*)|*.*",
                Title = "Select Audio File"
            };

            if (ofd.ShowDialog() == true)
            {
                selectedAudioPath = ofd.FileName;
                audioData = File.ReadAllBytes(selectedAudioPath);
                AudioPreview.Source = new Uri(selectedAudioPath);
                AudioPreview.Position = TimeSpan.Zero;

                if (Path.GetExtension(selectedAudioPath).Equals(".flac", StringComparison.OrdinalIgnoreCase))
                {
                    LosslessTag.Visibility = Visibility.Visible;
                }
                else
                {
                    LosslessTag.Visibility = Visibility.Collapsed;
                }

                CheckIfReady();
            }
        }

        private void CoverFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All Files (*.*)|*.*",
                Title = "Select Cover Image"
            };

            if (ofd.ShowDialog() == true)
            {
                selectedCoverPath = ofd.FileName;
                coverData = File.ReadAllBytes(selectedCoverPath);
                CoverImage.Source = new BitmapImage(new Uri(selectedCoverPath));
            }
        }

        private void Input_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            var minimizeWidthAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase()
            };

            var minimizeHeightAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase()
            };

            minimizeHeightAnimation.Completed += (s, e) =>
            {
                this.WindowState = WindowState.Minimized;
                this.Width = originalWidth;
                this.Height = originalHeight;
            };
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Maximized)
            {
                originalBounds = new Rect(RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height);
            }
            else if (this.WindowState == WindowState.Normal)
            {
                var restoreWidthAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = originalBounds.Width,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase()
                };

                var restoreHeightAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = originalBounds.Height,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase()
                };

                this.BeginAnimation(Window.WidthProperty, restoreWidthAnimation);
                this.BeginAnimation(Window.HeightProperty, restoreHeightAnimation);
            }
        }

        private void UpdatePreview()
        {
            PreviewTitle.Text = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? "Track Title" : TitleTextBox.Text;
            string artist = string.IsNullOrWhiteSpace(ArtistTextBox.Text) ? "Artist" : ArtistTextBox.Text;
            string album = string.IsNullOrWhiteSpace(AlbumTextBox.Text) ? "Album" : AlbumTextBox.Text;
            PreviewArtist.Text = $"{artist} — {album}";
        }

        private void CheckIfReady()
        {
            WriteMetaButton.IsEnabled = audioData != null;
            PlayButton.IsEnabled = audioData != null;
        }

        private void WriteMetaButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioData == null)
            {
                MessageBox.Show("Please select an audio file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                byte[] updatedAudio = WriteID3Tags(audioData, TitleTextBox.Text, ArtistTextBox.Text, AlbumTextBox.Text, coverData);
                SaveFileDialog sfd = new SaveFileDialog
                {
                    Filter = "MP3 Files (*.mp3)|*.mp3",
                    Title = "Save Updated File",
                    FileName = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? "updated_" + Path.GetFileName(selectedAudioPath) : TitleTextBox.Text + ".mp3"
                };

                if (sfd.ShowDialog() == true)
                {
                    File.WriteAllBytes(sfd.FileName, updatedAudio);
                    MessageBox.Show("Done", "Peter Alert!", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error writing metadata: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private byte[] WriteID3Tags(byte[] audio, string title, string artist, string album, byte[]? cover)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                int id3v2Size = 0;
                if (audio.Length > 10 && Encoding.ASCII.GetString(audio, 0, 3) == "ID3")
                {
                    id3v2Size = ReadSynchsafeInt(audio, 6);
                }

                using (MemoryStream tagStream = new MemoryStream())
                {
                    tagStream.Write(Encoding.ASCII.GetBytes("ID3"), 0, 3);
                    tagStream.WriteByte(3);
                    tagStream.WriteByte(0);
                    tagStream.WriteByte(0);
                    tagStream.Write(new byte[4], 0, 4);

                    if (!string.IsNullOrWhiteSpace(title))
                        WriteFrame(tagStream, "TIT2", title);
                    if (!string.IsNullOrWhiteSpace(artist))
                        WriteFrame(tagStream, "TPE1", artist);
                    if (!string.IsNullOrWhiteSpace(album))
                        WriteFrame(tagStream, "TALB", album);
                    if (cover != null && cover.Length > 0)
                        WriteAPICFrame(tagStream, cover);

                    long tagSize = tagStream.Length - 10;
                    byte[] sizeBytes = EncodeSynchsafeInt((int)tagSize);
                    byte[] tagBytes = tagStream.ToArray();
                    tagBytes[6] = sizeBytes[0];
                    tagBytes[7] = sizeBytes[1];
                    tagBytes[8] = sizeBytes[2];
                    tagBytes[9] = sizeBytes[3];
                    ms.Write(tagBytes, 0, tagBytes.Length);
                }

                ms.Write(audio, id3v2Size + 10, audio.Length - id3v2Size - 10);
                return ms.ToArray();
            }
        }

        private void WriteFrame(MemoryStream ms, string frameID, string content)
        {
            byte[] contentBytes = Encoding.Unicode.GetBytes(content);
            ms.Write(Encoding.ASCII.GetBytes(frameID), 0, 4);
            byte[] size = BitConverter.GetBytes(contentBytes.Length + 1);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(size);
            ms.Write(size, 0, 4);
            ms.Write(new byte[2], 0, 2);
            ms.WriteByte(1);
            ms.Write(contentBytes, 0, contentBytes.Length);
        }

        private void WriteAPICFrame(MemoryStream ms, byte[] imageData)
        {
            using (MemoryStream frame = new MemoryStream())
            {
                frame.Write(Encoding.ASCII.GetBytes("APIC"), 0, 4);
                frame.Write(new byte[4], 0, 4);
                frame.Write(new byte[2], 0, 2);
                frame.WriteByte(0);
                frame.Write(Encoding.ASCII.GetBytes("image/jpeg"), 0, "image/jpeg".Length);
                frame.WriteByte(0);
                frame.WriteByte(3);
                frame.WriteByte(0);
                frame.Write(imageData, 0, imageData.Length);

                byte[] frameBytes = frame.ToArray();
                int frameSize = frameBytes.Length - 10;
                byte[] sizeBytes = BitConverter.GetBytes(frameSize);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(sizeBytes);
                frameBytes[4] = sizeBytes[0];
                frameBytes[5] = sizeBytes[1];
                frameBytes[6] = sizeBytes[2];
                frameBytes[7] = sizeBytes[3];

                ms.Write(frameBytes, 0, frameBytes.Length);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        private int ReadSynchsafeInt(byte[] bytes, int start)
        {
            return (bytes[start] & 0x7F) << 21 |
                   (bytes[start + 1] & 0x7F) << 14 |
                   (bytes[start + 2] & 0x7F) << 7 |
                   (bytes[start + 3] & 0x7F);
        }

        private byte[] EncodeSynchsafeInt(int size)
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)((size >> 21) & 0x7F);
            bytes[1] = (byte)((size >> 14) & 0x7F);
            bytes[2] = (byte)((size >> 7) & 0x7F);
            bytes[3] = (byte)(size & 0x7F);
            return bytes;
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (AudioPreview.Source != null)
            {
                AudioPreview.Play();
                PlayButton.IsEnabled = false;
                PauseButton.IsEnabled = true;
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (AudioPreview.Source != null)
            {
                AudioPreview.Pause();
                PlayButton.IsEnabled = true;
                PauseButton.IsEnabled = false;
            }
        }
    }
}