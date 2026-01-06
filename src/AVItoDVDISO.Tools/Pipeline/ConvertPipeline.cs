using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        if (req.Preset is null) throw new InvalidOperationException("Preset missing.");

        ct.ThrowIfCancellationRequested();

        var wd = req.WorkingDir;
        var toolsDir = req.ToolsDir;

        Directory.CreateDirectory(wd);

        var jobDir = PrepareJobDir(wd);
        Directory.CreateDirectory(jobDir);

        var transcodeDir = Path.Combine(jobDir, "transcoded");
        var dvdRootDir = Path.Combine(jobDir, "dvdroot");
        var videoTsDir = Path.Combine(dvdRootDir, "VIDEO_TS");

        Directory.CreateDirectory(transcodeDir);
        Directory.CreateDirectory(videoTsDir);

        var runner = new ProcessRunner(_log);

        var ffmpeg = new FfmpegService(_log, runner, toolsDir);
        var dvdauthor = new DvdauthorService(_log, runner, toolsDir);
        var xorriso = new XorrisoService(_log, runner, toolsDir);

        onProgress(MakeProgress("Prepare", "Preparing job folders", 1));

        var dvdMode = req.Dvd.Mode;
        var aspect = req.Dvd.Aspect;

        var chapterMode = req.Dvd.ChaptersMode;
        var chapterEveryMin = Math.Clamp(req.Dvd.ChaptersEveryMinutes, 1, 60);

        var mpegFiles = new List<string>();

        for (var i = 0; i < req.Sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var src = req.Sources[i];
            if (src is null || string.IsNullOrWhiteSpace(src.Path))
                throw new InvalidOperationException("Source path missing.");

            if (!File.Exists(src.Path))
                throw new FileNotFoundException("Source file not found.", src.Path);

            var baseName = SafeFileStem(Path.GetFileNameWithoutExtension(src.Path));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = $"input_{i + 1:D2}";

            var outMpeg = Path.Combine(transcodeDir, $"{baseName}.mpg");

            var pct = 5 + (int)Math.Round((i / Math.Max(1.0, req.Sources.Count)) * 45.0);
            onProgress(MakeProgress("Transcode", $"Transcoding {Path.GetFileName(src.Path)}", pct));

            await ffmpeg.TranscodeToDvdMpegAsync(
                inputPath: src.Path,
                outputMpegPath: outMpeg,
                mode: dvdMode,
                aspect: aspect,
                preset: req.Preset,
                ct: ct);

            if (!File.Exists(outMpeg))
                throw new IOException($"FFmpeg did not produce expected output: {outMpeg}");

            mpegFiles.Add(outMpeg);
        }

        ct.ThrowIfCancellationRequested();

        onProgress(MakeProgress("Author", "Creating DVD structure (VIDEO_TS)", 60));

        await dvdauthor.AuthorDvdAsync(
            mpegFiles: mpegFiles,
            dvdRootDir: dvdRootDir,
            chaptersMode: chapterMode,
            chaptersEveryMinutes: chapterEveryMin,
            ct: ct);

        if (!Directory.Exists(videoTsDir))
            throw new IOException("DVD authoring failed: VIDEO_TS folder not found.");

        onProgress(MakeProgress("Validate", "Validating VIDEO_TS folder", 70));

        ValidateVideoTs(videoTsDir);

        var exportedVideoTs = (string?)null;

        if (req.Output.ExportFolder)
        {
            ct.ThrowIfCancellationRequested();

            var exportFolder = EnsureOutputFolder(req.Output.OutputPath);
            var target = Path.Combine(exportFolder, "VIDEO_TS");

            onProgress(MakeProgress("Export", $"Exporting VIDEO_TS to {target}", 80));

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);

            CopyDirectory(videoTsDir, target);

            exportedVideoTs = target;
        }

        if (req.Output.ExportIso)
        {
            ct.ThrowIfCancellationRequested();

            var exportFolder = EnsureOutputFolder(req.Output.OutputPath);
            var isoPath = Path.Combine(exportFolder, $"{req.Output.DiscLabel}.iso");

            onProgress(MakeProgress("ISO", "Creating ISO image", 90));

            var imgburnPath = FindImgBurnExe(toolsDir);

            if (!string.IsNullOrWhiteSpace(imgburnPath))
            {
                await CreateIsoWithImgBurnAsync(
                    runner: runner,
                    imgburnExe: imgburnPath,
                    dvdRootDir: dvdRootDir,
                    isoPath: isoPath,
                    discLabel: req.Output.DiscLabel,
                    ct: ct);
            }
            else
            {
                await xorriso.CreateDvdIsoAsync(
                    dvdRootDir: dvdRootDir,
                    isoPath: isoPath,
                    discLabel: req.Output.DiscLabel,
                    ct: ct);
            }

            if (!File.Exists(isoPath))
                throw new IOException($"ISO creation failed. ISO not found: {isoPath}");
        }

        onProgress(MakeProgress("Done", "Completed", 100));

        if (!string.IsNullOrWhiteSpace(exportedVideoTs))
            _log.Line($"Exported VIDEO_TS to: {exportedVideoTs}");
    }

    private static ConvertProgress MakeProgress(string stage, string message, int percent)
        => new ConvertProgress { Stage = stage, Message = message, Percent = Math.Clamp(percent, 0, 100) };

    private static string PrepareJobDir(string workingDir)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(workingDir, $"job_{stamp}");
    }

    private static string EnsureOutputFolder(string outputPath)
    {
        var p = outputPath;
        if (string.IsNullOrWhiteSpace(p))
            p = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        Directory.CreateDirectory(p);
        return p;
    }

    private static string SafeFileStem(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "output";
        foreach (var ch in Path.GetInvalidFileNameChars())
            s = s.Replace(ch, '_');
        return s.Trim();
    }

    private static void ValidateVideoTs(string videoTsDir)
    {
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

    private static string? FindImgBurnExe(string toolsDir)
    {
        var candidates = new[]
        {
            Path.Combine(toolsDir, "ImgBurn.exe"),
            Path.Combine(toolsDir, "imgburn.exe"),
            Path.Combine(toolsDir, "ImgBurn", "ImgBurn.exe"),
            Path.Combine(toolsDir, "imgburn", "ImgBurn.exe")
        };

        foreach (var c in candidates)
            if (File.Exists(c))
                return c;

        return null;
    }

    private async Task CreateIsoWithImgBurnAsync(
        ProcessRunner runner,
        string imgburnExe,
        string dvdRootDir,
        string isoPath,
        string discLabel,
        CancellationToken ct)
    {
        if (File.Exists(isoPath))
            File.Delete(isoPath);

        var args =
            $"/MODE BUILD " +
            $"/BUILDINPUTMODE STANDARD " +
            $"/BUILDTYPE IMAGEFILE " +
            $"/SRC \"{dvdRootDir}\" " +
            $"/DEST \"{isoPath}\" " +
            $"/VOLUMELABEL \"{discLabel}\" " +
            $"/FILESYSTEM \"UDF\" " +
            $"/UDFREVISION \"1.02\" " +
            $"/START " +
            $"/CLOSE";

        _log.Line($"ISO (ImgBurn): \"{imgburnExe}\" {args}");

        var result = await runner.RunAsync(
            exePath: imgburnExe,
            arguments: args,
            workingDirectory: Path.GetDirectoryName(isoPath) ?? Environment.CurrentDirectory,
            ct: ct);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ImgBurn failed with exit code {result.ExitCode}. See log for details.");
    }
}
