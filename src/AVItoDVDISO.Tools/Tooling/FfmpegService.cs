# ---------------------------------
# File: src\AVItoDVDISO.Tools\Tooling\FfmpegService.cs
# ---------------------------------
using AVItoDVDISO.Core.Models;
using AVItoDVDISO.Core.Services;
using AVItoDVDISO.Tools.Process;

namespace AVItoDVDISO.Tools.Tooling;

public sealed class FfmpegService
{
    private readonly ProcessRunner _runner;
    private readonly LogBuffer _log;
    private readonly ToolPaths _tools;

    public FfmpegService(ProcessRunner runner, LogBuffer log, ToolPaths tools)
    {
        _runner = runner;
        _log = log;
        _tools = tools;
    }

    public async Task<string> TranscodeToMpgAsync(
        string inputPath,
        string outputMpgPath,
        DvdMode mode,
        AspectMode aspect,
        PresetDefinition preset,
        int? fitVideoKbps,
        string passLogPrefix,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputMpgPath)!);

        var target = mode == DvdMode.PAL ? "pal-dvd" : "ntsc-dvd";
        var aspectArg = aspect switch
        {
            AspectMode.Anamorphic16x9 => "-aspect 16:9",
            AspectMode.Standard4x3 => "-aspect 4:3",
            _ => ""
        };

        var audio = preset.Audio;
        var video = preset.Video;

        var videoKbps = fitVideoKbps ?? video.TargetRateKbps ?? 5000;
        var maxRate = video.MaxRateKbps;
        var bufSize = video.BufSizeKbps;

        if (video.TwoPass)
        {
            var pass1Args =
                $"-y -i \"{inputPath}\" -target {target} {aspectArg} -an " +
                $"-b:v {videoKbps}k -maxrate {maxRate}k -bufsize {bufSize}k " +
                $"-pass 1 -passlogfile \"{passLogPrefix}\" -f mpeg2video NUL";

            var e1 = await _runner.RunAsync(_tools.Ffmpeg, pass1Args, null, ct);
            if (e1 != 0) throw new InvalidOperationException("ffmpeg pass 1 failed.");

            var pass2Args =
                $"-y -i \"{inputPath}\" -target {target} {aspectArg} " +
                $"-acodec {audio.Codec} -b:a {audio.BitrateKbps}k -ar {audio.SampleRateHz} -ac {audio.Channels} " +
                $"-b:v {videoKbps}k -maxrate {maxRate}k -bufsize {bufSize}k " +
                $"-pass 2 -passlogfile \"{passLogPrefix}\" \"{outputMpgPath}\"";

            var e2 = await _runner.RunAsync(_tools.Ffmpeg, pass2Args, null, ct);
            if (e2 != 0) throw new InvalidOperationException("ffmpeg pass 2 failed.");
        }
        else
        {
            var args =
                $"-y -i \"{inputPath}\" -target {target} {aspectArg} " +
                $"-acodec {audio.Codec} -b:a {audio.BitrateKbps}k -ar {audio.SampleRateHz} -ac {audio.Channels} " +
                $"-b:v {videoKbps}k -maxrate {maxRate}k -bufsize {bufSize}k " +
                $"\"{outputMpgPath}\"";

            var e = await _runner.RunAsync(_tools.Ffmpeg, args, null, ct);
            if (e != 0) throw new InvalidOperationException("ffmpeg failed.");
        }

        _log.Add($"Transcoded: {outputMpgPath}");
        return outputMpgPath;
    }
}