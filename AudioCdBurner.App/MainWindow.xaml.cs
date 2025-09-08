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
using IMAPI2.Interop;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AudioCdBurner.App
{
    public partial class MainWindow : Window
    {
        private MsftDiscFormat2TrackAtOnce _tao;
        private IDiscRecorder2 _recorder;
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
                                
                                string outPath = System.IO.Path.Combine(GetWorkFolder(), System.IO.Path.GetFileNameWithoutExtension(track.Path) + ".wav");
                                
                                WriteAsCdWav(reader, outPath);

                                Dispatcher.Invoke(() =>
                                {
                                    track.OutputWavPath = outPath;
                                    track.AudioNote = "WAV Saved";
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
        
        private void RemoveBtn_Click(object sender, RoutedEventArgs e)
        {
            var toRemove = FilesList.SelectedItems.Cast<Track>().ToList();
            foreach (var t in toRemove) Tracks.Remove(t);
        }
        
        private void DetectBtn_Click(object sender, RoutedEventArgs e)
        {
            var master = new MsftDiscMaster2();
            if (!master.IsSupportedEnvironment)
            {
                MessageBox.Show("CD-brænder ikke fundet");
                return;
            }
            
            var recorders = new List<IDiscRecorder2>();
            foreach (string id in master)
            {
                var rec = new MsftDiscRecorder2();
                rec.InitializeDiscRecorder(id);
                recorders.Add(rec);
            }
            MessageBox.Show($"Found drives: {recorders.Count}");
            
            if (recorders.Count > 0)
            {
                _recorder = recorders[0];
                MessageBox.Show($"Using drive: {_recorder.VolumePathNames[0]}");
            }
            else
            {
                MessageBox.Show($"No drives");
            }
        }
        
        //Helper services
        private WaveStream CreateReader(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp3") return new Mp3FileReader(path);
            if (ext == ".wav") return new WaveFileReader(path);
            return new MediaFoundationReader(path);
        }
        
        private string GetWorkFolder()
        {
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioCdBurner", "work");
            
            if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

            return folder;
        }

        private void WriteAsCdWav(WaveStream reader, string outPath)
        {
            var target = new WaveFormat(44100, 16, 2);
            using var resampler = new MediaFoundationResampler(reader, target);
            WaveFileWriter.CreateWaveFile(outPath, resampler);
        }

        private void ValidateBtn_OnClick(object sender, RoutedEventArgs e)
        {
            _tao = new MsftDiscFormat2TrackAtOnce();
               
            if (_tao.IsRecorderSupported(_recorder) && _tao.IsCurrentMediaSupported(_recorder))
            {
                _tao.Recorder = _recorder;
                _tao.ClientName = "AudioCdBurner";
                MessageBox.Show("CD ready for burning");
                
                try
                {
                    _tao.PrepareMedia();
                    MessageBox.Show("Media prepared");
                }
                catch (COMException ex)
                {
                    MessageBox.Show("Could not prepare media: " + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("CD not valid for burning");
            }
        }
        
        private async void BurnBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                
                //TODO : Få statusTextBlock til at opdatere undervejs
                StatusTextBlock.Text = "Preparing media...";
                _tao.PrepareMedia();

                var inputFiles = new List<string>
                {
                    @"C:\Audio\track1.mp3",
                    @"C:\Audio\track2.flac",
                    @"C:\Audio\track3.wav"
                };

                var tempWavs = new List<string>();
                var streams  = new List<IStream>();

                try
                {
                    foreach (var path in inputFiles)
                    {
                        var wav = ConvertToPcmWav(path);
                        tempWavs.Add(wav);
                        streams.Add(OpenComStream(wav));
                        StatusTextBlock.Text = $"Added: {System.IO.Path.GetFileName(path)}";
                        await Task.Yield(); // lader UI trække vejret
                    }

                    foreach (var stm in streams)
                    {
                        _tao.AddAudioTrack(stm);
                    }

                    StatusTextBlock.Text = "Finalizing...";
                    _tao.ReleaseMedia();
                    StatusTextBlock.Text = "Burn complete.";
                }
                finally
                {
                    foreach (var s in streams) Marshal.ReleaseComObject(s);
                    foreach (var w in tempWavs) try { System.IO.File.Delete(w); } catch { }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Fejl: " + ex.Message;
            }
        }

        
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int SHCreateStreamOnFile(string pszFile, uint grfMode, out IStream ppstm);

        private string ConvertToPcmWav(string inputPath)
        {
            string wavPath = System.IO.Path.ChangeExtension(System.IO.Path.GetTempFileName(), ".wav");
            using (var reader = new AudioFileReader(inputPath))
            {
                var fmt = new WaveFormat(44100, 16, 2);
                using (var resampler = new MediaFoundationResampler(reader, fmt))
                {
                    WaveFileWriter.CreateWaveFile(wavPath, resampler);
                }
            }
            return wavPath;
        }

        private IStream OpenComStream(string filePath)
        {
            SHCreateStreamOnFile(filePath, 0x20, out var stm); // STGM_READ
            return stm;
        }

    }
}
