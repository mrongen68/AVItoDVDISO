using System.Text.Json;
using AVItoDVDISO.Core.Models;
using AVItoDVDISO.Core.Services;
using AVItoDVDISO.Tools.Process;

namespace AVItoDVDISO.Tools.Tooling;

public sealed class FfprobeService
{
    private readonly ProcessRunner _runner;
    private readonly LogBuffer _log;
    private readonly ToolPaths _tools;

    public FfprobeService(ProcessRunner runner, LogBuffer log, ToolPaths tools)
    {
        _runner = runner;
        _log = log;
        _tools = tools;
    }

    public async Task ProbeAsync(SourceItem item, CancellationToken ct)
    {
        var tempJson = System.IO.Path.GetTempFileName();
        try
        {
            var args = $"-v error -print_format json -show_format -show_streams \"{item.Path}\"";
            var exit = await _runner.RunAsync(_tools.Ffprobe, args + $" > \"{tempJson}\"", workingDir: null, ct: ct);

            // Redirection using ">" is a shell feature. ProcessStartInfo does not use a shell.
            // To keep this portable and reliable, we run ffprobe and capture output directly.
        }
        finally
        {
            try { File.Delete(tempJson); } catch { }
        }
    }

    public async Task ProbeAsyncPortable(SourceItem item, CancellationToken ct)
    {
        var json = await RunCaptureStdoutAsync(_tools.Ffprobe, $"-v error -print_format json -show_format -show_streams \"{item.Path}\"", ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        item.Duration = ParseDuration(root);
        ParseStreams(root, item);
    }

    private async Task<string> RunCaptureStdoutAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = new System.Diagnostics.Process { StartInfo = psi };
        _log.Add($"RUN: {exe} {args}");

        p.Start();

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        await p.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!string.IsNullOrWhiteSpace(stderr))
            _log.Add(stderr.Trim());

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe failed with exit code {p.ExitCode}");

        return stdout;
    }

    private static TimeSpan ParseDuration(JsonElement root)
    {
        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var dur))
            {
                if (dur.ValueKind == JsonValueKind.String && double.TryParse(dur.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                    return TimeSpan.FromSeconds(seconds);
                if (dur.ValueKind == JsonValueKind.Number && dur.TryGetDouble(out var seconds2))
                    return TimeSpan.FromSeconds(seconds2);
            }
        }
        return TimeSpan.Zero;
    }

    private static void ParseStreams(JsonElement root, SourceItem item)
    {
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            return;

        foreach (var s in streams.EnumerateArray())
        {
            var codecType = s.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;
            if (codecType == "video")
            {
                item.VideoWidth = s.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : 0;
                item.VideoHeight = s.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi) ? hi : 0;

                var rFrameRate = s.TryGetProperty("r_frame_rate", out var r) ? r.GetString() : null;
                item.Fps = ParseFraction(rFrameRate);
            }
            else if (codecType == "audio")
            {
                item.HasAudio = true;
                item.AudioChannels = s.TryGetProperty("channels", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
                item.AudioSampleRateHz = s.TryGetProperty("sample_rate", out var sr) && int.TryParse(sr.GetString(), out var sri) ? sri : 0;
            }
        }
    }

    private static double ParseFraction(string? frac)
    {
        if (string.IsNullOrWhiteSpace(frac)) return 0;
        var parts = frac.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
            double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var b) &&
            b != 0)
            return a / b;

        if (double.TryParse(frac, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;

        return 0;
    }

}
