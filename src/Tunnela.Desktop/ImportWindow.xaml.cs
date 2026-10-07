using System.Windows;

namespace Tunnela.Desktop;

public partial class ImportWindow : Window
{
    public string ConfigurationText => Input.Text;
    public ImportWindow() => InitializeComponent();
    private void Import_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(Input.Text)) DialogResult = true; }
}
