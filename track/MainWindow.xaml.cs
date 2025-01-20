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
        private double originalHeight;

        public MainWindow()
        {
            InitializeComponent();

            originalHeight = this.Height;

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PlayBounceAnimation();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            var minimizeHeightAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase()
            };

            minimizeHeightAnimation.Completed += (s, e) =>
            {
                this.WindowState = WindowState.Minimized;
                this.Height = originalHeight;
            };

            this.BeginAnimation(Window.HeightProperty, minimizeHeightAnimation);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (this.WindowState == WindowState.Normal)
            {
                PlayBounceAnimation();
            }
        }

        private void PlayBounceAnimation()
        {
            this.Height = 0;
            var bounceAnimation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(300),
            };

            bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(originalHeight * 1.01, KeyTime.FromPercent(0.7), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(originalHeight, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));

            this.BeginAnimation(Window.HeightProperty, bounceAnimation);
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

                ReadID3Tags(audioData);
                CheckIfReady();
            }
        }

        private void DisplayCoverImage(byte[] imageData)
        {
            try
            {
                using (var ms = new MemoryStream(imageData))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = ms;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    CoverImage.Source = image;
                }
            }
            catch
            {
                CoverImage.Source = null;
            }
        }

        private void ReadID3Tags(byte[] audio)
        {
            try
            {
                if (audio.Length > 10 && Encoding.ASCII.GetString(audio, 0, 3) == "ID3")
                {
                    int id3v2Size = ReadSynchsafeInt(audio, 6);
                    int offset = 10;

                    while (offset < id3v2Size && offset + 10 <= audio.Length)
                    {
                        string frameID = Encoding.ASCII.GetString(audio, offset, 4);
                        int frameSize = ReadSynchsafeInt(audio, offset + 4);

                        if (frameSize <= 0 || offset + 10 + frameSize > audio.Length) break;

                        if (frameID == "TIT2")
                        {
                            TitleTextBox.Text = ReadTextFrame(audio, offset + 10, frameSize);
                        }
                        else if (frameID == "TPE1")
                        {
                            ArtistTextBox.Text = ReadTextFrame(audio, offset + 10, frameSize);
                        }
                        else if (frameID == "TALB")
                        {
                            AlbumTextBox.Text = ReadTextFrame(audio, offset + 10, frameSize);
                        }
                        else if (frameID == "APIC")
                        {
                            coverData = ReadAPICFrame(audio, offset + 10, frameSize);
                            if (coverData != null)
                            {
                                DisplayCoverImage(coverData);
                            }
                        }

                        offset += 10 + frameSize;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading ID3 tags: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string fileExtension = Path.GetExtension(files[0]).ToLower();
                    if (fileExtension == ".mp3" || fileExtension == ".flac" || fileExtension == ".wav" ||
                        fileExtension == ".jpg" || fileExtension == ".jpeg" || fileExtension == ".png")
                    {
                        e.Effects = DragDropEffects.Copy;
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    string fileExtension = Path.GetExtension(filePath).ToLower();

                    if (fileExtension == ".mp3" || fileExtension == ".flac" || fileExtension == ".wav")
                    {
                        HandleAudioFile(filePath);
                    }
                    else if (fileExtension == ".jpg" || fileExtension == ".jpeg" || fileExtension == ".png")
                    {
                        HandleImageFile(filePath);
                    }
                    else
                    {
                        MessageBox.Show("Unsupported file type.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void HandleAudioFile(string filePath)
        {
            try
            {
                selectedAudioPath = filePath;
                audioData = File.ReadAllBytes(filePath);

                AudioPreview.Source = new Uri(filePath);
                AudioPreview.Position = TimeSpan.Zero;

                if (Path.GetExtension(filePath).Equals(".flac", StringComparison.OrdinalIgnoreCase))
                {
                    LosslessTag.Visibility = Visibility.Visible;
                }
                else
                {
                    LosslessTag.Visibility = Visibility.Collapsed;
                }

                ReadID3Tags(audioData);
                CheckIfReady();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling audio file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleImageFile(string filePath)
        {
            try
            {
                selectedCoverPath = filePath;
                coverData = File.ReadAllBytes(filePath);

                CoverImage.Source = new BitmapImage(new Uri(filePath));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error handling image file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private string ReadTextFrame(byte[] audio, int offset, int frameSize)
        {
            try
            {
                if (frameSize < 1 || offset + frameSize > audio.Length) return string.Empty;

                byte encoding = audio[offset];
                int textOffset = offset + 1;

                if (encoding == 0 && textOffset < audio.Length)
                    return Encoding.GetEncoding("ISO-8859-1").GetString(audio, textOffset, frameSize - 1);
                else if (encoding == 1 && textOffset + 1 < audio.Length)
                    return Encoding.Unicode.GetString(audio, textOffset, frameSize - 1);

                return string.Empty;
            }
            catch
            {
                return string.Empty;
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
        private byte[]? ReadAPICFrame(byte[] audio, int offset, int frameSize)
        {
            try
            {
                int mimeTypeEnd = Array.IndexOf(audio, (byte)0, offset, frameSize);
                if (mimeTypeEnd < 0 || mimeTypeEnd >= offset + frameSize) return null;

                int descriptionEnd = Array.IndexOf(audio, (byte)0, mimeTypeEnd + 1, frameSize - (mimeTypeEnd + 1 - offset));
                if (descriptionEnd < 0 || descriptionEnd >= offset + frameSize) return null;

                int imageDataOffset = descriptionEnd + 1;
                int imageDataLength = frameSize - (imageDataOffset - offset);

                if (imageDataLength <= 0 || imageDataOffset + imageDataLength > audio.Length) return null;

                byte[] imageData = new byte[imageDataLength];
                Array.Copy(audio, imageDataOffset, imageData, 0, imageDataLength);

                return imageData;
            }
            catch
            {
                return null;
            }
        }
    }
}