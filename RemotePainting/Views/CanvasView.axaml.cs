using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

    private async void SavePng_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить рисунок",
            SuggestedFileName = "drawing.png",
            DefaultExtension = "png",
            FileTypeChoices = new[] { FilePickerFileTypes.ImagePng }
        });
        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        Board.SaveToPng(stream);
    }
}