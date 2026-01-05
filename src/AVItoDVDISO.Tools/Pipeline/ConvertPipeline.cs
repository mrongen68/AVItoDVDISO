using AVItoDVDISO.Core.Models;
using AVItoDVDISO.Core.Services;
using AVItoDVDISO.Tools.Process;
using AVItoDVDISO.Tools.Tooling;

namespace AVItoDVDISO.Tools.Pipeline;

public sealed class ConvertPipeline
{
    private readonly LogBuffer _log;

    public ConvertPipeline(LogBuffer log)
    {
        _log = log;
    }

    public async Task RunAsync(ConvertJobRequest req, Action<ConvertProgress> onProgress, CancellationToken ct)
    {
        var tools = new ToolPaths { ToolsDir = req.ToolsDir };
        tools.Validate();

        Directory.CreateDirectory(req.WorkingDir);

        var runner = new ProcessRunner(_log);
        var ffprobe = new FfprobeService(runner, _log, tools);
        var ffmpeg = new FfmpegService(runner, _log, tools);
        var dvdauthor = new DvdauthorService(runner, _log, tools);
        var xorriso = new XorrisoService(runner, _log, tools);

        onProgress(new ConvertProgress { Stage = "Probe", Percent = 0, Message = "Probing sources" });

        foreach (var s in req.Sources)
        {
            ct.ThrowIfCancellationRequested();
            await ffprobe.ProbeAsyncPortable(s, ct);
        }

        var totalDuration = TimeSpan.FromSeconds(req.Sources.Sum(x => x.Duration.TotalSeconds));

        var preset = req.Preset;
        var encodeMode = Enum.TryParse<EncodeMode>(preset.EncodeMode, ignoreCase: true, out var em) ? em : EncodeMode.Fit;

        int? fitVideoKbps = null;
        if (encodeMode == EncodeMode.Fit)
        {
            fitVideoKbps = BitrateCalculator.CalculateVideoBitrateKbpsFit(
                totalDuration,
                preset.Audio.BitrateKbps,
                preset.Video.MinRateKbps,
                preset.Video.MaxRateKbps);
            _log.Add($"Fit bitrate: {fitVideoKbps} kbps");
        }

        onProgress(new ConvertProgress { Stage = "Transcode", Percent = 0, Message = "Transcoding" });

        var mpgPaths = new List<string>();
        for (int i = 0; i < req.Sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var src = req.Sources[i];
            var outMpg = Path.Combine(req.WorkingDir, $"title{i + 1:00}.mpg");
            var passLog = Path.Combine(req.WorkingDir, $"passlog_title{i + 1:00}");

            await ffmpeg.TranscodeToMpgAsync(
                src.Path,
                outMpg,
                req.Dvd.Mode,
                req.Dvd.Aspect,
                preset,
                fitVideoKbps,
                passLog,
                ct);

            mpgPaths.Add(outMpg);

            var pct = (int)Math.Round(((i + 1) / (double)req.Sources.Count) * 100);
            onProgress(new ConvertProgress { Stage = "Transcode", Percent = pct, Message = $"Transcoded {i + 1}/{req.Sources.Count}" });
        }

        onProgress(new ConvertProgress { Stage = "Author", Percent = 0, Message = "Authoring DVD folder" });

        var dvdFolder = Path.Combine(req.Output.OutputPath, "DVD");
        if (req.Output.ExportFolder)
        {
            if (Directory.Exists(dvdFolder))
                Directory.Delete(dvdFolder, recursive: true);
        }

        if (req.Output.ExportFolder)
        {
            await dvdauthor.AuthorAsync(mpgPaths, dvdFolder, req.Dvd.ChaptersMode, req.Dvd.ChaptersEveryMinutes, ct);
        }

        onProgress(new ConvertProgress { Stage = "Author", Percent = 100, Message = "DVD folder ready" });

        if (req.Output.ExportIso)
        {
            onProgress(new ConvertProgress { Stage = "Iso", Percent = 0, Message = "Building ISO" });

            var isoPath = Path.Combine(req.Output.OutputPath, $"{req.Output.DiscLabel}.iso");
            var dvdInputForIso = req.Output.ExportFolder ? dvdFolder : throw new InvalidOperationException("ISO requested but DVD folder export is off in v1.");

            await xorriso.BuildIsoAsync(dvdInputForIso, isoPath, req.Output.DiscLabel, ct);

            onProgress(new ConvertProgress { Stage = "Iso", Percent = 100, Message = "ISO ready" });
        }
    }

}
