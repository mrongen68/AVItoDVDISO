using System.Diagnostics;
using System.Text;
using AVItoDVDISO.Core.Models;
using AVItoDVDISO.Core.Services;

namespace AVItoDVDISO.Tools.Pipeline;

public sealed class ConvertPipeline
{
    private readonly LogBuffer _log;

    public ConvertPipeline(LogBuffer log)
    {
        _log = log;
    }

    public async Task RunAsync(
        ConvertJobRequest req,
        Action<ConvertProgress> progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        ValidateRequest(req);

        var ffmpegExe = Path.Combine(req.ToolsDir, "ffmpeg.exe");
        var dvdauthorExe = Path.Combine(req.ToolsDir, "dvdauthor.exe");
        var mkisofsExe = Path.Combine(req.ToolsDir, "mkisofs.exe");

        if (!File.Exists(ffmpegExe))
            throw new FileNotFoundException("Missing required tool: ffmpeg.exe", ffmpegExe);

        if (req.Output.ExportFolder && !File.Exists(dvdauthorExe))
            throw new FileNotFoundException("Missing required tool: dvdauthor.exe", dvdauthorExe);

        if (req.Output.ExportIso && !File.Exists(mkisofsExe))
            throw new FileNotFoundException("Missing required tool: mkisofs.exe", mkisofsExe);

        Directory.CreateDirectory(req.WorkingDir);

        var workRoot = Path.Combine(req.WorkingDir, "job_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var encodeDir = Path.Combine(workRoot, "encode");
        var dvdRoot = Path.Combine(workRoot, "dvdroot");
        var videoTsDir = Path.Combine(dvdRoot, "VIDEO_TS");

        Directory.CreateDirectory(encodeDir);
        Directory.CreateDirectory(dvdRoot);

        progress(new ConvertProgress { Stage = "Prepare", Percent = 5, Message = "Starting job" });
        _log.Add("WorkingDir: " + workRoot);
        _log.Add("ToolsDir: " + req.ToolsDir);

        // 1) Encode each source to DVD-compliant MPEG
        progress(new ConvertProgress { Stage = "Encode", Percent = 10, Message = "Encoding to DVD MPEG-2" });

        var mpegFiles = new List<string>();
        for (var i = 0; i < req.Sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var src = req.Sources[i];
            var outMpg = Path.Combine(encodeDir, $"title_{i + 1:00}.mpg");
            mpegFiles.Add(outMpg);

            _log.Add($"Encoding {i + 1}/{req.Sources.Count}: {src.Path}");
            await EncodeToDvdMpegAsync(ffmpegExe, src.Path, outMpg, req, ct).ConfigureAwait(false);

            var pct = 10 + (int)Math.Floor(40.0 * (i + 1) / Math.Max(1, req.Sources.Count));
            progress(new ConvertProgress { Stage = "Encode", Percent = pct, Message = $"Encoded {i + 1}/{req.Sources.Count}" });
        }

        // 2) Create DVD folder using dvdauthor (only if needed)
        if (req.Output.ExportFolder || req.Output.ExportIso)
        {
            // For ISO we also need a proper dvdroot
            progress(new ConvertProgress { Stage = "Author", Percent = 55, Message = "Authoring VIDEO_TS" });

            if (!File.Exists(dvdauthorExe))
                throw new FileNotFoundException("dvdauthor.exe is required for VIDEO_TS creation.", dvdauthorExe);

            var xmlPath = Path.Combine(workRoot, "dvdauthor.xml");
            File.WriteAllText(xmlPath, BuildDvdauthorXml(mpegFiles), Encoding.UTF8);

            _log.Add("dvdauthor.xml: " + xmlPath);
            await RunProcessAsync(
                dvdauthorExe,
                $"-o \"{dvdRoot}\" -x \"{xmlPath}\"",
                workRoot,
                ct).ConfigureAwait(false);

            if (!Directory.Exists(videoTsDir))
                throw new InvalidOperationException("dvdauthor did not create VIDEO_TS folder.");

            progress(new ConvertProgress { Stage = "Author", Percent = 70, Message = "VIDEO_TS created" });
        }

        // 3) Copy DVD folder to output (if enabled)
        if (req.Output.ExportFolder)
        {
            progress(new ConvertProgress { Stage = "Export", Percent = 75, Message = "Copying DVD folder" });

            var outDvdRoot = req.Output.OutputPath;
            Directory.CreateDirectory(outDvdRoot);

            var outVideoTs = Path.Combine(outDvdRoot, "VIDEO_TS");
            var outAudioTs = Path.Combine(outDvdRoot, "AUDIO_TS");

            if (Directory.Exists(outVideoTs)) Directory.Delete(outVideoTs, true);
            if (Directory.Exists(outAudioTs)) Directory.Delete(outAudioTs, true);

            CopyDirectory(videoTsDir, outVideoTs);
            Directory.CreateDirectory(outAudioTs); // standard empty folder

            progress(new ConvertProgress { Stage = "Export", Percent = 85, Message = "DVD folder exported" });
            _log.Add("Exported VIDEO_TS to: " + outVideoTs);
        }

        // 4) Create ISO (if enabled)
        if (req.Output.ExportIso)
        {
            progress(new ConvertProgress { Stage = "ISO", Percent = 88, Message = "Creating ISO image" });

            var isoName = req.Output.DiscLabel;
            if (string.IsNullOrWhiteSpace(isoName)) isoName = "AVITODVD";

            var isoPath = Path.Combine(req.Output.OutputPath, isoName + ".iso");
            Directory.CreateDirectory(req.Output.OutputPath);

            // IMPORTANT: mkisofs needs the DVD root folder which contains VIDEO_TS (and optionally AUDIO_TS)
            if (!Directory.Exists(Path.Combine(dvdRoot, "AUDIO_TS")))
                Directory.CreateDirectory(Path.Combine(dvdRoot, "AUDIO_TS"));

            await IsoMaker.CreateIsoFromDvdFolderAsync(
                mkisofsExe,
                dvdRoot,
                isoPath,
                isoName,
                _log,
                ct).ConfigureAwait(false);

            if (!File.Exists(isoPath))
                throw new InvalidOperationException("ISO creation finished without producing an ISO file.");

            progress(new ConvertProgress { Stage = "ISO", Percent = 98, Message = "ISO created" });
            _log.Add("ISO created: " + isoPath);
        }

        progress(new ConvertProgress { Stage = "Done", Percent = 100, Message = "Completed" });
        _log.Add("Job completed.");
    }

    private static void ValidateRequest(ConvertJobRequest req)
    {
        if (req.Sources is null || req.Sources.Count == 0)
            throw new InvalidOperationException("No sources selected.");

        if (req.Preset is null)
            throw new InvalidOperationException("No preset selected.");

        if (req.Output is null)
            throw new InvalidOperationException("No output settings.");

        if (!req.Output.ExportFolder && !req.Output.ExportIso)
            throw new InvalidOperationException("No output option selected.");

        if (string.IsNullOrWhiteSpace(req.Output.OutputPath))
            throw new InvalidOperationException("Output path is empty.");
    }

    private async Task EncodeToDvdMpegAsync(string ffmpegExe, string input, string outMpg, ConvertJobRequest req, CancellationToken ct)
    {
        var target = req.Dvd.Mode == DvdMode.NTSC ? "ntsc-dvd" : "pal-dvd";

        string aspectArg = req.Dvd.Aspect switch
        {
            AspectMode.Anamorphic16x9 => "-aspect 16:9",
            AspectMode.Standard4x3 => "-aspect 4:3",
            _ => ""
        };

        // Audio: use preset audio bitrate when available
        var audioKbps = req.Preset.Audio?.BitrateKbps > 0 ? req.Preset.Audio.BitrateKbps : 192;

        // Video: if preset specifies target rate, use it; else let ffmpeg decide for target dvd profile
        var videoRateArg = "";
        if (req.Preset.Video?.TargetRateKbps is int vr && vr > 0)
        {
            videoRateArg = $"-b:v {vr}k -maxrate {Math.Max(vr, req.Preset.Video.MaxRateKbps)}k -bufsize {Math.Max(1, req.Preset.Video.BufSizeKbps)}k";
        }

        // Build args
        var args = new StringBuilder();
        args.Append("-y ");
        args.Append("-i ").Append('"').Append(input).Append("\" ");
        args.Append("-target ").Append(target).Append(' ');
        if (!string.IsNullOrWhiteSpace(videoRateArg)) args.Append(videoRateArg).Append(' ');
        if (!string.IsNullOrWhiteSpace(aspectArg)) args.Append(aspectArg).Append(' ');
        args.Append("-c:a ac3 ").Append($"-b:a {audioKbps}k ").Append(' ');
        args.Append("-ar 48000 ");
        args.Append("-ac 2 ");
        args.Append('"').Append(outMpg).Append('"');

        await RunProcessAsync(ffmpegExe, args.ToString(), Path.GetDirectoryName(outMpg)!, ct).ConfigureAwait(false);

        if (!File.Exists(outMpg))
            throw new InvalidOperationException("ffmpeg finished without producing MPG: " + outMpg);
    }

private string BuildDvdauthorXml(List<string> mpgFiles)
{
    var sb = new StringBuilder();

    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    sb.AppendLine("<dvdauthor>");
    sb.AppendLine("  <vmgm />");
    sb.AppendLine("  <titleset>");
    sb.AppendLine("    <titles>");
    sb.AppendLine("      <pgc>");

    foreach (var f in mpgFiles)
    {
        var safe = EscapeXml(f);
        sb.AppendLine($"        <vob file=\"{safe}\" />");
    }

    sb.AppendLine("      </pgc>");
    sb.AppendLine("    </titles>");
    sb.AppendLine("  </titleset>");
    sb.AppendLine("</dvdauthor>");

    return sb.ToString();
}

    private static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private async Task RunProcessAsync(string exe, string args, string workDir, CancellationToken ct)
    {
        _log.Add("RUN: " + exe + " " + args);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process: " + exe);

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout)) _log.Add(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr)) _log.Add(stderr.TrimEnd());

        _log.Add("Exit code: " + p.ExitCode);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Tool failed: {Path.GetFileName(exe)} (exit {p.ExitCode})");
    }

    private static void CopyDirectory(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);

        foreach (var file in Directory.GetFiles(srcDir))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(dstDir, name), true);
        }

        foreach (var dir in Directory.GetDirectories(srcDir))
        {
            var name = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(dstDir, name));
        }
    }
}

