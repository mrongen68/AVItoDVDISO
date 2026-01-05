# ---------------------------------
# File: src\AVItoDVDISO.Core\Models\SourceItem.cs
# ---------------------------------
namespace AVItoDVDISO.Core.Models;

public sealed class SourceItem
{
    public required string Path { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);

    public TimeSpan Duration { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public double Fps { get; set; }

    public bool HasAudio { get; set; }
    public int AudioChannels { get; set; }
    public int AudioSampleRateHz { get; set; }

    public override string ToString() => $"{FileName} ({Duration})";
}