# ---------------------------------
# File: src\AVItoDVDISO.Core\Models\Enums.cs
# ---------------------------------
namespace AVItoDVDISO.Core.Models;

public enum DvdMode
{
    PAL,
    NTSC
}

public enum AspectMode
{
    Auto,
    Anamorphic16x9,
    Standard4x3
}

public enum ChapterMode
{
    Off,
    EveryNMinutes
}

public enum EncodeMode
{
    Fit,
    Best,
    Fast
}