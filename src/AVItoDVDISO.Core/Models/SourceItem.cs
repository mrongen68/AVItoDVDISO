using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AVItoDVDISO.Core.Models;

public sealed class SourceItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public required string Path { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    private TimeSpan _duration;
    public TimeSpan Duration
    {
        get => _duration;
        set { _duration = value; Raise(); }
    }

    private int _videoWidth;
    public int VideoWidth
    {
        get => _videoWidth;
        set { _videoWidth = value; Raise(); Raise(nameof(WxH)); }
    }

    private int _videoHeight;
    public int VideoHeight
    {
        get => _videoHeight;
        set { _videoHeight = value; Raise(); Raise(nameof(WxH)); }
    }

    public string WxH => $"{VideoWidth}x{VideoHeight}";

    private double _fps;
    public double Fps
    {
        get => _fps;
        set { _fps = value; Raise(); }
    }

    private bool _hasAudio;
    public bool HasAudio
    {
        get => _hasAudio;
        set { _hasAudio = value; Raise(); }
    }

    private int _audioChannels;
    public int AudioChannels
    {
        get => _audioChannels;
        set { _audioChannels = value; Raise(); }
    }

    private int _audioSampleRateHz;
    public int AudioSampleRateHz
    {
        get => _audioSampleRateHz;
        set { _audioSampleRateHz = value; Raise(); }
    }

    public override string ToString() => $"{FileName} ({Duration})";
}
