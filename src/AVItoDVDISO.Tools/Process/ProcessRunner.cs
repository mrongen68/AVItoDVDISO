# ---------------------------------
# File: src\AVItoDVDISO.Tools\Process\ProcessRunner.cs
# ---------------------------------
using System.Diagnostics;
using AVItoDVDISO.Core.Services;

namespace AVItoDVDISO.Tools.Process;

public sealed class ProcessRunner
{
    private readonly LogBuffer _log;

    public ProcessRunner(LogBuffer log)
    {
        _log = log;
    }

    public async Task<int> RunAsync(string exePath, string args, string? workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            WorkingDirectory = workingDir ?? "",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _log.Add($"RUN: {exePath} {args}");

        using var p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        p.OutputDataReceived += (_, e) => { if (e.Data is not null) _log.Add(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) _log.Add(e.Data); };

        ct.Register(() =>
        {
            try
            {
                if (!p.HasExited)
                {
                    _log.Add("Cancel requested, killing process.");
                    p.Kill(entireProcessTree: true);
                }
            }
            catch { }
        });

        p.Exited += (_, _) =>
        {
            try { tcs.TrySetResult(p.ExitCode); }
            catch { tcs.TrySetResult(-1); }
        };

        if (!p.Start())
            throw new InvalidOperationException("Failed to start process.");

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        var exitCode = await tcs.Task.ConfigureAwait(false);
        _log.Add($"EXIT: {exitCode}");
        return exitCode;
    }
}