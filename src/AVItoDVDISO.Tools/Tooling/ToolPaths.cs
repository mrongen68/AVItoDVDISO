# ---------------------------------
# File: src\AVItoDVDISO.Tools\Tooling\ToolPaths.cs
# ---------------------------------
namespace AVItoDVDISO.Tools.Tooling;

public sealed class ToolPaths
{
    public required string ToolsDir { get; init; }

    public string Ffmpeg => System.IO.Path.Combine(ToolsDir, "ffmpeg.exe");
    public string Ffprobe => System.IO.Path.Combine(ToolsDir, "ffprobe.exe");
    public string Dvdauthor => System.IO.Path.Combine(ToolsDir, "dvdauthor.exe");
    public string Xorriso => System.IO.Path.Combine(ToolsDir, "xorriso.exe");

    public void Validate()
    {
        Ensure(Ffmpeg);
        Ensure(Ffprobe);
        Ensure(Dvdauthor);
        Ensure(Xorriso);
    }

    private static void Ensure(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Missing required tool.", path);
    }
}