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
    public string St
