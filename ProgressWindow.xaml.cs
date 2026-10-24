using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace PBLanHelper;

public enum Mode { Install, Clean }

public partial class ProgressWindow : Window
{
    readonly Mode _mode;
    CancellationTokenSource? _cts;
    bool _running;

    public ProgressWindow(Mode mode)
    {
        InitializeComponent();
        _mode = mode;
        bool install = mode == Mode.Install;
        TopBar.Title = install ? "PB - LAN Helper · Instalar" : "PB - LAN Helper · Limpar";

        if (install)
        {
            Loaded += async (_, _) => await Run();
        }
        else
        {
            ConfirmText.Text = $"Esta ação irá apagar a pasta {AppSettings.Destination} e tudo que há dentro dela, " +
                               $"além da pasta {AppSettings.AppDataFolder} na sua pasta AppData.";
            ConfirmPanel.Visibility = Visibility.Visible;
        }
    }

    Brush Res(string key) => (Brush)FindResource(key);

    void SetHeader(string glyph, Brush bg, string title, string sub)
    {
        IconText.Text = glyph;
        IconCircle.Background = bg;
        HeaderTitle.Text = title;
        HeaderSub.Text = sub;
    }

    static string Fmt(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s" : $"{t.TotalSeconds:0.0}s";

    async Task Run()
    {
        _running = true;
        _cts = new CancellationTokenSource();
        var runner = new StepRunner();
        StepsList.ItemsSource = runner.Steps;
        runner.Changed += (f, d, t) =>
        {
            Overall.Value = f * 100;
            CountText.Text = $"{d}/{t}";
        };
        Overall.Value = 0;
        CountText.Text = "";
        BtnRetry.Visibility = Visibility.Collapsed;
        BtnClose.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Visible;
        BtnCancel.IsEnabled = true;

        bool install = _mode == Mode.Install;
        SetHeader(install ? "\uE896" : "\uE74D", Res("AccentBrush"),
                  install ? "Instalando…" : "Limpando…", "Aguarde, não feche o app.");

        var sw = Stopwatch.StartNew();
        bool ok = false;
        try
        {
            if (install) await Operations.Install(runner, _cts.Token);
            else await Operations.Clean(runner, _cts.Token);

            ok = true;
            Overall.Value = 100;
            SetHeader("\uE73E", Res("OkBrush"),
                      install ? "Instalação concluída!" : "Limpeza concluída!",
                      $"Tudo certo em {Fmt(sw.Elapsed)}.");
        }
        catch (OperationCanceledException)
        {
            SetHeader("\uE711", Res("DangerBrush"), "Cancelado", "A operação foi interrompida.");
        }
        catch (Exception ex)
        {
            SetHeader("\uE711", Res("DangerBrush"), install ? "Falha na instalação" : "Falha na limpeza", ex.Message);
        }
        finally
        {
            _running = false;
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnClose.Visibility = Visibility.Visible;
            BtnRetry.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        BtnCancel.IsEnabled = false;
        _cts?.Cancel();
    }

    async void Retry_Click(object sender, RoutedEventArgs e) => await Run();

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void ConfirmCancel_Click(object sender, RoutedEventArgs e) => Close();

    async void ConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        await Run();
    }

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_running) return;
        e.Cancel = true;
        _cts?.Cancel();
    }
}