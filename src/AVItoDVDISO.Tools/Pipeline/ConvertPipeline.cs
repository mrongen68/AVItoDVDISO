using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AVItoDVDISO.Core.Models;

namespace AVItoDVDISO.Tools;

public sealed class ConvertPipeline
{
    private readonly LogBuffer _log;

    public ConvertPipeline(LogBuffer log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task RunAsync(ConvertJobRequest req, Action<ConvertProgress> progress, CancellationToken ct)
    {
        if (req is null) throw new ArgumentNullException(nameof(req));
        if (progress is null) throw new ArgumentNullException(nameof(progress));
        ct.ThrowIfCancellationRequested();

        if (req.Sources is null || req.Sources.Count == 0)
            throw new InvalidOperationException("No sources provided.");

        if (req.Output is null)
            throw new InvalidOperationException("Output settings are missing.");

        if (req.Dvd is null)
            throw new InvalidOperationException("DVD settings are missing.");

        if (string.IsNullOrWhiteSpace(req.WorkingDir))
            throw new InvalidOperationException("WorkingDir is missing.");

        if (string.IsNullOrWhiteSpace(req.ToolsDir))
            throw new InvalidOperationException("ToolsDir is missing.");

        Directory.CreateDirectory(req.WorkingDir);

        var jobDir = Path.Combine(req.WorkingDir, $"job_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(jobDir);

        var dvdRoot = Path.Combine(jobDir, "dvdroot");
        var videoTsDir = Path.Combine(dvdRoot, "VIDEO_TS");
        Directory.CreateDirectory(videoTsDir);

        var assetsDir = Path.Combine(jobDir, "assets");
        Directory.CreateDirectory(assetsDir);

        var transcodeDir = Path.Combine(jobDir, "transcoded");
        Directory.CreateDirectory(transcodeDir);

        Report(progress, 1, "Init", $"Working directory: {jobDir}");
        Report(progress, 2, "Init", $"DVD root: {dvdRoot}");

        var ffmpegExe = Path.Combine(req.ToolsDir, "ffmpeg.exe");
        var ffprobeExe = Path.Combine(req.ToolsDir, "ffprobe.exe");
        var dvdauthorExe = Path.Combine(req.ToolsDir, "dvdauthor.exe");
        var imgBurnExe = ResolveImgBurnPath(req.ToolsDir);

        if (!File.Exists(ffmpegExe))
            throw new FileNotFoundException("ffmpeg.exe not found in tools folder.", ffmpegExe);

        if (!File.Exists(dvdauthorExe))
            throw new FileNotFoundException("dvdauthor.exe not found in tools folder.", dvdauthorExe);

        if (req.Output.ExportIso && string.IsNullOrWhiteSpace(imgBurnExe))
            throw new FileNotFoundException("ImgBurn.exe not found in tools folder. ISO export is enabled, but ImgBurn is missing.");

        var titleMpgs = new List<string>();
        for (int i = 0; i < req.Sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var src = req.Sources[i];
            if (src is null || string.IsNullOrWhiteSpace(src.Path) || !File.Exists(src.Path))
                throw new FileNotFoundException("Source file not found.", src?.Path ?? "(null)");

            var inPath = src.Path;
            var outMpg = Path.Combine(transcodeDir, $"title_{i + 1:00}.mpg");

            Report(progress, Percent(3, 55, i, req.Sources.Count), "Transcode", $"Transcoding: {Path.GetFileName(inPath)}");

            var ffArgs = BuildFfmpegArgs(inPath, outMpg, req.Dvd, req.Preset);
            await ExecAsync(ffmpegExe, ffArgs, jobDir, progress, "ffmpeg", ct).ConfigureAwait(false);

            if (!File.Exists(outMpg) || new FileInfo(outMpg).Length == 0)
                throw new InvalidOperationException($"Transcode produced no output: {outMpg}");

            titleMpgs.Add(outMpg);
        }

        ct.ThrowIfCancellationRequested();

        Report(progress, 56, "Author", "Creating dvdauthor XML");
        var xmlPath = Path.Combine(assetsDir, "dvdauthor.xml");
        File.WriteAllText(xmlPath, BuildDvdauthorXml(titleMpgs, req.Dvd), Encoding.UTF8);

        Report(progress, 60, "Author", "Running dvdauthor (DVD-Video authoring)");
        var dvdAuthorArgs = $"-x \"{xmlPath}\"";
        await ExecAsync(dvdauthorExe, dvdAuthorArgs, dvdRoot, progress, "dvdauthor", ct).ConfigureAwait(false);

        if (!Directory.Exists(videoTsDir))
            throw new InvalidOperationException("dvdauthor did not create VIDEO_TS folder.");

        var ifoCount = Directory.EnumerateFiles(videoTsDir, "*.IFO", SearchOption.TopDirectoryOnly).Count();
        var vobCount = Directory.EnumerateFiles(videoTsDir, "*.VOB", SearchOption.TopDirectoryOnly).Count();
        if (ifoCount == 0 || vobCount == 0)
            throw new InvalidOperationException("DVD authoring finished, but VIDEO_TS appears incomplete (missing IFO/VOB).");

        Report(progress, 70, "Author", $"DVD structure ready: IFO={ifoCount}, VOB={vobCount}");

        if (req.Output.ExportFolder)
        {
            ct.ThrowIfCancellationRequested();

            var targetVideoTs = Path.Combine(req.Output.OutputPath ?? "", "VIDEO_TS");
            if (string.IsNullOrWhiteSpace(req.Output.OutputPath))
                throw new InvalidOperationException("OutputPath is empty while ExportFolder is enabled.");

            Directory.CreateDirectory(req.Output.OutputPath);

            Report(progress, 75, "Export", $"Exporting VIDEO_TS to: {targetVideoTs}");
            if (Directory.Exists(targetVideoTs))
                Directory.Delete(targetVideoTs, true);

            CopyDirectory(videoTsDir, targetVideoTs);
            Report(progress, 80, "Export", "VIDEO_TS exported");
        }

        if (req.Output.ExportIso)
        {
            ct.ThrowIfCancellationRequested();

            var outIso = Path.Combine(req.Output.OutputPath ?? "", $"{req.Output.DiscLabel ?? "AVITODVD"}.iso");
            Directory.CreateDirectory(req.Output.OutputPath ?? "");

            Report(progress, 85, "ISO", $"Creating ISO via ImgBurn: {Path.GetFileName(outIso)}");
            await CreateIsoWithImgBurnAsync(
                imgBurnExe!,
                dvdRoot,
                outIso,
                req.Output.DiscLabel ?? "AVITODVD",
                progress,
                ct
            ).ConfigureAwait(false);

            if (!File.Exists(outIso) || new FileInfo(outIso).Length == 0)
                throw new InvalidOperationException($"ISO creation produced no output: {outIso}");

            Report(progress, 99, "ISO", "ISO created");
        }

        Report(progress, 100, "Done", "Conversion finished");
    }

    private static int Percent(int start, int end, int index, int total)
    {
        if (total <= 0) return start;
        if (index < 0) index = 0;
        if (index > total) index = total;
        var span = end - start;
        var p = start + (int)Math.Round(span * (index / (double)total), MidpointRounding.AwayFromZero);
        return Math.Clamp(p, start, end);
    }

    private void Report(Action<ConvertProgress> progress, int percent, string stage, string message)
    {
        var p = new ConvertProgress
        {
            Percent = Math.Clamp(percent, 0, 100),
            Stage = stage ?? "",
            Message = message ?? ""
        };

        _log.AddLine($"{stage}: {message}");
        progress(p);
    }

    private async Task ExecAsync(
        string exe,
        string args,
        string workingDir,
        Action<ConvertProgress> progress,
        string label,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _log.AddLine($"{label}: {exe} {args}");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null)
            throw new InvalidOperationException($"Failed to start process: {exe}");

        var stdoutTask = ReadLinesAsync(p.StandardOutput, line => _log.AddLine(line), ct);
        var stderrTask = ReadLinesAsync(p.StandardError, line => _log.AddLine(line), ct);

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{label} failed with exit code {p.ExitCode}.");
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine, CancellationToken ct)
    {
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null) break;
            if (line.Length > 0) onLine(line);
        }
    }

    private static string BuildFfmpegArgs(string input, string outputMpg, DvdSettings dvd, PresetDefinition? preset)
    {
        var isNtsc = dvd.Mode == DvdMode.NTSC;
        var fps = isNtsc ? "30000/1001" : "25";
        var size = isNtsc ? "720x480" : "720x576";

        var aspectFlag = dvd.Aspect switch
        {
            AspectMode.Anamorphic16x9 => "16:9",
            AspectMode.Standard4x3 => "4:3",
            _ => "16:9"
        };

        var vBitrateK = GetPresetInt(preset, "video_bitrate_k", isNtsc ? 5500 : 6000);
        var aBitrateK = GetPresetInt(preset, "audio_bitrate_k", 192);
        var audioRate = GetPresetInt(preset, "audio_sample_rate_hz", 48000);

        var sb = new StringBuilder();
        sb.Append("-y ");
        sb.Append($"-i \"{input}\" ");
        sb.Append("-vf ");
        sb.Append($"\"scale={size}:flags=lanczos,setsar=1,setdar={aspectFlag}\" ");
        sb.Append($"-r {fps} ");
        sb.Append("-c:v mpeg2video ");
        sb.Append($"-b:v {vBitrateK}k ");
        sb.Append("-maxrate 9000k -bufsize 1835k ");
        sb.Append("-pix_fmt yuv420p ");
        sb.Append("-g 15 -bf 2 ");
        sb.Append("-c:a ac3 ");
        sb.Append($"-b:a {aBitrateK}k ");
        sb.Append($"-ar {audioRate} ");
        sb.Append("-ac 2 ");
        sb.Append("-muxrate 10080000 ");
        sb.Append("-packetsize 2048 ");
        sb.Append($"\"{outputMpg}\"");

        return sb.ToString();
    }

    private static int GetPresetInt(PresetDefinition? preset, string key, int fallback)
    {
        try
        {
            if (preset is null) return fallback;

            var prop = preset.GetType().GetProperty("Params");
            if (prop?.GetValue(preset) is IDictionary<string, string> dict &&
                dict.TryGetValue(key, out var s) &&
                int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }
        }
        catch
        {
        }
        return fallback;
    }

    private static string BuildDvdauthorXml(IReadOnlyList<string> titleMpgs, DvdSettings dvd)
    {
        var dvdOut = Path.Combine(".");

        var formatAttr = dvd.Mode == DvdMode.NTSC ? "ntsc" : "pal";

        var aspectAttr = dvd.Aspect switch
        {
            AspectMode.Standard4x3 => "4:3",
            AspectMode.Anamorphic16x9 => "16:9",
            _ => "16:9"
        };

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<dvdauthor dest=\"{dvdOut}\">");
        sb.AppendLine("  <vmgm />");
        sb.AppendLine("  <titleset>");
        sb.AppendLine("    <titles>");

        foreach (var mpg in titleMpgs)
        {
            var fileName = Path.GetFileName(mpg);
            sb.AppendLine($"      <pgc>");
            sb.AppendLine($"        <vob file=\"{Path.Combine("..", "transcoded", fileName)}\" />");
            sb.AppendLine($"      </pgc>");
        }

        sb.AppendLine("    </titles>");
        sb.AppendLine("  </titleset>");
        sb.AppendLine("</dvdauthor>");

        var xml = sb.ToString();

        xml = xml.Replace("<titles>", $"<titles format=\"{formatAttr}\" aspect=\"{aspectAttr}\">", StringComparison.OrdinalIgnoreCase);

        return xml;
    }

    private async Task CreateIsoWithImgBurnAsync(
        string imgBurnExe,
        string dvdRoot,
        string outIso,
        string discLabel,
        Action<ConvertProgress> progress,
        CancellationToken ct)
    {
        if (!File.Exists(imgBurnExe))
            throw new FileNotFoundException("ImgBurn.exe not found.", imgBurnExe);

        Directory.CreateDirectory(Path.GetDirectoryName(outIso) ?? ".");

        if (File.Exists(outIso))
            File.Delete(outIso);

        var label = SanitizeLabelForImgBurn(discLabel);

        var args =
            "/MODE BUILD " +
            "/BUILDMODE IMAGEFILE " +
            "/BUILDOUTPUTMODE IMAGEFILE " +
            "/FILESYSTEM \"ISO9660 + UDF\" " +
            "/UDFREVISION 1.02 " +
            "/VOLUMELABEL \"" + label + "\" " +
            "/SRC \"" + dvdRoot + "\" " +
            "/DEST \"" + outIso + "\" " +
            "/START " +
            "/CLOSE";

        Report(progress, 90, "ISO", "Starting ImgBurn");
        await ExecAsync(imgBurnExe, args, Path.GetDirectoryName(imgBurnExe) ?? Environment.CurrentDirectory, progress, "ImgBurn", ct).ConfigureAwait(false);
        Report(progress, 98, "ISO", "ImgBurn finished");
    }

    private static string SanitizeLabelForImgBurn(string label)
    {
        var s = (label ?? "AVITODVD").Trim();
        if (s.Length == 0) s = "AVITODVD";
        if (s.Length > 32) s = s.Substring(0, 32);

        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if ((ch >= 'A' && ch <= 'Z') ||
                (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '_' || ch == '-')
            {
                sb.Append(ch);
            }
        }

        var clean = sb.ToString().ToUpperInvariant();
        if (clean.Length == 0) clean = "AVITODVD";
        return clean;
    }

    private static string? ResolveImgBurnPath(string toolsDir)
    {
        var direct = Path.Combine(toolsDir, "ImgBurn.exe");
        if (File.Exists(direct)) return direct;

        var toolsSub = Path.Combine(toolsDir, "ImgBurn", "ImgBurn.exe");
        if (File.Exists(toolsSub)) return toolsSub;

        var binSub = Path.Combine(toolsDir, "bin", "ImgBurn.exe");
        if (File.Exists(binSub)) return binSub;

        return null;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, dest);
        }
    }
}
