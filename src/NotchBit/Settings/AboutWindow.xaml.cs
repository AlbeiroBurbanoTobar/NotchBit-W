using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NotchBit.Settings;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        WindowSizing.SetAppIcon(this);
        WindowSizing.SizeAndCenter(this, 420, 420);

        // The informational version carries a "+commit" suffix from the SDK; show just the number.
        string? version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        VersionText.Text = $"Versión {version?.Split('+')[0] ?? "desconocida"}";
    }
}
