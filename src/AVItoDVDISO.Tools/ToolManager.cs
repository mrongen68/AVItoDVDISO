using System.Diagnostics;
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
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AVItoDVDISO/1.0 (+https://github.com/mrongen68/AVItoDVDISO)");
    }

    public bool HasAllRequiredTools(string toolsDir, bool needDvdauthor, bool needXorriso)
    {
        if (!File.Exists(Path.Combine(toolsDir, "ffmpeg.exe"))) return false;
        if (!File.Exists(Path.Combine(toolsDir, "ffprobe.exe"))) return false;

        if (needDvdauthor && !File.Exists(Path.Combine(toolsDir, "dvdauthor.exe"))) return false;
        if (needXorriso && !File.Exists(Path.Combine(toolsDir, "xorriso.exe"))) return false;

        return true;
    }

    public async Task<ToolStatus> EnsureToolsAsync(
        string toolsDir,
        bool needDvdauthor,
        bool needXorriso,
        Action<string> log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(toolsDir);

        // ffmpeg/ffprobe should already be present from our build step,
        // but we keep checks here for completeness.
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

        if (needXorriso && !File.Exists(Path.Combine(toolsDir, "xorriso.exe")))
        {
            log("xorriso.exe missing, downloading...");
            var ok = await DownloadXorrisoAsync(toolsDir, log, ct).ConfigureAwait(false);
            if (!ok)
            {
                return new ToolStatus
                {
                    Ok = false,
                    MissingTool = "xorriso",
                    Message = "Failed to download xorriso.exe."
                };
            }
        }

        return new ToolStatus { Ok = true, Message = "All required tools are present." };
    }

    private async Task<bool> DownloadDvdauthorAsync(string toolsDir, Action<string> log, CancellationToken ct)
    {
        // Source: VideoHelp "Dvdauthor_winbin" package (zip contains dvdauthor.exe and related utilities)
        // Note: This is a pragmatic v1 choice. We can later switch to another mirror if needed.
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

    private async Task<bool> DownloadXorrisoAsync(string toolsDir, Action<string> log, CancellationToken ct)
    {
        // Source: PeyTy/xorriso-exe-for-windows (repo zip contains prebuilt xorriso.exe)
        var url = "https://github.com/PeyTy/xorriso-exe-for-windows/archive/refs/heads/master.zip";
        var zipPath = Path.Combine(Path.GetTempPath(), "AVItoDVDISO_xorriso.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), "AVItoDVDISO_xorriso_extract");

        try
        {
            await DownloadFileAsync(url, zipPath, log, ct).ConfigureAwait(false);

            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            ZipFile.ExtractToDirectory(zipPath, extractDir, true);

            var exe = Directory.GetFiles(extractDir, "xorriso.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null)
            {
                log("xorriso.exe not found inside downloaded zip.");
                return false;
            }

            File.Copy(exe, Path.Combine(toolsDir, "xorriso.exe"), true);
            log("xorriso.exe installed.");
            return true;
        }
        catch (Exception ex)
        {
            log("xorriso download failed: " + ex.Message);
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
