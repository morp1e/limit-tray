using System;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using LimitTray.App.ViewModels;

namespace LimitTray.App.Views;

public sealed class NullableVisibilityConverter : System.Windows.Data.IValueConverter
{
    public static NullableVisibilityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture) =>
        value is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private void OnGitHubRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
            settings.OpenGitHubCommand.Execute(null);
        e.Handled = true;
    }
}
