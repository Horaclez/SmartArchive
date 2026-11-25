using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartArchive;

/// Diálogo simple de contraseña (sin XAML para mantenerlo compacto).
public class PasswordDialog : Window
{
    readonly PasswordBox _pb = new();

    public static string? Ask(Window? owner, string file, bool wrong)
    {
        var d = new PasswordDialog(file, wrong);
        if (owner is { IsVisible: true }) d.Owner = owner; else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return d.ShowDialog() == true ? d._pb.Password : null;
    }

    PasswordDialog(string file, bool wrong)
    {
        Title = "Contraseña requerida"; Width = 400; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;

        var ok = new Button { Content = "Aceptar", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Width = 90 };

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = $"“{file}” está protegido con contraseña.", TextWrapping = TextWrapping.Wrap });
        if (wrong)
            sp.Children.Add(new TextBlock { Text = "Contraseña incorrecta. Intenta de nuevo.", Foreground = Brushes.Firebrick, Margin = new Thickness(0, 6, 0, 0) });
        _pb.Margin = new Thickness(0, 12, 0, 14);
        sp.Children.Add(_pb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok); row.Children.Add(cancel);
        sp.Children.Add(row);
        Content = sp;
        Loaded += (_, _) => _pb.Focus();
    }
}
