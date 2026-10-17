using Avalonia;
using System;
using System.Collections.Generic;

namespace RemotePainting.Controls
{
    public abstract record DrawCommand;
    public record BeginStroke(string Author, int Id, uint Color, float Thickness, float X, float Y) : DrawCommand;
    public record AddPoint(string Author, int Id, float X, float Y) : DrawCommand;
    public record RemoveStroke(string Author, int Id) : DrawCommand;
    public record CursorMove(string Author, float X, float Y) : DrawCommand;
    public record CursorHide(string Author) : DrawCommand;
    public record ClearAll : DrawCommand;

    public class Stroke
    {
        public string Author { get; init; } = string.Empty;
        public int Id { get; init; }
        public uint ColorArgb { get; init; }
        public float Thickness { get; init; }
        public List<Point> Points { get; } = new();
    }

    public interface IDrawingBoard
    {
        event Action<DrawCommand>? CommandProduced;
        void Apply(DrawCommand cmd);
        List<DrawCommand> GetAllStrokeCommands();
        void SetActive(bool active);
    }
}
