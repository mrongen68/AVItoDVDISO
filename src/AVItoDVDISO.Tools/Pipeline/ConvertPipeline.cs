using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AVItoDVDISO.Core.Models;
using AVItoDVDISO.Core.Services;
using AVItoDVDISO.Tools.Process;

namespace AVItoDVDISO.Tools.Pipeline;

public sealed class ConvertPipeline
{
    private readonly LogBuffer _log;

    public ConvertPipeline(LogBuffer log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task RunAsync(
        ConvertJobRequest req,
        Action<ConvertProgress> onProgress,
        CancellationToken ct)
    {
        if (req is null) throw new ArgumentNullException(nameof(req));
        if (onProgress is null) throw new ArgumentNullException(nameof(onProgress));
        if (req.Sources is null || req.Sources.Count == 0) throw new InvalidOperationException("No sources provided.");
        if (req.Dvd is null) throw new InvalidOperationException("DVD settings missing.");
        if (req.Output is null) throw new InvalidOperationException("Output settings missing.");
        if (string.IsNullOrWhiteSpace(req.WorkingDir)) throw new InvalidOperationException("WorkingDir missing.");
        if (string.IsNullOrWhiteSpace(req.ToolsDir)) throw new InvalidOperationException("ToolsDir missing.");

        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(req.WorkingDir);

        var jobDir = Path.Combine(req.WorkingDir, $"job_{DateTime.Now:yyyyMMdd_HHmmss}");
        var transcodeDir = Path.Combine(jobDir, "transcoded");
        var dvdRootDir = Path.Combine(jobDir, "dvdroot");
        var videoTsDir = Path.Combine(dvdRootDir, "VIDEO_TS");

        Directory.CreateDirectory(jobDir);
        Directory.CreateDirectory(transcodeDir);
        Directory.CreateDirectory(videoTsDir);

        var toolsDir = req.ToolsDir;

        var ffmpegExe = ResolveTool(toolsDir, "ffmpeg.exe");
        var dvdauthorExe = ResolveTool(toolsDir, "dvdauthor.exe");
        var xorrisoExe = ResolveToolOptional(toolsDir, "xorriso.exe");
        var imgburnExe = ResolveToolOptional(toolsDir, "ImgBurn.exe");

        var runner = new ProcessRunner(_log);

        onProgress(MakeProgress("Prepare", "Preparing job", 1));

        var mpegFiles = new List<string>();
        for (var i = 0; i < req.Sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var src = req.Sources[i];
            if (src is null || string.IsNullOrWhiteSpace(src.Path))
                throw new InvalidOperationException("Source path missing.");
            if (!File.Exists(src.Path))
                throw new FileNotFoundException("Source file not found.", src.Path);

            var baseName = SafeStem(Path.GetFileNameWithoutExtension(src.Path));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = $"input_{i + 1:D2}";

            var outMpeg = Path.Combine(transcodeDir, $"{baseName}.mpg");

            var pct = 5 + (int)Math.Round((i / Math.Max(1.0, req.Sources.Count)) * 55.0);
            onProgress(MakeProgress("Transcode", $"Transcoding {Path.GetFileName(src.Path)}", pct));

            var ffmpegArgs = BuildFfmpegDvdArgs(
                inputPath: src.Path,
                outputMpegPath: outMpeg,
                dvdMode: req.Dvd.Mode,
                aspect: req.Dvd.Aspect);

            await RunToolAsync(
                runner: runner,
                exe: ffmpegExe,
                args: ffmpegArgs,
                workingDir: jobDir,
                ct: ct);

            if (!File.Exists(outMpeg))
                throw new IOException($"FFmpeg did not produce expected output: {outMpeg}");

            mpegFiles.Add(outMpeg);
        }

        ct.ThrowIfCancellationRequested();

        onProgress(MakeProgress("Author", "Creating DVD structure (VIDEO_TS)", 65));

        var xmlPath = Path.Combine(jobDir, "dvdauthor.xml");
        File.WriteAllText(
            xmlPath,
            BuildDvdauthorXml(mpegFiles, req.Dvd.ChaptersMode, Math.Clamp(req.Dvd.ChaptersEveryMinutes, 1, 60)),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var dvdauthorArgs = $"-x \"{xmlPath}\"";
        await RunToolAsync(
            runner: runner,
            exe: dvdauthorExe,
            args: dvdauthorArgs,
            workingDir: dvdRootDir,
            ct: ct);

        onProgress(MakeProgress("Validate", "Validating VIDEO_TS", 75));
        ValidateVideoTs(videoTsDir);

        string? exportedVideoTs = null;

        if (req.Output.ExportFolder)
        {
            ct.ThrowIfCancellationRequested();

            var outFolder = EnsureOutputFolder(req.Output.OutputPath);
            var target = Path.Combine(outFolder, "VIDEO_TS");

            onProgress(MakeProgress("Export", $"Exporting VIDEO_TS to {target}", 82));

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);

            CopyDirectory(videoTsDir, target);
            exportedVideoTs = target;
        }

        if (req.Output.ExportIso)
        {
            ct.ThrowIfCancellationRequested();

            var outFolder = EnsureOutputFolder(req.Output.OutputPath);
            var isoPath = Path.Combine(outFolder, $"{SanitizeDiscLabel(req.Output.DiscLabel)}.iso");
            if (File.Exists(isoPath))
                File.Delete(isoPath);

            onProgress(MakeProgress("ISO", "Creating ISO image", 92));

            if (!string.IsNullOrWhiteSpace(imgburnExe) && File.Exists(imgburnExe))
            {
                var imgburnArgs =
                    "/MODE BUILD " +
                    "/BUILDINPUTMODE STANDARD " +
                    "/BUILDTYPE IMAGEFILE " +
                    $"/SRC \"{dvdRootDir}\" " +
                    $"/DEST \"{isoPath}\" " +
                    $"/VOLUMELABEL \"{SanitizeDiscLabel(req.Output.DiscLabel)}\" " +
                    "/FILESYSTEM \"UDF\" " +
                    "/UDFREVISION \"1.02\" " +
                    "/START " +
                    "/CLOSE";

                await RunToolAsync(
                    runner: runner,
                    exe: imgburnExe,
                    args: imgburnArgs,
                    workingDir: outFolder,
                    ct: ct);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(xorrisoExe) || !File.Exists(xorrisoExe))
                    throw new InvalidOperationException("No ISO tool available. Place ImgBurn.exe or xorriso.exe in the tools folder.");

                var label = SanitizeDiscLabel(req.Output.DiscLabel);

                var xorrisoArgs =
                    "-as mkisofs " +
                    "-dvd-video " +
                    "-udf " +
                    $"-V \"{label}\" " +
                    $"-o \"{isoPath}\" " +
                    $"\"{dvdRootDir}\"";

                await RunToolAsync(
                    runner: runner,
                    exe: xorrisoExe,
                    args: xorrisoArgs,
                    workingDir: outFolder,
                    ct: ct);
            }

            if (!File.Exists(isoPath))
                throw new IOException($"ISO creation failed. ISO not found: {isoPath}");
        }

        onProgress(MakeProgress("Done", "Completed", 100));

        if (!string.IsNullOrWhiteSpace(exportedVideoTs))
            TryLog($"Exported VIDEO_TS to: {exportedVideoTs}");
    }

