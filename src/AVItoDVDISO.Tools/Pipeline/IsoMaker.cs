using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace AVItoDVDISO_Release
{
    internal static class IsoMaker
    {
        // Create ISO from DVD folder using ImgBurn.
        // dvdRootFolder must contain VIDEO_TS (and optionally AUDIO_TS).
        // Returns 0 on success, throws on failure unless throwOnError=false.
        public static int CreateIsoWithImgBurn(
            string imgBurnExePath,
            string dvdRootFolder,
            string isoOutputPath,
            string volumeLabel,
            Action<string>? log = null,
            bool throwOnError = true)
        {
            if (string.IsNullOrWhiteSpace(imgBurnExePath))
                throw new ArgumentException("ImgBurn path is empty.", nameof(imgBurnExePath));

            if (!File.Exists(imgBurnExePath))
                throw new FileNotFoundException("ImgBurn.exe not found.", imgBurnExePath);

            if (string.IsNullOrWhiteSpace(dvdRootFolder))
                throw new ArgumentException("DVD root folder is empty.", nameof(dvdRootFolder));

            if (!Directory.Exists(dvdRootFolder))
                throw new DirectoryNotFoundException($"DVD root folder not found: {dvdRootFolder}");

            var videoTs = Path.Combine(dvdRootFolder, "VIDEO_TS");
            if (!Directory.Exists(videoTs))
                throw new DirectoryNotFoundException($"VIDEO_TS folder not found under dvd root: {videoTs}");

            if (string.IsNullOrWhiteSpace(isoOutputPath))
                throw new ArgumentException("ISO output path is empty.", nameof(isoOutputPath));

            var outDir = Path.GetDirectoryName(isoOutputPath);
            if (string.IsNullOrWhiteSpace(outDir))
                throw new ArgumentException("ISO output directory could not be determined.", nameof(isoOutputPath));

            Directory.CreateDirectory(outDir);

            if (string.IsNullOrWhiteSpace(volumeLabel))
                volumeLabel = "AVITODVD";

            volumeLabel = SanitizeVolumeLabel(volumeLabel);

            // ImgBurn CLI
            // - MODE BUILD: build project
            // - BUILDMODE IMAGEFILE: output ISO image
            // - FILESYSTEM ISO9660+UDF, UDF 1.02 for DVD-Video compatibility
            // - START/CLOSE: run immediately and close
            // - NOIMAGEDETAILS / NOLOGO / QUIET reduces UI chatter (still may show minimal UI depending on settings)
            // - OPERATION: building ISO from folder
            var args = new StringBuilder();
            args.Append("/MODE BUILD ");
            args.Append("/BUILDMODE IMAGEFILE ");
            args.Append("/SRC ").Append(Quote(dvdRootFolder)).Append(' ');
            args.Append("/DEST ").Append(Quote(isoOutputPath)).Append(' ');
            args.Append("/FILESYSTEM \"ISO9660 + UDF\" ");
            args.Append("/UDFREVISION 1.02 ");
            args.Append("/VOLUMELABEL ").Append(Quote(volumeLabel)).Append(' ');
            args.Append("/START ");
            args.Append("/CLOSE ");
            args.Append("/NOIMAGEDETAILS ");
            args.Append("/NOLOGO ");

            // Prefer not to use /QUIET if you rely on visible progress. If you want quiet runs, enable it:
            // args.Append("/QUIET ");

            log?.Invoke($"ISO (ImgBurn): {imgBurnExePath} {args}");

            var psi = new ProcessStartInfo
            {
                FileName = imgBurnExePath,
                Arguments = args.ToString(),
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
                log?.Invoke(e.Data);
            };

            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stderr.AppendLine(e.Data);
                log?.Invoke(e.Data);
            };

            try
            {
                if (!p.Start())
                {
                    var msg = "Failed to start ImgBurn process.";
                    if (throwOnError) throw new InvalidOperationException(msg);
                    log?.Invoke(msg);
                    return -1;
                }

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                p.WaitForExit();

                var exitCode = p.ExitCode;

                // ImgBurn typically returns 0 on success. Non-zero indicates failure.
                if (exitCode != 0)
                {
                    var msg =
                        "ImgBurn failed to create ISO.\n" +
                        $"Exit code: {exitCode}\n" +
                        $"dvdRoot: {dvdRootFolder}\n" +
                        $"isoOut: {isoOutputPath}\n" +
                        "Captured output:\n" +
                        stdout.ToString() +
                        "\nCaptured error:\n" +
                        stderr.ToString();

                    if (throwOnError) throw new InvalidOperationException(msg);
                    log?.Invoke(msg);
                }
                else
                {
                    if (!File.Exists(isoOutputPath))
                    {
                        var msg = $"ImgBurn reported success, but ISO was not found: {isoOutputPath}";
                        if (throwOnError) throw new FileNotFoundException(msg, isoOutputPath);
                        log?.Invoke(msg);
                        return -2;
                    }

                    log?.Invoke($"ISO created: {isoOutputPath}");
                }

                return exitCode;
            }
            catch (Exception ex)
            {
                var msg = "Exception while running ImgBurn: " + ex.Message;
                if (throwOnError) throw;
                log?.Invoke(msg);
                return -3;
            }
        }

        // Helper: Find ImgBurn in your tools folder first; fallback to Program Files installs.
        // Adjust these paths to match your app layout if needed.
        public static string ResolveImgBurnPath(string toolsFolder, Action<string>? log = null)
        {
            // 1) Local tools folder (recommended: ship ImgBurn.exe alongside your app)
            var local = Path.Combine(toolsFolder, "ImgBurn.exe");
            if (File.Exists(local))
            {
                log?.Invoke($"ImgBurn resolved (tools): {local}");
                return local;
            }

            // 2) Typical install locations
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var candidates = new[]
            {
                Path.Combine(pfX86, "ImgBurn", "ImgBurn.exe"),
                Path.Combine(pf, "ImgBurn", "ImgBurn.exe"),
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    log?.Invoke($"ImgBurn resolved (installed): {c}");
                    return c;
                }
            }

            throw new FileNotFoundException(
                "ImgBurn.exe not found. Place ImgBurn.exe in your tools folder or install ImgBurn and ensure it is in a standard location.",
                local);
        }

        private static string Quote(string s)
        {
            if (s.Contains("\"", StringComparison.Ordinal))
                s = s.Replace("\"", "\\\"", StringComparison.Ordinal);
            return "\"" + s + "\"";
        }

        private static string SanitizeVolumeLabel(string label)
        {
            // Keep DVD volume label conservative:
            // uppercase, max 32 chars, only A-Z 0-9 _ and space.
            label = label.Trim();
            if (label.Length == 0) return "AVITODVD";

            label = label.ToUpperInvariant();

            var sb = new StringBuilder(label.Length);
            foreach (var ch in label)
            {
                if ((ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9') ||
                    ch == '_' ||
                    ch == ' ')
                {
                    sb.Append(ch);
                }
            }

            var cleaned = sb.ToString().Trim();
            if (cleaned.Length == 0) cleaned = "AVITODVD";
            if (cleaned.Length > 32) cleaned = cleaned.Substring(0, 32);

            return cleaned;
        }
    }
}
