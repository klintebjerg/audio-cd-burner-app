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
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms.VisualStyles;

namespace AudioCdBurner.App
{
    public partial class MainWindow : Window
    {
        private MsftDiscFormat2TrackAtOnce _tao;
        private IDiscRecorder2 _recorder;
        private bool _isRunning;
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
        private void RemoveBtn_Click(object sender, RoutedEventArgs e)
        {
            var toRemove = FilesList.SelectedItems.Cast<Track>().ToList();
            foreach (var t in toRemove) Tracks.Remove(t);
        }
        
        private async void CheckEverythingBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            _isRunning = true;
            CheckEverythingBtn.IsEnabled = false;

            try
            {
                await ExecutePipelineAsync();
            }
            finally
            {
                _isRunning = false;
                CheckEverythingBtn.IsEnabled = true;
            }
        }

        private async Task ExecutePipelineAsync()
        {
            if (!await RunStep(TranscodeAsync, "Transcode OK")) return;
            
            if (!await RunStep(DetectAsync, "Detect OK")) return;
            
            if (!await RunStep(ValidateAsync, "Validate OK")) return;
            
            var result = MessageBox.Show("Burn now?", "Burn", MessageBoxButton.YesNo); 
            
            if (result != MessageBoxResult.Yes)
            {
                StatusTextBlock.Text = "Aborted by user";
                return;
            }
            
            if (!await RunStep(BurnAsync, "Burn OK")) return;
            
            StatusTextBlock.Text = "Done";
        }

        private async Task TranscodeAsync()
        {
            EnsureTracksNotEmpty();
            var items = Tracks.ToList();
            EnsureAllFilesExist(items);
            EnsureWorkFolderWritable();

            var budget = TimeSpan.FromMinutes(80);
            var total = TimeSpan.Zero;

            await Task.Run(() =>
            {
                foreach (Track track in items)
                {
                    using (var reader = CreateReader(track.Path))
                    {
                        total += reader.TotalTime;
                        if (total > budget)
                            throw new InvalidOperationException("Total duration exceeds 80 minutes");
                        var outPath = System.IO.Path.Combine(
                            GetWorkFolder(),
                            System.IO.Path.GetFileNameWithoutExtension(track.Path) + ".wav");
                        
                        WriteAsCdWav(reader, outPath);
                        
                        Dispatcher.Invoke(()=> 
                        {
                            track.Duration = reader.TotalTime;
                            track.SampleRateHz = reader.WaveFormat.SampleRate;
                            track.Channels = reader.WaveFormat.Channels;
                            track.OutputWavPath = outPath;
                            track.AudioNote = "WAV OK";
                        });
                    }
                }
            });
        }

        private async Task DetectAsync()
        {
            var master = new MsftDiscMaster2();
            if (!master.IsSupportedEnvironment)
                throw new InvalidOperationException("No CD burner found");
            
            var recorders = new List<IDiscRecorder2>();
            foreach (string id in master)
            {
                var rec = new MsftDiscRecorder2();
                rec.InitializeDiscRecorder(id);
                recorders.Add(rec);
            }

            if (recorders.Count == 0)
                throw new InvalidOperationException("No CD burners found");
            
            _recorder = recorders[0];
        }

        private async Task ValidateAsync()
        {
            _tao = new MsftDiscFormat2TrackAtOnce();
            
            if (_tao.IsRecorderSupported(_recorder))
            {
                if (!_tao.IsCurrentMediaSupported(_recorder))
                {
                    var result = MessageBox.Show("Delete Files on disc?", "Burn", MessageBoxButton.YesNo);
                    if (result != MessageBoxResult.Yes) throw new InvalidOperationException("Media not blank");
                }

                await DeleteSync();
                
                _tao.Recorder = _recorder;
                _tao.ClientName = "AudioCdBurner";
            }
            else
            {
                throw new InvalidOperationException("CD not valid for burning");
            }
        }

        private async Task DeleteSync()
        {
            if (_recorder == null) throw new InvalidOperationException("Recorder not initialized.");
            
            await Task.Run(()=>
            {
                var erase = new MsftDiscFormat2Erase();
                if (erase.IsRecorderSupported(_recorder))
                {
                    erase.Recorder = _recorder;
                    erase.ClientName = "AudioCdBurner";
                    try
                    {
                        erase.EraseMedia();
                    }
                    catch (COMException ex)
                    {
                        throw new InvalidOperationException("Could not erase media: " + ex.Message);
                    }
                }
            });
        }

