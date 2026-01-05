# ---------------------------------
# File: src\AVItoDVDISO.Core\Models\PresetModels.cs
# ---------------------------------
namespace AVItoDVDISO.Core.Models;

public sealed class PresetsRoot
{
    public int Version { get; set; }
    public PresetsDefaults Defaults { get; set; } = new();
    public List<PresetDefinition> Presets { get; set; } = new();
}

public sealed class PresetsDefaults
{
    public string DvdMode { get; set; } = "PAL";
    public string Aspect { get; set; } = "Auto";
    public ChaptersDefaults Chapters { get; set; } = new();
    public string PresetId { get; set; } = "fit";
    public bool ExportFolder { get; set; } = true;
    public bool ExportIso { get; set; } = true;
    public string DiscLabel { get; set; } = "AVITODVD";
}

public sealed class ChaptersDefaults
{
    public string Mode { get; set; } = "Off";
    public int Minutes { get; set; } = 5;
}

public sealed class PresetDefinition
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string EncodeMode { get; set; } // Fit, Best, Fast
    public AudioPreset Audio { get; set; } = new();
    public VideoPreset Video { get; set; } = new();
}

public sealed class AudioPreset
{
    public string Codec { get; set; } = "ac3";
    public int BitrateKbps { get; set; } = 192;
    public int SampleRateHz { get; set; } = 48000;
    public int Channels { get; set; } = 2;
}

public sealed class VideoPreset
{
    public int? TargetRateKbps { get; set; }
    public int MaxRateKbps { get; set; } = 8000;
    public int BufSizeKbps { get; set; } = 1835;
    public int MinRateKbps { get; set; } = 2000;
    public bool TwoPass { get; set; } = false;
}