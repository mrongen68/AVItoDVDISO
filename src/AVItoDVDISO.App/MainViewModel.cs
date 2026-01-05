# ---------------------------------
# File: src\AVItoDVDISO.App\MainViewModel.cs
# ---------------------------------
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AVItoDVDISO.Core.Models;
using AVItoDVDISO.Core.Services;
using AVItoDVDISO.Tools.Pipeline;

namespace AVItoDVDISO.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ObservableCollection<SourceItem> Sources { get; } = new();
    public SourceItem? SelectedSource { get; set; }

    public ObservableCollection<string> DvdModes { get; } = new() { "PAL", "NTSC" };
    public ObservableCollection<string> AspectModes { get; } = new() { "Auto", "16:9", "4:3" };

    public ObservableCollection<PresetDefinition> Presets { get; } = new();
    public PresetDefinition? SelectedPreset { get; set; }

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

    public string WorkingDir => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AVItoDVDISO");

    private CancellationTokenSource? _cts;

    private PresetsRoot? _presetsRoot;

    public void Initialize()
    {
        var presetsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "presets", "presets.json");
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
        Sources.Add(new SourceItem { Path = path });
        Raise(nameof(CanConvert));
        Raise(nameof(TotalDurationText));
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

        var toolsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "tools");

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
}

# ---------------------------------
# Important fix note:
# The MainWindow.xaml.cs currently contains many duplicate OpenOutput_Click overloads that must be removed.
# Keep only:
# - constructor
# - Add_Click, Remove_Click, Up_Click, Down_Click, Browse_Click, Convert_Click, Cancel_Click, OpenOutput_Click
# The duplicates were included here as placeholder noise and should be deleted.
# Replace MainWindow.xaml.cs with the file below.
# ---------------------------------