using AVItoDVDISO.Core.Models;

namespace AVItoDVDISO.Core.Services;

public static class BitrateCalculator
{
    // Effective DVD-5 budget in GiB for user content after overhead.
    private const double EffectiveGiB = 4.1;

    public static int CalculateVideoBitrateKbpsFit(TimeSpan totalDuration, int audioBitrateKbps, int minVideoKbps, int maxVideoKbps)
    {
        var seconds = Math.Max(1.0, totalDuration.TotalSeconds);

        var availableBits = EffectiveGiB * 1024 * 1024 * 1024 * 8;
        var audioBits = seconds * (audioBitrateKbps * 1000.0);
        var videoBits = Math.Max(0.0, availableBits - audioBits);

        var videoBps = videoBits / seconds;
        var videoKbps = (int)Math.Floor(videoBps / 1000.0);

        if (videoKbps < minVideoKbps) videoKbps = minVideoKbps;
        if (videoKbps > maxVideoKbps) videoKbps = maxVideoKbps;

        return videoKbps;
    }

}
