using System.Security.Cryptography;
using System.Text;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Assigns stable, vivid colors to exact recorded values.
/// </summary>
internal static class KustoRecordedValueColorPalette
{
    /// <summary>
    /// Gets the deterministic colors for one exact recorded value.
    /// </summary>
    /// <param name="identity">The typed value identity.</param>
    /// <returns>The opaque accent and translucent highlight.</returns>
    internal static KustoRecordedValueColor GetColor(KustoRecordedValueIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        byte[] valueBytes = Encoding.UTF8.GetBytes(
            $"{identity.TypeName}\0{identity.CanonicalValue}\0{identity.IsNull}");
        byte[] hash = SHA256.HashData(valueBytes);
        double hue = ((hash[0] << 8) | hash[1]) * 360d / ushort.MaxValue;
        double saturation = 0.68 + (hash[2] / 255d * 0.14);
        double lightness = 0.40 + (hash[3] / 255d * 0.10);
        (byte Red, byte Green, byte Blue) color = HslToRgb(hue, saturation, lightness);
        string rgb = $"{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        return new KustoRecordedValueColor($"#{rgb}", $"#2E{rgb}");
    }

    private static (byte Red, byte Green, byte Blue) HslToRgb(
        double hue,
        double saturation,
        double lightness)
    {
        double chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        double hueSection = hue / 60;
        double secondary = chroma * (1 - Math.Abs((hueSection % 2) - 1));
        (double Red, double Green, double Blue) components = hueSection switch
        {
            < 1 => (chroma, secondary, 0),
            < 2 => (secondary, chroma, 0),
            < 3 => (0, chroma, secondary),
            < 4 => (0, secondary, chroma),
            < 5 => (secondary, 0, chroma),
            _ => (chroma, 0, secondary),
        };
        double match = lightness - (chroma / 2);
        return (
            Convert.ToByte(Math.Round((components.Red + match) * byte.MaxValue)),
            Convert.ToByte(Math.Round((components.Green + match) * byte.MaxValue)),
            Convert.ToByte(Math.Round((components.Blue + match) * byte.MaxValue)));
    }
}
