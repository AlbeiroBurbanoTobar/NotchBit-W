using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace NotchBit.Modules.Music;

/// <summary>The two small pieces shown beside the resting island while music plays.</summary>
internal static class MusicCompactViews
{
    private const double ArtworkSize = 22;
    private const int BarCount = 4;
    private const float BarWidth = 3;
    private const float BarHeight = 16;

    /// <summary>The track's artwork, or a music note until it loads.</summary>
    public static UIElement CreateArtwork(MusicController controller)
    {
        var brush = new ImageBrush { Stretch = Stretch.UniformToFill };
        var note = new FontIcon { Glyph = "", FontSize = 12, Opacity = 0.5, Foreground = new SolidColorBrush(Colors.White) };
        var root = new Grid { Width = ArtworkSize, Height = ArtworkSize };
        root.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = brush });
        root.Children.Add(note);

        void Update()
        {
            brush.ImageSource = controller.Current?.Artwork;
            note.Visibility = controller.Current?.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        }
        controller.Changed += Update;
        Update();
        return root;
    }

    /// <summary>Animated bars in the artwork's dominant color. They are decorative.</summary>
    public static UIElement CreateBars(MusicController controller)
    {
        var fill = new SolidColorBrush(Colors.White);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = BarHeight };
        var bars = new List<Border>();
        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Border
            {
                Width = BarWidth,
                Height = BarHeight,
                CornerRadius = new CornerRadius(BarWidth / 2),
                Background = fill,
            };
            bars.Add(bar);
            panel.Children.Add(bar);
        }

        void Update()
        {
            fill.Color = controller.Current?.Accent ?? Colors.White;
        }
        controller.Changed += Update;
        Update();

        // A smooth animation redraws the island 60 times a second, and WinUI composes in the app's
        // own process: measured at about 9% of a core while music plays, against under 1% still.
        // Equalizer-style steps about ten times a second look the part for a fraction of that.
        // They run only while the bars are on screen: the island takes them out of the tree whenever
        // the compact view is hidden.
        var visuals = bars.Select(ElementCompositionPreview.GetElementVisual).ToList();
        foreach (Visual visual in visuals) visual.CenterPoint = new Vector3(BarWidth / 2, BarHeight, 0); // grow up from the bottom

        var random = new Random();
        var levels = new float[BarCount];
        var timer = panel.DispatcherQueue.CreateTimer();
        timer.Interval = BarStep;
        timer.Tick += (_, _) =>
        {
            for (int i = 0; i < BarCount; i++)
            {
                // Drift toward a new random height rather than jumping anywhere, so it reads as music.
                float target = 0.25f + (float)random.NextDouble() * 0.75f;
                levels[i] = levels[i] * 0.35f + target * 0.65f;
                visuals[i].Scale = new Vector3(1, levels[i], 1);
            }
        };
        panel.Loaded += (_, _) => timer.Start();
        panel.Unloaded += (_, _) => timer.Stop();
        return panel;
    }

    /// <summary>How often the bars change height.</summary>
    private static readonly TimeSpan BarStep = TimeSpan.FromMilliseconds(100);
}
