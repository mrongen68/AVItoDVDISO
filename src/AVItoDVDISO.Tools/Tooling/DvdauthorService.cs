using System.Text;
using AVItoDVDISO.Core.Models;
using AVItoDVDISO.Core.Services;
using AVItoDVDISO.Tools.Process;

namespace AVItoDVDISO.Tools.Tooling;

public sealed class DvdauthorService
{
    private readonly ProcessRunner _runner;
    private readonly LogBuffer _log;
    private readonly ToolPaths _tools;

    public DvdauthorService(ProcessRunner runner, LogBuffer log, ToolPaths tools)
    {
        _runner = runner;
        _log = log;
        _tools = tools;
    }

    public async Task<string> AuthorAsync(List<string> mpgPaths, string dvdOutDir, ChapterMode chapterMode, int chapterMinutes, CancellationToken ct)
    {
        Directory.CreateDirectory(dvdOutDir);

        var xmlPath = Path.Combine(Path.GetTempPath(), $"dvdauthor_{Guid.NewGuid():N}.xml");
        try
        {
            var xml = BuildXml(mpgPaths, chapterMode, chapterMinutes);
            File.WriteAllText(xmlPath, xml, Encoding.UTF8);

            var args = $"-o \"{dvdOutDir}\" -x \"{xmlPath}\"";
            var exit = await _runner.RunAsync(_tools.Dvdauthor, args, null, ct);
            if (exit != 0) throw new InvalidOperationException("dvdauthor failed.");

            var videoTs = Path.Combine(dvdOutDir, "VIDEO_TS");
            if (!Directory.Exists(videoTs))
                throw new InvalidOperationException("dvdauthor did not produce VIDEO_TS.");

            _log.Add($"Authored DVD folder: {dvdOutDir}");
            return dvdOutDir;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    private static string BuildXml(List<string> mpgPaths, ChapterMode chapterMode, int chapterMinutes)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<dvdauthor>");
        sb.AppendLine(@"  <vmgm />");
        sb.AppendLine(@"  <titleset>");
        sb.AppendLine(@"    <titles>");
        foreach (var mpg in mpgPaths)
        {
            if (chapterMode == ChapterMode.EveryNMinutes && chapterMinutes > 0)
            {
                var ch = $"{chapterMinutes}:00";
                sb.AppendLine($@"      <pgc>");
                sb.AppendLine($@"        <vob file=""{EscapeXml(mpg)}"" chapters=""{ch}"" />");
                sb.AppendLine($@"      </pgc>");
            }
            else
            {
                sb.AppendLine($@"      <pgc>");
                sb.AppendLine($@"        <vob file=""{EscapeXml(mpg)}"" />");
                sb.AppendLine($@"      </pgc>");
            }
        }
        sb.AppendLine(@"    </titles>");
        sb.AppendLine(@"  </titleset>");
        sb.AppendLine(@"</dvdauthor>");
        return sb.ToString();
    }

    private static string EscapeXml(string s)
        => s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

}