    private static ConvertProgress MakeProgress(string stage, string message, int percent)
        => new ConvertProgress { Stage = stage, Message = message, Percent = Math.Clamp(percent, 0, 100) };

    private static string EnsureOutputFolder(string outputPath)
    {
        var p = outputPath;
        if (string.IsNullOrWhiteSpace(p))
            p = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        Directory.CreateDirectory(p);
        return p;
    }

    private static string ResolveTool(string toolsDir, string exeName)
    {
        var p = Path.Combine(toolsDir, exeName);
        if (!File.Exists(p))
            throw new FileNotFoundException($"Required tool not found: {p}");
        return p;
    }

    private static string? ResolveToolOptional(string toolsDir, string exeName)
    {
        var p = Path.Combine(toolsDir, exeName);
        return File.Exists(p) ? p : null;
    }

    private static async Task RunToolAsync(ProcessRunner runner, string exe, string args, string workingDir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // In this solution ProcessRunner.RunAsync returns an int (exit code).
        var exitCode = await runner.RunAsync(exe, args, workingDir, ct).ConfigureAwait(false);
        if (exitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} failed with exit code {exitCode}. See log for details.");
    }

    private static string BuildFfmpegDvdArgs(string inputPath, string outputMpegPath, DvdMode dvdMode, AspectMode aspect)
    {
        var isNtsc = dvdMode == DvdMode.NTSC;

        var targetW = 720;
        var targetH = isNtsc ? 480 : 576;

        var fps = isNtsc ? "30000/1001" : "25";

        var vf = $"scale={targetW}:{targetH}:force_original_aspect_ratio=decrease,pad={targetW}:{targetH}:(ow-iw)/2:(oh-ih)/2,setsar=1";

        var aspectFlag = aspect switch
        {
            AspectMode.Anamorphic16x9 => "-aspect 16:9",
            AspectMode.Standard4x3 => "-aspect 4:3",
            _ => ""
        };

        var vbitrate = "6000k";
        var vmax = "8000k";
        var vbuf = "1835k";

        var args =
            "-y " +
            $"-i \"{inputPath}\" " +
            "-map 0:v:0 -map 0:a? " +
            "-c:v mpeg2video " +
            $"-b:v {vbitrate} -maxrate {vmax} -bufsize {vbuf} " +
            "-flags +ilme+ildct " +
            $"-r {fps} " +
            "-g 15 -bf 2 " +
            $"-vf \"{vf}\" " +
            $"{(string.IsNullOrWhiteSpace(aspectFlag) ? "" : " " + aspectFlag)} " +
            "-c:a ac3 -b:a 192k -ar 48000 " +
            "-muxrate 10080000 " +
            "-f dvd " +
            $"\"{outputMpegPath}\"";

        return args;
    }

