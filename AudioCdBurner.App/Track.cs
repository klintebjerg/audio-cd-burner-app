using System.Reflection;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioCdBurner.App;

public class Track : INotifyPropertyChanged
{
    private string _title = "";

    public string Title
    {
        get => _title;
        set { if (_title != value) {_title = value; OnPropertyChanged();} }
    }
    public string Artist { get; set; }
    public string Album { get; set; }
    public string Path { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsMp3 { get; set; }
    private int? _sampleRateHz;
    public int? SampleRateHz
    {
        get => _sampleRateHz;
        set { if (_sampleRateHz != value) {_sampleRateHz = value; OnPropertyChanged(); } }
    }
    private int? _channels;

    public int? Channels
    {
        get => _channels;
        set { if (_channels != value) {_channels = value; OnPropertyChanged();}}
    }
    private int? _bitrateKbps;
    public int? BitrateKbps
    {
        get => _bitrateKbps;
        set { if (_bitrateKbps != value) {_bitrateKbps = value; OnPropertyChanged();} }
    }
    public string? AudioNote { get; set; }
    public string? OutputWavPath { get; set; }
    public Track() {}
    
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
}