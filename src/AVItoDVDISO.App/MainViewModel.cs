using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AVItoDVDISO.Core.Models;
using AVItoDVDISO.Core.Services;
using AVItoDVDISO.Tools.Pipeline;
using System.IO;
using System.Linq;

namespace AVItoDVDISO.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ObservableCollection<SourceItem> Sources { get; } = new();

    private SourceItem? _selectedSource;
    public SourceItem? SelectedSource
    {
        get => _selectedSource;
        set { _selectedSource = value; Raise(); }
    }

    public ObservableCollection<string> DvdModes { get; } = new() { "PAL", "NTSC" };
    public ObservableCollection<string> AspectModes { get; } = new() { "Auto", "16:9", "4:3" };

    public ObservableCollection<PresetDefinition> Presets { get; } = new();

    private PresetDefinition? _selectedPreset;
    public PresetDefinition? SelectedPreset
    {
        get => _selectedPreset;
        set { _selectedPreset = value; Raise(); }
    }

    private string _selectedDvdMode = "PAL";
    public string SelectedDvdMode { get => _selectedDvdMode; set { _selectedDvdMode = value; Raise(); } }

    private string _selectedAspectMode = "Auto";
    public string SelectedAspectMode { get => _selectedAspectMode; set { _selectedAspectMode = value; Raise(); } }

    private bool _chaptersEnabled;
    public bool ChaptersEnabled { get => _chaptersEnabled; set { _chaptersEnabled = value; Raise(); } }

    private int _chaptersMinutes = 5;
    public int ChaptersMinutes { get => _chaptersMinutes; set { _chaptersMinutes = value; Raise(); } }

    private bool _exportFolder = true;
    public bool ExportFolder { get => _exportFolder; set { _exportFolder = value; Raise(); Raise(nameof(CanConvert)); } }

    private bool _exportIso = true;
    public bool ExportIso { get => _exportIso; set { _exportIso = value; Raise(); Raise(nameof(CanConvert)); } }

    private string _outputPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public string OutputPath { get => _outputPath; set { _outputPath = value; Raise(); } }

    private string _discLabel = "AVITODVD";
    public string DiscLabel { get => _discLabel; set { _discLabel = value; Raise(); } }

    private string _statusText = "Idle";
    public string StatusText { get => _statusText; set { _statusText = value; Raise(); } }

    private int _overallProgress;
    public int OverallProgress { get => _overallProgress; set { _overallProgress = value; Raise(); } }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set { _isRunning = value; Raise(); Raise(nameof(CanConvert)); } }

    public bool CanConvert => !IsRunning && Sources.Count > 0 && (ExportFolder || ExportIso);

    private string _logText = "";
    public string LogText { get => _logText; set { _logText = value; Raise(); } }

    public string TotalDurationText
    {
        get
        {
            var total = TimeSpan.FromSeconds(Sources.Sum(s => s.Duration.TotalSeconds));
            return $"Total duration: {total}";
        }
    }

    public string WorkingDir => Path.Combine(Path.GetTempPath(), "AVItoDVDISO");

    private CancellationTokenSource? _cts;
    private PresetsRoot? _presetsRoot;

    public void Initialize()
    {
        var presetsPath = Path.Combine(AppContext.BaseDirectory, "presets", "presets.json");
        var presetsService = new PresetsService(presetsPath);
        _presetsRoot = presetsService.Load();

        Presets.Clear();
        foreach (var p in _presetsRoot.Presets)
            Presets.Add(p);

        SelectedPreset = Presets.FirstOrDefault(p => p.Id == _presetsRoot.Defaults.PresetId) ?? Presets.FirstOrDefault();

        SelectedDvdMode = _presetsRoot.Defaults.DvdMode;
        SelectedAspectMode = _presetsRoot.Defaults.Aspect;

        ExportFolder = _presetsRoot.Defaults.ExportFolder;
        ExportIso = _presetsRoot.Defaults.ExportIso;

        DiscLabel = _presetsRoot.Defaults.DiscLabel;

        Raise(nameof(CanConvert));
        Raise(nameof(TotalDurationText));
    }