    private static string BuildDvdauthorXml(IReadOnlyList<string> mpegFiles, ChapterMode chaptersMode, int chaptersEveryMinutes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<dvdauthor>");
        sb.AppendLine("  <vmgm />");
        sb.AppendLine("  <titleset>");
        sb.AppendLine("    <titles>");
        sb.AppendLine("      <pgc>");

        foreach (var mpg in mpegFiles)
        {
            if (chaptersMode == ChapterMode.EveryNMinutes)
            {
                var ch = $"00:{chaptersEveryMinutes:00}:00";
                sb.AppendLine($"        <vob file=\"{EscapeXml(mpg)}\" chapters=\"{ch}\" />");
            }
            else
            {
                sb.AppendLine($"        <vob file=\"{EscapeXml(mpg)}\" />");
            }
        }

        sb.AppendLine("      </pgc>");
        sb.AppendLine("    </titles>");
        sb.AppendLine("  </titleset>");
        sb.AppendLine("</dvdauthor>");
        return sb.ToString();
    }

    private static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
    }

    private static void ValidateVideoTs(string videoTsDir)
    {
        if (!Directory.Exists(videoTsDir))
            throw new IOException("VIDEO_TS folder not found after authoring.");

        var ifos = Directory.GetFiles(videoTsDir, "*.IFO", SearchOption.TopDirectoryOnly);
        var bups = Directory.GetFiles(videoTsDir, "*.BUP", SearchOption.TopDirectoryOnly);
        var vobs = Directory.GetFiles(videoTsDir, "*.VOB", SearchOption.TopDirectoryOnly);

        if (ifos.Length == 0 || bups.Length == 0 || vobs.Length == 0)
            throw new IOException("VIDEO_TS folder is incomplete (missing IFO/BUP/VOB files).");
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var target = Path.Combine(destDir, name);
            File.Copy(file, target, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            var target = Path.Combine(destDir, name);
            CopyDirectory(dir, target);
        }
    }

    private static string SafeStem(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "output";
        foreach (var ch in Path.GetInvalidFileNameChars())
            s = s.Replace(ch, '_');
        return s.Trim();
    }

    private static string SanitizeDiscLabel(string label)
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
                sb.Append(ch);
        }

        var clean = sb.ToString().ToUpperInvariant();
        if (clean.Length == 0) clean = "AVITODVD";
        return clean;
    }

    private void TryLog(string line)
    {
        try
        {
            var d = (dynamic)_log;
            d.Add(line);
        }
        catch
        {
            // ignore
        }
    }
}
