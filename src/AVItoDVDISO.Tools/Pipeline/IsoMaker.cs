using System.Diagnostics;
using System.Text;
using AVItoDVDISO.Core.Services;

namespace AVItoDVDISO.Tools.Pipeline;

public static class IsoMaker
{
    public static async Task CreateIsoFromDvdFolderAsync(
        string mkisofsExe,
        string dvdFolderPath,
        string outIsoPath,
        string discLabel,
        LogBuffer log,
        CancellationToken ct)
    {
        if (!File.Exists(mkisofsExe))
            throw new FileNotFoundException("mkisofs.exe not found.", mkisofsExe);

        if (!Directory.Exists(dvdFolderPath))
            throw new DirectoryNotFoundException("DVD folder not found: " + dvdFolderPath);

        Directory.CreateDirectory(Path.GetDirectoryName(outIsoPath)!);

        // First attempt: -dvd-video + -udf (some builds support both, best compatibility)
        var args1 = $"-dvd-video -udf -V \"{discLabel}\" -o \"{outIsoPath}\" \"{dvdFolderPath}\"";
        var ok = await RunAsync(mkisofsExe, args1, log, ct).ConfigureAwait(false);

        if (ok) return;

        // Fallback: only -dvd-video
        var args2 = $"-dvd-video -V \"{discLabel}\" -o \"{outIsoPath}\" \"{dvdFolderPath}\"";
        ok = await RunAsync(mkisofsExe, args2, log, ct).ConfigureAwait(false);

        if (!ok)
            throw new InvalidOperationException("mkisofs failed to create ISO. See log for details.");
    }

    private static async Task<bool> RunAsync(string exe, string args, LogBuffer log, CancellationToken ct)
    {
        log.Add("ISO: " + exe + " " + args);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start mkisofs.");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout)) log.Add(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr)) log.Add(stderr.TrimEnd());

        log.Add("ISO exit code: " + p.ExitCode);

        return p.ExitCode == 0 && File.Exists(GetOutIsoFromArgs(args));
    }

    private static string GetOutIsoFromArgs(string args)
    {
        // naive parse: find -o "path"
        var idx = args.IndexOf("-o ", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = args.Substring(idx + 3).TrimStart();
        if (rest.StartsWith("\""))
        {
            var end = rest.IndexOf('\"', 1);
            if (end > 1) return rest.Substring(1, end - 1);
        }
        else
        {
            var end = rest.IndexOf(' ');
            if (end > 0) return rest.Substring(0, end);
        }
        return "";
    }
}
