# ---------------------------------
# File: src\AVItoDVDISO.Core\Services\JobModels.cs
# ---------------------------------
using AVItoDVDISO.Core.Models;

namespace AVItoDVDISO.Core.Services;

public sealed class ConvertJobRequest
{
    public required List<SourceItem> Sources { get; init; }
    public required DvdSettings Dvd { get; init; }
    public required OutputSettings Output { get; init; }
    public required string WorkingDir { get; init; }
    public required string ToolsDir { get; init; }
    public required PresetDefinition Preset { get; init; }
}

public sealed class ConvertProgress
{
    public required string Stage { get; init; } // Probe, Transcode, Author, Iso
    public int Percent { get; init; }
    public string? Message { get; init; }
}