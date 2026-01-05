using AVItoDVDISO.Core.Services;
using AVItoDVDISO.Tools.Process;

namespace AVItoDVDISO.Tools.Tooling;

public sealed class XorrisoService
{
    private readonly ProcessRunner _runner;
    private readonly LogBuffer _log;
    private readonly ToolPaths _tools;

    public XorrisoService(ProcessRunner runner, LogBuffer log, ToolPaths tools)
    {
        _runner = runner;
        _log = log;
        _tools = tools;
    }

    public async Task<string> BuildIsoAsync(string dvdFolderDir, string isoPath, string discLabel, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(isoPath)!);

        // Using xorriso in mkisofs compatibility mode.
        var args = $"-as mkisofs -dvd-video -V \"{discLabel}\" -o \"{isoPath}\" \"{dvdFolderDir}\"";

        var exit = await _runner.RunAsync(_tools.Xorriso, args, null, ct);
        if (exit != 0) throw new InvalidOperationException("xorriso failed.");

        _log.Add($"Built ISO: {isoPath}");
        return isoPath;
    }

}
