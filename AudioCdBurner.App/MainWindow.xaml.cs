using System.Printing;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Shapes;
using TagLib;
using NAudio.Wave;
using NAudio.MediaFoundation;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace AudioCdBurner.App
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Track> Tracks { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }
        

        private void AddFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Audio|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg|Alle filer|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (var path in dlg.FileNames)
                {
                    TagLib.File file = TagLib.File.Create(path);
                    Track track = new AudioCdBurner.App.Track {
                        Title = file.Tag.Title ?? System.IO.Path.GetFileNameWithoutExtension(path),
                        Artist = file.Tag.FirstPerformer ?? "Unknown",
                        Album = file.Tag.Album ?? "Unknown",
                        Path = path,
                        Duration = file.Properties.Duration,
                        BitrateKbps = file.Properties.AudioBitrate
                    };
                    Tracks.Add(track);
                }
            }
        }

        private async void TranscodeBtn_Click(object sender, RoutedEventArgs e)
        {
            TranscodeBtn.IsEnabled = false;

            var items = Tracks.ToList();

            try
            {
                await Task.Run(() =>
                {
                    foreach (Track track in Tracks)
                    {
                        try
                        {
                            using (var reader = CreateReader(track.Path))
                            {
                                var duration = reader.TotalTime;
                                var sr = reader.WaveFormat.SampleRate;
                                var ch = reader.WaveFormat.Channels;
                                var isMp3 = System.IO.Path.GetExtension(track.Path)
                                    .Equals(".mp3", StringComparison.OrdinalIgnoreCase);

                                Dispatcher.Invoke(() =>
                                {
                                    track.Duration = duration;
                                    track.SampleRateHz = sr; // <- UI-tråd
                                    track.Channels = ch; // <- UI-tråd
                                    track.IsMp3 = isMp3;
                                    track.AudioNote = $"sr={sr}, ch={ch} (UI set)";
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                track.IsMp3 = false;
                                track.AudioNote = "Kunne ikke åbne " + ex.Message;
                            });
                        }
                    }
                });
            }
            finally
            {
                TranscodeBtn.IsEnabled = true;
            }
        }

        private WaveStream CreateReader(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp3") return new Mp3FileReader(path);
            if (ext == ".wav") return new WaveFileReader(path);
            return new MediaFoundationReader(path);
        }

        private void RemoveBtn_Click(object sender, RoutedEventArgs e)
        {
            var toRemove = FilesList.SelectedItems.Cast<Track>().ToList();
            foreach (var t in toRemove) Tracks.Remove(t);
        }
    }
}
