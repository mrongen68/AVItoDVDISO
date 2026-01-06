using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace AVItoDVDISO_Release
{
    internal sealed class ConvertPipeline
    {
        public sealed class Settings
        {
            // Input
            public string InputVideoPath { get; set; } = "";

            // Output
            public string OutputFolder { get; set; } = "";
            public string OutputBaseName { get; set; } = "AVITODVD";
            public bool ExportVideoTsFolder { get; set; } = true;
            public bool CreateIsoImage { get; set; } = true;

            // Volume label (ISO)
            public string VolumeLabel { get; set; } = "AVITODVD";

            // Tools
            public string ToolsFolder { get; set; } = "";
            public string DvdAuthorExeName { get; set; } = "dvdauthor.exe";
            public string MkisofsExeName { get; set; } = "mkisofs.exe"; // kept only for legacy compatibility, not used when CreateIsoImage uses ImgBurn
            public string ImgBurnExeName { get; set; } = "ImgBurn.exe";

            // Working folder
            public string WorkRootFolder { get; set; } = ""; // if empty, uses %LOCALAPPDATA%\Temp\AVItoDVDISO\<jobId>\

            // Optional: dvd folder name inside work root
            public string DvdRootFolderName { get; set; } = "dvdroot";

            // Optional: dvd title set name
            public string DvdTitle { get; set; } = "DVD";
        }

        public sealed class Result
        {
            public bool Success { get; set; }
            public string JobFolder { get; set; } = "";
            public string DvdRootFolder { get; set; } = "";
            public string VideoTsFolder { get; set; } = "";
            public string? ExportedVideoTsFolder { get; set; }
            public string? IsoPath { get; set; }
            public string? ErrorMessage { get; set; }
        }

        private readonly Action<string> _log;

        public ConvertPipeline(Action<string> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public Result Run(Settings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var res = new Result();

            try
            {
                ValidateSettings(settings);

                var jobId = "job_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var jobFolder = ResolveJobFolder(settings, jobId);
                Directory.CreateDirectory(jobFolder);

                res.JobFolder = jobFolder;

                var dvdRoot = Path.Combine(jobFolder, settings.DvdRootFolderName);
                Directory.CreateDirectory(dvdRoot);

                res.DvdRootFolder = dvdRoot;

                var videoTs = Path.Combine(dvdRoot, "VIDEO_TS");
                Directory.CreateDirectory(videoTs);

                res.VideoTsFolder = videoTs;

                _log($"JOB: {jobId}");
                _log($"Input: {settings.InputVideoPath}");
                _log($"Work: {jobFolder}");
                _log($"DVD root: {dvdRoot}");
                _log($"Output folder: {settings.OutputFolder}");
                _log($"Create ISO: {settings.CreateIsoImage}");
                _log($"Export VIDEO_TS: {settings.ExportVideoTsFolder}");

                // =========================================================================
                // STEP A: Create VIDEO_TS (DVD-Video structure)
                // =========================================================================
                // This pipeline expects that a valid VIDEO_TS is created under dvdRoot.
                // If you already generate VIDEO_TS elsewhere, keep that code and remove
                // the placeholder below. The log you pasted shows dvdauthor already runs.
                //
                // Here we include a "dvdauthor" call pattern that is typical:
                //   1) dvdauthor -o <dvdRoot> -t <mpegFile>
                //   2) dvdauthor -o <dvdRoot> -T
                //
                // NOTE: You must provide an MPEG2 file compatible with DVD-Video.
                // If your app creates that earlier, set mpegPath to it.
                // =========================================================================

                // Placeholder path for MPEG2 source created earlier in your process
                // If your project already has a known location, replace this with that variable.
                var mpegPath = Path.Combine(jobFolder, "dvd.mpg");
                if (!File.Exists(mpegPath))
                {
                    // If your pipeline generates VIDEO_TS elsewhere, you can remove this check.
                    // For safety, we stop here because dvdauthor needs a valid MPEG input.
                    throw new FileNotFoundException(
                        "Expected MPEG2 file not found. Ensure your pipeline creates a DVD-compatible MPEG2 file at: " + mpegPath,
                        mpegPath);
                }

                var dvdauthorExe = ResolveTool(settings.ToolsFolder, settings.DvdAuthorExeName);
                RunProcess(
                    dvdauthorExe,
                    $"-o {Quote(dvdRoot)} -t {Quote(mpegPath)}",
                    workingDir: jobFolder,
                    logPrefix: "dvdauthor",
                    throwOnNonZeroExit: true);

                RunProcess(
                    dvdauthorExe,
                    $"-o {Quote(dvdRoot)} -T",
                    workingDir: jobFolder,
                    logPrefix: "dvdauthor",
                    throwOnNonZeroExit: true);

                // Verify VIDEO_TS looks populated
                EnsureVideoTsLooksValid(videoTs);

                // =========================================================================
                // STEP B: Export VIDEO_TS folder to output (optional)
                // =========================================================================
                if (settings.ExportVideoTsFolder)
                {
                    var exportVideoTs = Path.Combine(settings.OutputFolder, "VIDEO_TS");
                    Directory.CreateDirectory(settings.OutputFolder);

                    _log($"Exported VIDEO_TS to: {exportVideoTs}");
                    CopyDirectory(videoTs, exportVideoTs, overwrite: true);

                    res.ExportedVideoTsFolder = exportVideoTs;
                }

                // =========================================================================
                // STEP C: Create ISO (ImgBurn only)
                // =========================================================================
                if (settings.CreateIsoImage)
                {
                    Directory.CreateDirectory(settings.OutputFolder);

                    var isoPath = Path.Combine(settings.OutputFolder, settings.OutputBaseName + ".iso");
                    if (File.Exists(isoPath))
                    {
                        try
                        {
                            File.Delete(isoPath);
                        }
                        catch
                        {
                            // If delete fails, pick a new name
                            isoPath = Path.Combine(settings.OutputFolder, settings.OutputBaseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".iso");
                        }
                    }

                    // Prefer tools folder ImgBurn.exe, otherwise standard install location
                    var imgBurnPath = ResolveImgBurn(settings.ToolsFolder, settings.ImgBurnExeName);

                    var exit = IsoMaker.CreateIsoWithImgBurn(
                        imgBurnExePath: imgBurnPath,
                        dvdRootFolder: dvdRoot,
                        isoOutputPath: isoPath,
                        volumeLabel: settings.VolumeLabel,
                        log: _log,
                        throwOnError: true
                    );

                    _log($"ISO exit code: {exit}");
                    res.IsoPath = isoPath;
                }

                res.Success = true;
                return res;
            }
            catch (Exception ex)
            {
                res.Success = false;
                res.ErrorMessage = ex.ToString();
                _log("ERROR: " + ex.Message);
                _log(ex.ToString());
                return res;
            }
        }

        private static void ValidateSettings(Settings s)
        {
            if (string.IsNullOrWhiteSpace(s.InputVideoPath))
                throw new ArgumentException("InputVideoPath is empty.", nameof(s));

            if (!File.Exists(s.InputVideoPath))
                throw new FileNotFoundException("Input video not found.", s.InputVideoPath);

            if (string.IsNullOrWhiteSpace(s.OutputFolder))
                throw new ArgumentException("OutputFolder is empty.", nameof(s));

            if (string.IsNullOrWhiteSpace(s.ToolsFolder))
                throw new ArgumentException("ToolsFolder is empty.", nameof(s));

            if (!Directory.Exists(s.ToolsFolder))
                throw new DirectoryNotFoundException("ToolsFolder not found: " + s.ToolsFolder);

            if (string.IsNullOrWhiteSpace(s.OutputBaseName))
                s.OutputBaseName = "AVITODVD";

            if (string.IsNullOrWhiteSpace(s.VolumeLabel))
                s.VolumeLabel = "AVITODVD";

            if (string.IsNullOrWhiteSpace(s.DvdRootFolderName))
                s.DvdRootFolderName = "dvdroot";
        }

        private static string ResolveJobFolder(Settings s, string jobId)
        {
            if (!string.IsNullOrWhiteSpace(s.WorkRootFolder))
                return Path.Combine(s.WorkRootFolder, jobId);

            var baseTemp = Path.GetTempPath();
            return Path.Combine(baseTemp, "AVItoDVDISO", jobId);
        }

        private static string ResolveTool(string toolsFolder, string exeName)
        {
            var p = Path.Combine(toolsFolder, exeName);
            if (!File.Exists(p))
                throw new FileNotFoundException("Tool not found: " + p, p);
            return p;
        }

        private static string ResolveImgBurn(string toolsFolder, string exeName)
        {
            // 1) local tools folder (preferred)
            var local = Path.Combine(toolsFolder, exeName);
            if (File.Exists(local))
                return local;

            // 2) standard installs
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var c1 = Path.Combine(pfX86, "ImgBurn", "ImgBurn.exe");
            if (File.Exists(c1)) return c1;

            var c2 = Path.Combine(pf, "ImgBurn", "ImgBurn.exe");
            if (File.Exists(c2)) return c2;

            throw new FileNotFoundException("ImgBurn.exe not found. Place ImgBurn.exe in tools folder or install ImgBurn.");
        }

        private void RunProcess(
            string exePath,
            string arguments,
            string workingDir,
            string logPrefix,
            bool throwOnNonZeroExit)
        {
            _log($"{logPrefix}: {exePath} {arguments}");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = new Process { StartInfo = psi };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stdout.AppendLine(e.Data);
                _log($"{logPrefix}: {e.Data}");
            };

            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stderr.AppendLine(e.Data);
                _log($"{logPrefix} ERR: {e.Data}");
            };

            if (!p.Start())
                throw new InvalidOperationException("Failed to start process: " + exePath);

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            p.WaitForExit();

            var exit = p.ExitCode;
            _log($"{logPrefix} exit code: {exit}");

            if (throwOnNonZeroExit && exit != 0)
            {
                throw new InvalidOperationException(
                    $"{logPrefix} failed.\nExit code: {exit}\nCommand: {exePath} {arguments}\nOutput:\n{stdout}\nError:\n{stderr}");
            }
        }

        private static void EnsureVideoTsLooksValid(string videoTsFolder)
        {
            if (!Directory.Exists(videoTsFolder))
                throw new DirectoryNotFoundException("VIDEO_TS not found: " + videoTsFolder);

            var ifoFiles = Directory.GetFiles(videoTsFolder, "*.IFO", SearchOption.TopDirectoryOnly);
            var vobFiles = Directory.GetFiles(videoTsFolder, "*.VOB", SearchOption.TopDirectoryOnly);

            if (ifoFiles.Length == 0 || vobFiles.Length == 0)
            {
                throw new InvalidOperationException(
                    "VIDEO_TS folder does not look like a valid DVD-Video structure. Missing IFO and/or VOB files: " + videoTsFolder);
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir, bool overwrite)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var dest = Path.Combine(targetDir, name);
                File.Copy(file, dest, overwrite);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                var dest = Path.Combine(targetDir, name);
                CopyDirectory(dir, dest, overwrite);
            }
        }

        private static string Quote(string s)
        {
            if (s.Contains("\"", StringComparison.Ordinal))
                s = s.Replace("\"", "\\\"", StringComparison.Ordinal);
            return "\"" + s + "\"";
        }
    }
}
