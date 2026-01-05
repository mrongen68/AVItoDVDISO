# ---------------------------------
# File: src\AVItoDVDISO.Core\Models\OutputSettings.cs
# ---------------------------------
namespace AVItoDVDISO.Core.Models;

public sealed class OutputSettings
{
    public bool ExportFolder { get; set; } = true;
    public bool ExportIso { get; set; } = true;
    public required string OutputPath { get; set; }
    public string DiscLabel { get; set; } = "AVITODVD";
}