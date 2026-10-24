using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PBLanHelper;

public partial class MainWindow : Window
{
    ObservableCollection<Entry> _smbs = new();
    ObservableCollection<Entry> _zips = new();

    public MainWindow()
    {
        InitializeComponent();
        TopBar.Title = "PB - LAN Helper";
        VersionText.Text = "Versão " + typeof(App).Assembly.GetName().Version?.ToString(3);
    }

    void Install_Click(object sender, RoutedEventArgs e) =>
        new ProgressWindow(Mode.Install) { Owner = this }.ShowDialog();

    void Clean_Click(object sender, RoutedEventArgs e) =>
        new ProgressWindow(Mode.Clean) { Owner = this }.ShowDialog();

    void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _smbs = new ObservableCollection<Entry>(AppSettings.LoadSmbs());
        _zips = new ObservableCollection<Entry>(AppSettings.LoadZips());
        SmbList.ItemsSource = _smbs;
        ZipList.ItemsSource = _zips;
        TxtDest.Text = AppSettings.Destination;
        SaveStatus.Text = "";
        HomeView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
    }

    void Home_Click(object sender, RoutedEventArgs e)
    {
        SettingsView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
    }

    void AddSmb_Click(object sender, RoutedEventArgs e) => _smbs.Add(new Entry());

    void AddZip_Click(object sender, RoutedEventArgs e) => _zips.Add(new Entry());

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Entry entry) return;
        _smbs.Remove(entry);
        _zips.Remove(entry);
    }

    void BrowseSmb_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Entry entry) return;
        var dlg = new OpenFolderDialog { Title = "Selecione a pasta de origem (SMB)" };
        if (dlg.ShowDialog(this) == true) entry.Value = dlg.FolderName;
    }

    void BrowseZip_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Entry entry) return;
        var dlg = new OpenFileDialog
        {
            Title = "Selecione o arquivo .zip",
            Filter = "Arquivos ZIP (*.zip)|*.zip"
        };
        if (dlg.ShowDialog(this) == true) entry.Value = ToRelative(dlg.FileName);
    }

    // Se o zip escolhido está dentro de uma das origens, guarda só o caminho relativo
    string ToRelative(string file)
    {
        foreach (var s in _smbs.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var root = s.Value.Trim().TrimEnd('\\') + "\\";
            if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return file[root.Length..];
        }
        return Path.GetFileName(file);
    }

    void BrowseDest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Selecione a pasta de destino" };
        if (Directory.Exists(TxtDest.Text)) dlg.InitialDirectory = TxtDest.Text;
        if (dlg.ShowDialog(this) == true) TxtDest.Text = dlg.FolderName;
    }

    void Status(string text, bool ok)
    {
        SaveStatus.Text = text;
        SaveStatus.Foreground = (Brush)FindResource(ok ? "OkBrush" : "DangerBrush");
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var dest = TxtDest.Text.Trim();
        if (dest == "")
        {
            Status("Informe a pasta de destino.", false);
            return;
        }

        var bad = _zips.FirstOrDefault(z =>
            !string.IsNullOrWhiteSpace(z.Value) &&
            !z.Value.Trim().EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (bad != null)
        {
            Status($"Somente arquivos .zip: {bad.Value}", false);
            return;
        }

        AppSettings.SaveSmbs(_smbs);
        AppSettings.SaveZips(_zips);
        AppSettings.Destination = dest;
        Status("Configurações salvas ✓", true);
    }
}