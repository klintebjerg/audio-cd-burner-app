using System.Reflection;

namespace AudioCdBurner.App;

public class Track
{
    public string Title { get; set; }
    public string Artist { get; set; }
    public string Album { get; set; }
    public string Path { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsMp3 { get; set; }
    public int? SampleRateHz { get; set; }
    public int? Channels { get; set; }
    public int? BitrateKbps { get; set; }
    public string? AudioNote { get; set; }
    public string? OutputWavPath { get; set; }
    public Track() {}
}