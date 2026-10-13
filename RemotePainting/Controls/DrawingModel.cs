using Avalonia;
using System.Collections.Generic;

namespace RemotePainting.Controls
{
    public abstract record DrawCommand;
    public record BeginStroke(int Id, uint Color, float Thickness, float X, float Y) : DrawCommand;
    public record AddPoint(int Id, float X, float Y) : DrawCommand;
    public record RemoveStroke(int Id) : DrawCommand;
    public record ClearAll : DrawCommand;

    public class Stroke
    {
        public int Id { get; init; }
        public uint ColorArgb { get; init; }
        public float Thickness { get; init; }
        public List<Point> Points { get; } = new();
    }
}