        private async Task BurnAsync()
        {
            if (_tao == null) throw new InvalidOperationException("Burner not initialized");
            if (_recorder == null) throw new InvalidOperationException("Recorder not initialized");
            var items = Tracks?.ToList() ?? throw new InvalidOperationException("No tracks loadead");
            if (items.Count == 0) throw new InvalidOperationException("No tracks to burn");

            var wavs = new List<string>();
            var temps = new HashSet<string>();
            foreach (var t in items)
            {
                var src = string.IsNullOrWhiteSpace(t.OutputWavPath) ? t.Path : t.OutputWavPath;
                if (!System.IO.File.Exists(src)) throw new FileNotFoundException("Output file not found", src);

                string wav = src;
                if (!src.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || !IsCdPcmWav(src))
                {
                    wav = ConvertToPcmWav(src);
                    temps.Add(wav);
                }
                wavs.Add(wav);
            }
            
            var streams = new List<IStream>();
            try
            {
                StatusTextBlock.Text = "Preparing media...";
                _tao.PrepareMedia();

                for (int i = 0; i < wavs.Count; i++)
                {
                    StatusTextBlock.Text = $"Adding track {i + 1} of {wavs.Count}...";
                    
                    var pcm = ToRawPcm(wavs[i]);
                    temps.Add(pcm); 

                    var stm = OpenComStream(pcm); 
                    streams.Add(stm);

                    _tao.AddAudioTrack(stm);
                    await Task.Delay(100);
                }
                
                StatusTextBlock.Text = "Finalizing burn...";
                _tao.ReleaseMedia();
                StatusTextBlock.Text = "Burn complete.";
            }
            finally
            {
                foreach (var s in streams) {try { Marshal.ReleaseComObject(s); } catch { } }
                foreach (var t in temps) try { System.IO.File.Delete(t); } catch { }
            }
        }
        private async Task<bool> RunStep(Func<Task> stepFunc, string successMessage)
        {
            try
            {
                StatusTextBlock.Text = "Running step...";
                await stepFunc();
                StatusTextBlock.Text = successMessage;
                await Task.Delay(500);
                return true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error: " + ex.Message;
                return false;
            }
        }
        
        //Helper Services
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
        
        private IStream OpenComStream(string filePath)
        {
            const uint STGM_READ = 0x00000000;
            const uint STGM_SHARE_DENY_WRITE = 0x00000020;

            int hr = SHCreateStreamOnFileEx(
                filePath,
                STGM_READ | STGM_SHARE_DENY_WRITE,
                0,
                false,
                null,
                out var stm);

            if (hr != 0) throw new COMException("SHCreateStreamOnFileEx failed", hr);
            return stm;
        }
        
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
        
        private string ToRawPcm(string wavPath)
        {
            string pcmPath = System.IO.Path.ChangeExtension(wavPath, ".pcm");
            using (var wav = new WaveFileReader(wavPath))
            {
                var f = wav.WaveFormat;
                if (f.Encoding != WaveFormatEncoding.Pcm || f.SampleRate != 44100 || f.BitsPerSample != 16 || f.Channels != 2)
                    throw new InvalidOperationException("Not Red Book PCM (44.1/16/stereo).");

                using (var fs = new FileStream(pcmPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    byte[] buffer = new byte[64 * 1024];
                    long total = 0;
                    int read;
                    while ((read = wav.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        fs.Write(buffer, 0, read);
                        total += read;
                    }

                    const int sector = 2352; // 588 frames * 4 bytes
                    int pad = (int)(total % sector == 0 ? 0 : sector - (total % sector));
                    if (pad > 0)
                    {
                        Array.Clear(buffer, 0, Math.Min(pad, buffer.Length));
                        while (pad > 0)
                        {
                            int chunk = Math.Min(pad, buffer.Length);
                            fs.Write(buffer, 0, chunk);
                            pad -= chunk;
                        }
                    }
                }
            }
            return pcmPath;
        }

        
        private bool IsCdPcmWav(string path)
        {
            using var r = new WaveFileReader(path);
            var f = r.WaveFormat;
            return f.Encoding == WaveFormatEncoding.Pcm
                   && f.SampleRate == 44100
                   && f.BitsPerSample == 16
                   && f.Channels == 2;
        }
        
        private void EnsureTracksNotEmpty()
        {
            if (Tracks == null || !Tracks.Any())
                throw new InvalidOperationException("No tracks in list");
        }

        private void EnsureAllFilesExist(IEnumerable<Track> items)
        {
            foreach (var item in items)
                if (!System.IO.File.Exists(item.Path))
                    throw new FileNotFoundException("FIle not found: " + item.Path);
        }

        private void EnsureWorkFolderWritable()
        {
            var folder = GetWorkFolder();
            try
            {
                var probe = System.IO.Path.Combine(folder, "._probe");
                System.IO.File.WriteAllText(probe, "test");
                System.IO.File.Delete(probe);
            }
            catch
            {
                throw new InvalidOperationException("Unable to write files");
            }
        }
        
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int SHCreateStreamOnFileEx(
            string pszFile,
            uint grfMode,
            uint dwAttributes,
            bool fCreate,
            IStream pstmTemplate,
            out IStream ppstm);
    }
}
