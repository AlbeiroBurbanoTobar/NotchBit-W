using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace NotchBit.Modules.Music;

/// <summary>Picks the color that best represents a piece of artwork, for tinting the playback bars.</summary>
public static class ArtworkPalette
{
    /// <summary>The dominant color of the image, or <c>null</c> when it is grayscale (the bars then stay white).</summary>
    public static async Task<Color?> DominantColorAsync(IRandomAccessStream stream)
    {
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform { ScaledWidth = 32, ScaledHeight = 32 };
        PixelDataProvider data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        byte[] pixels = data.DetachPixelData();

        // Average the pixels, weighting vivid ones far more than dull ones, so a mostly gray
        // cover with a red logo comes out red rather than gray.
        double red = 0, green = 0, blue = 0, total = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            double b = pixels[i] / 255.0, g = pixels[i + 1] / 255.0, r = pixels[i + 2] / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max < 0.2) continue; // near black

            double saturation = (max - min) / max;
            double weight = saturation * saturation * max;
            red += r * weight;
            green += g * weight;
            blue += b * weight;
            total += weight;
        }
        if (total < 0.5) return null;

        red /= total;
        green /= total;
        blue /= total;

        // The bars sit on a black island, so lift dark colors until they are easy to see.
        double brightest = Math.Max(red, Math.Max(green, blue));
        double scale = brightest < 0.75 ? 0.75 / brightest : 1;
        return Color.FromArgb(255, ToByte(red * scale), ToByte(green * scale), ToByte(blue * scale));
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
