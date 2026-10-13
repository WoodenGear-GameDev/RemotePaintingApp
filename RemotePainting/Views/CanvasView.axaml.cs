using Avalonia.Controls;
using RemotePainting.ViewModels;

namespace RemotePainting.Views;

public partial class CanvasView : UserControl
{
    public CanvasView()
    {
        InitializeComponent();
    }

    private void ConsoleTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.CaretIndex = textBox.Text?.Length ?? 0;
        }
    }
}