public void AddSource(string path)
{
    var item = new SourceItem { Path = path };
    Sources.Add(item);

    Raise(nameof(CanConvert));
    Raise(nameof(TotalDurationText));

    _ = Task.Run(async () =>
    {
        try
        {
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
            var ffprobePath = Path.Combine(toolsDir, "ffprobe.exe");

            if (!File.Exists(ffprobePath))
                return;

            var meta = await ProbeWithFfprobeAsync(ffprobePath, path).ConfigureAwait(false);

            App.Current.Dispatcher.Invoke(() =>
            {
                item.Duration = meta.Duration;
                item.VideoWidth = meta.Width;
                item.VideoHeight = meta.Height;
                item.Fps = meta.Fps;
                item.HasAudio = meta.HasAudio;
                item.AudioChannels = meta.AudioChannels;
                item.AudioSampleRateHz = meta.AudioSampleRateHz;

                Raise(nameof(TotalDurationText));
            });
        }
        catch
        {
            // ignore probe errors, conversion can still run later
        }
    });
}


    public void RemoveSelected()
    {
        if (SelectedSource is null) return;
        Sources.Remove(SelectedSource);
        SelectedSource = null;
        Raise(nameof(CanConvert));
        Raise(nameof(TotalDurationText));
    }

    public void MoveSelected(int delta)
    {
        if (SelectedSource is null) return;
        var idx = Sources.IndexOf(SelectedSource);
        if (idx < 0) return;

        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= Sources.Count) return;

        Sources.Move(idx, newIdx);
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    public void OpenOutputFolder()
    {
        try
        {
            if (!Directory.Exists(OutputPath)) return;
            Process.Start(new ProcessStartInfo { FileName = OutputPath, UseShellExecute = true });
        }
        catch { }
    }

    public async Task StartConvertAsync()
    {
        if (SelectedPreset is null)
            throw new InvalidOperationException("No preset selected.");

        IsRunning = true;
        StatusText = "Running";
        OverallProgress = 0;

        var log = new LogBuffer();
        log.LineAdded += (line) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                LogText += line + Environment.NewLine;
            });
        };

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var pipeline = new ConvertPipeline(log);

        var dvd = new DvdSettings
        {
            Mode = SelectedDvdMode == "NTSC" ? DvdMode.NTSC : DvdMode.PAL,
            Aspect = SelectedAspectMode switch
            {
                "16:9" => AspectMode.Anamorphic16x9,
                "4:3" => AspectMode.Standard4x3,
                _ => AspectMode.Auto
            },
            ChaptersMode = ChaptersEnabled ? ChapterMode.EveryNMinutes : ChapterMode.Off,
            ChaptersEveryMinutes = Math.Clamp(ChaptersMinutes, 1, 60),
            PresetId = SelectedPreset.Id
        };

        var outSettings = new OutputSettings
        {
            ExportFolder = ExportFolder,
            ExportIso = ExportIso,
            OutputPath = OutputPath,
            DiscLabel = SanitizeDiscLabel(DiscLabel)
        };

        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");

        var req = new ConvertJobRequest
        {
            Sources = Sources.ToList(),
            Dvd = dvd,
            Output = outSettings,
            WorkingDir = WorkingDir,
            ToolsDir = toolsDir,
            Preset = SelectedPreset
        };

        try
        {
            Directory.CreateDirectory(WorkingDir);

            void OnProgress(ConvertProgress p)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    StatusText = $"{p.Stage}: {p.Message}";
                    OverallProgress = p.Percent;
                });
            }

            await pipeline.RunAsync(req, OnProgress, ct);

            StatusText = "Done";
            OverallProgress = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Canceled";
        }
        finally
        {
            IsRunning = false;
            Raise(nameof(CanConvert));
            Raise(nameof(TotalDurationText));
        }
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
    private sealed class ProbeResult
{
    public TimeSpan Duration { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; }
    public bool HasAudio { get; init; }
    public int AudioChannels { get; init; }
    public int AudioSampleRateHz { get; init; }
}

private static async Task<ProbeResult> ProbeWithFfprobeAsync(string ffprobeExe, string inputPath)
{
    // Ask ffprobe for JSON so we can parse duration reliably
    var args =
        "-v error " +
        "-print_format json " +
        "-show_entries format=duration " +
        "-show_entries stream=index,codec_type,width,height,r_frame_rate,channels,sample_rate " +
        $"\"{inputPath}\"";

    var psi = new ProcessStartInfo
    {
        FileName = ffprobeExe,
        Arguments = args,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffprobe.");
    var stdout = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
    _ = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
    await p.WaitForExitAsync().ConfigureAwait(false);

    if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        return new ProbeResult { Duration = TimeSpan.Zero };

    using var doc = System.Text.Json.JsonDocument.Parse(stdout);

    double durationSeconds = 0.0;
    if (doc.RootElement.TryGetProperty("format", out var formatEl) &&
        formatEl.TryGetProperty("duration", out var durEl) &&
        durEl.ValueKind == System.Text.Json.JsonValueKind.String &&
        double.TryParse(durEl.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
    {
        durationSeconds = d;
    }

    int width = 0, height = 0;
    double fps = 0.0;

    bool hasAudio = false;
    int audioChannels = 0;
    int audioSampleRate = 0;

    if (doc.RootElement.TryGetProperty("streams", out var streamsEl) &&
        streamsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
        foreach (var s in streamsEl.EnumerateArray())
        {
            if (!s.TryGetProperty("codec_type", out var ctEl)) continue;
            var ct = ctEl.GetString();

            if (ct == "video")
            {
                if (s.TryGetProperty("width", out var wEl) && wEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                    width = wEl.GetInt32();
                if (s.TryGetProperty("height", out var hEl) && hEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                    height = hEl.GetInt32();

                if (s.TryGetProperty("r_frame_rate", out var rEl) && rEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    fps = ParseFps(rEl.GetString());
                }
            }
            else if (ct == "audio")
            {
                hasAudio = true;

                if (s.TryGetProperty("channels", out var chEl) && chEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                    audioChannels = chEl.GetInt32();

                if (s.TryGetProperty("sample_rate", out var srEl) && srEl.ValueKind == System.Text.Json.JsonValueKind.String &&
                    int.TryParse(srEl.GetString(), out var sr))
                    audioSampleRate = sr;
            }
        }
    }

    return new ProbeResult
    {
        Duration = durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : TimeSpan.Zero,
        Width = width,
        Height = height,
        Fps = fps,
        HasAudio = hasAudio,
        AudioChannels = audioChannels,
        AudioSampleRateHz = audioSampleRate
    };
}

private static double ParseFps(string? rate)
{
    if (string.IsNullOrWhiteSpace(rate)) return 0.0;
    var parts = rate.Split('/');
    if (parts.Length == 2 &&
        double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
        double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) &&
        d != 0.0)
    {
        return n / d;
    }
    if (double.TryParse(rate, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
        return v;
    return 0.0;
}

}


