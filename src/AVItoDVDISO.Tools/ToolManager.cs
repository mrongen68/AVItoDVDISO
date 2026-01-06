using System.IO.Compression;
using System.Net.Http;

namespace AVItoDVDISO.Tools;

public sealed class ToolManager
{
    public sealed class ToolStatus
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public string? MissingTool { get; init; }
    }

    private readonly HttpClient _http;

    public ToolManager()
    {
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AVItoDVDISO/1.0 (+https://github.com/mrongen68/AVItoDVDISO)");
    }

    public bool HasAllRequiredTools(string toolsDir, bool needDvdauthor, bool needIsoTool)
    {
        if (!File.Exists(Path.Combine(toolsDir, "ffmpeg.exe"))) return false;
        if (!File.Exists(Path.Combine(toolsDir, "ffprobe.exe"))) return false;

        if (needDvdauthor && !File.Exists(Path.Combine(toolsDir, "dvdauthor.exe"))) return false;
        if (needIsoTool && !File.Exists(Path.Combine(toolsDir, "mkisofs.exe"))) return false;

        return true;
    }

    public async Task<ToolStatus> EnsureToolsAsync(
        string toolsDir,
        bool needDvdauthor,
        bool needIsoTool,
        Action<string> log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(toolsDir);

        if (!File.Exists(Path.Combine(toolsDir, "ffmpeg.exe")) || !File.Exists(Path.Combine(toolsDir, "ffprobe.exe")))
        {
            return new ToolStatus
            {
                Ok = false,
                MissingTool = "ffmpeg/ffprobe",
                Message = "ffmpeg/ffprobe not found in tools folder."
            };
        }

        if (needDvdauthor && !File.Exists(Path.Combine(toolsDir, "dvdauthor.exe")))
        {
            log("dvdauthor.exe missing, downloading...");
            var ok = await DownloadDvdauthorAsync(toolsDir, log, ct).ConfigureAwait(false);
            if (!ok)
            {
                return new ToolStatus
                {
                    Ok = false,
                    MissingTool = "dvdauthor",
                    Message = "Failed to download dvdauthor.exe."
                };
            }
        }

        if (needIsoTool && !File.Exists(Path.Combine(toolsDir, "mkisofs.exe")))
        {
            log("mkisofs.exe missing, downloading...");
            var ok = await DownloadMkisofsAsync(toolsDir, log, ct).ConfigureAwait(false);
            if (!ok)
            {
                return new ToolStatus
                {
                    Ok = false,
                    MissingTool = "mkisofs",
                    Message = "Failed to download mkisofs.exe."
                };
            }
        }

        return new ToolStatus { Ok = true, Message = "All required tools are present." };
    }

    private async Task<bool> DownloadDvdauthorAsync(string toolsDir, Action<string> log, CancellationToken ct)
    {
        // VideoHelp zip, contains dvdauthor.exe
        var url = "https://download.videohelp.com/gfd/edcounter.php?file=download%2Fdvdauthor_winbin.zip";
        var zipPath = Path.Combine(Path.GetTempPath(), "AVItoDVDISO_dvdauthor.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), "AVItoDVDISO_dvdauthor_extract");

        try
        {
            await DownloadFileAsync(url, zipPath, log, ct).ConfigureAwait(false);

            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            ZipFile.ExtractToDirectory(zipPath, extractDir, true);

            var exe = Directory.GetFiles(extractDir, "dvdauthor.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null)
            {
                log("dvdauthor.exe not found inside downloaded zip.");
                return false;
            }

            File.Copy(exe, Path.Combine(toolsDir, "dvdauthor.exe"), true);
            log("dvdauthor.exe installed.");
            return true;
        }
        catch (Exception ex)
        {
            log("dvdauthor download failed: " + ex.Message);
            return false;
        }
        finally
        {
            SafeDelete(zipPath);
            SafeDeleteDir(extractDir);
        }
    }

    private async Task<bool> DownloadMkisofsAsync(string toolsDir, Action<string> log, CancellationToken ct)
    {
        // SourceForge cdrtools win32 binary zip (contains mkisofs.exe).
        // mkisofs is the ISO builder we will use instead of xorriso. :contentReference[oaicite:1]{index=1}
        var url = "https://sourceforge.net/projects/cdrtools/files/alpha/win32/cdrtools-1.11a12-win32-bin.zip/download";
        var zipPath = Path.Combine(Path.GetTempPath(), "AVItoDVDISO_cdrtools.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), "AVItoDVDISO_cdrtools_extract");

        try
        {
            await DownloadFileAsync(url, zipPath, log, ct).ConfigureAwait(false);

            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            ZipFile.ExtractToDirectory(zipPath, extractDir, true);

            var exe = Directory.GetFiles(extractDir, "mkisofs.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null)
            {
                log("mkisofs.exe not found inside downloaded zip.");
                return false;
            }

            File.Copy(exe, Path.Combine(toolsDir, "mkisofs.exe"), true);
            log("mkisofs.exe installed.");
            return true;
        }
        catch (Exception ex)
        {
            log("mkisofs download failed: " + ex.Message);
            return false;
        }
        finally
        {
            SafeDelete(zipPath);
            SafeDeleteDir(extractDir);
        }
    }

    private async Task DownloadFileAsync(string url, string outPath, Action<string> log, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);

        log("Downloaded: " + url);
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void SafeDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
