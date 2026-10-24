using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PBLanHelper;

public partial class TitleBar : UserControl
{
    public TitleBar() => InitializeComponent();

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    void Drag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) Window.GetWindow(this)?.DragMove();
    }

    void Min_Click(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w != null) w.WindowState = WindowState.Minimized;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}