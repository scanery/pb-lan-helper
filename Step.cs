using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace PBLanHelper;

public enum StepState { Pending, Running, Done, Error }

public sealed class Step : INotifyPropertyChanged
{
    static readonly Brush Blue = Frozen(0x1E, 0x6B, 0xFF);
    static readonly Brush Green = Frozen(0x22, 0xC5, 0x5E);
    static readonly Brush Red = Frozen(0xE5, 0x23, 0x2E);
    static readonly Brush Gray = Frozen(0x8A, 0x98, 0xB8);
    static readonly Brush White = Frozen(0xEA, 0xF0, 0xFF);

    static Brush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    string _text = "", _detail = "";
    StepState _state;
    double _pct;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text { get => _text; set => Set(ref _text, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public StepState State { get => _state; set => Set(ref _state, value); }
    public double Percent { get => _pct; set => Set(ref _pct, value); }
    public bool Determinate { get; set; }

    public string Glyph => State switch
    {
        StepState.Done => "✔",
        StepState.Error => "✖",
        StepState.Running => "◉",
        _ => "○"
    };

    public Brush IconBrush => State switch
    {
        StepState.Done => Green,
        StepState.Error => Red,
        StepState.Running => Blue,
        _ => Gray
    };

    public Brush TextBrush => State == StepState.Pending ? Gray : White;
    public Brush DetailBrush => State == StepState.Error ? Red : Gray;
    public string PercentText => State == StepState.Running && Determinate ? $"{(int)(Percent * 100)}%" : "";
    public Visibility BarVisibility => State == StepState.Running && Determinate ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // atualiza tudo
    }
}

public sealed class StepRunner
{
    public ObservableCollection<Step> Steps { get; } = new();
    public event Action<double, int, int>? Changed;

    public Step Add(string text)
    {
        var s = new Step { Text = text };
        Steps.Add(s);
        return s;
    }

    public async Task Run(Step s, Func<IProgress<double>, Task> work)
    {
        s.State = StepState.Running;
        Report();
        var prog = new Progress<double>(v =>
        {
            if (s.State != StepState.Running) return;
            s.Determinate = true;
            s.Percent = v;
            Report();
        });
        try
        {
            await work(prog);
        }
        catch (Exception ex)
        {
            s.Detail = ex is OperationCanceledException ? "Cancelado" : ex.Message;
            s.State = StepState.Error;
            Report();
            throw;
        }
        s.Percent = 1;
        s.State = StepState.Done;
        Report();
    }

    void Report()
    {
        int total = Math.Max(1, Steps.Count);
        double sum = Steps.Sum(x => x.State == StepState.Done ? 1.0 : x.State == StepState.Running ? x.Percent : 0.0);
        Changed?.Invoke(sum / total, Steps.Count(x => x.State == StepState.Done), Steps.Count);
    }
}