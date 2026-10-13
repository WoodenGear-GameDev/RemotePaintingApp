using Avalonia.Controls;
using Avalonia.Interactivity;
using RemotePainting.ViewModels;
using RemotePainting.ViewModels.Menus;

namespace RemotePainting.Views;

public partial class CanvasView : UserControl
{
    public CanvasView()
    {
        InitializeComponent();

        Loaded += (s, e) =>
        {
            if (DataContext is CanvasVM vm)
            {
                vm.AttachBoard(Board);
            }
        };
    }

    private void Undo_Click(object? sender, RoutedEventArgs e) => Board.Undo();

    private void Clear_Click(object? sender, RoutedEventArgs e) => Board.Clear();

    private void ConsoleTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.CaretIndex = textBox.Text?.Length ?? 0;
        }
    }
}