# ---------------------------------
# File: src\AVItoDVDISO.Core\Services\PresetsService.cs
# ---------------------------------
using System.Text.Json;
using AVItoDVDISO.Core.Models;

namespace AVItoDVDISO.Core.Services;

public sealed class PresetsService
{
    private readonly string _presetsPath;

    public PresetsService(string presetsPath)
    {
        _presetsPath = presetsPath;
    }

    public PresetsRoot Load()
    {
        if (!File.Exists(_presetsPath))
            throw new FileNotFoundException("Presets file not found.", _presetsPath);

        var json = File.ReadAllText(_presetsPath);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root = JsonSerializer.Deserialize<PresetsRoot>(json, opts);

        if (root is null)
            throw new InvalidOperationException("Failed to parse presets.json.");

        return root;
    }
}