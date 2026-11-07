using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace NotchBit.Modules.Music;

/// <summary>The two small pieces shown beside the resting island while music plays.</summary>
internal static class MusicCompactViews
{
    private const double ArtworkSize = 22;
    private const int BarCount = 4;

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
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 16 };
        var storyboard = new Storyboard();
        var random = new Random(7);

        for (int i = 0; i < BarCount; i++)
        {
            var scale = new ScaleTransform { ScaleY = 0.3 };
            panel.Children.Add(new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(1.5),
                Background = fill,
                RenderTransform = scale,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1),
            });

            // Each bar bounces at its own pace, so they never move in lockstep.
            var bounce = new DoubleAnimation
            {
                From = 0.25,
                To = 0.6 + random.NextDouble() * 0.4,
                Duration = TimeSpan.FromMilliseconds(320 + random.Next(0, 300)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(bounce, scale);
            Storyboard.SetTargetProperty(bounce, "ScaleY");
            storyboard.Children.Add(bounce);
        }

        void Update()
        {
            fill.Color = controller.Current?.Accent ?? Colors.White;
        }
        controller.Changed += Update;
        Update();

        storyboard.Begin();
        return panel;
    }
}
