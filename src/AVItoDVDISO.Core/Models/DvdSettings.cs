# ---------------------------------
# File: src\AVItoDVDISO.Core\Models\DvdSettings.cs
# ---------------------------------
namespace AVItoDVDISO.Core.Models;

public sealed class DvdSettings
{
    public DvdMode Mode { get; set; } = DvdMode.PAL;
    public AspectMode Aspect { get; set; } = AspectMode.Auto;
    public ChapterMode ChaptersMode { get; set; } = ChapterMode.Off;
    public int ChaptersEveryMinutes { get; set; } = 5;
    public string PresetId { get; set; } = "fit";
}