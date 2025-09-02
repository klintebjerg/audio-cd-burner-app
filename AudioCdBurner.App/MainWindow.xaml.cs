using System.Printing;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Shapes;
using TagLib;
using NAudio.Wave;
using NAudio.MediaFoundation;

namespace AudioCdBurner.App
{
    public partial class MainWindow : Window
    {
        public MainWindow() => InitializeComponent();

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
                    FilesList.Items.Add(track);
                }
            }
        }

        private void TranscodeBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (Track track in FilesList.Items)
            {
               try
               {
                   using (var reader = new NAudio.Wave.Mp3FileReader(track.Path))
                   {
                       track.IsMp3 = true;
                       track.Duration = reader.TotalTime;
                       track.SampleRateHz = reader.WaveFormat.SampleRate;
                       track.Channels = reader.WaveFormat.Channels;
                   }
               }
               catch
               {
                   track.IsMp3 = false;
                   track.AudioNote = "Not an MP3 file";
               } 
               
               
            }
        }
    }
}
