using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace RemotePainting.Controls
{
    public class DrawingCanvas : Control, IDrawingBoard
    {
        public static readonly StyledProperty<Color> BrushColorProperty =
            AvaloniaProperty.Register<DrawingCanvas, Color>(nameof(BrushColor), Colors.Black);

        public static readonly StyledProperty<double> BrushThicknessProperty =
            AvaloniaProperty.Register<DrawingCanvas, double>(nameof(BrushThickness), 3);

        public Color BrushColor
        {
            get => GetValue(BrushColorProperty);
            set => SetValue(BrushColorProperty, value);
        }

        public double BrushThickness
        {
            get => GetValue(BrushThicknessProperty);
            set => SetValue(BrushThicknessProperty, value);
        }

        private readonly List<Stroke> _strokes = new();
        private readonly Dictionary<(string Author, int Id), Stroke> _byId = new();
        private int _currentId = -1;   
        private int _nextId;

        private const double MinDistancePx = 1.5;

        public event Action<DrawCommand>? CommandProduced;

        public void Apply(DrawCommand cmd)
        {
            switch (cmd)
            {
                case BeginStroke b:
                    var s = new Stroke { Author = b.Author, Id = b.Id, ColorArgb = b.Color, Thickness = b.Thickness };
                    s.Points.Add(new Point(b.X, b.Y));
                    _strokes.Add(s);
                    _byId[(b.Author, s.Id)] = s;
                    break;

                case AddPoint a:
                    if (_byId.TryGetValue((a.Author, a.Id), out var st))
                        st.Points.Add(new Point(a.X, a.Y));
                    break;

                case RemoveStroke r:
                    if (_byId.Remove((r.Author, r.Id), out var rs))
                        _strokes.Remove(rs);
                    break;

                case ClearAll:
                    _strokes.Clear();
                    _byId.Clear();
                    break;
            }
            InvalidateVisual();
        }

        private void Do(DrawCommand cmd)
        {
            Apply(cmd);
            CommandProduced?.Invoke(cmd);
        }

        private Point Norm(Point p) => new(p.X / Bounds.Width, p.Y / Bounds.Height);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

            var p = Norm(e.GetPosition(this));
            _currentId = _nextId++;
            Do(new BeginStroke("Me", _currentId, BrushColor.ToUInt32(), (float)BrushThickness,
                               (float)p.X, (float)p.Y));
            e.Pointer.Capture(this); 
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_currentId < 0) return;

            var p = Norm(e.GetPosition(this));

            if (_byId.TryGetValue(("Me", _currentId), out var stroke) && stroke.Points.Count > 0)
            {
                var last = stroke.Points[^1];
                var dx = (p.X - last.X) * Bounds.Width;
                var dy = (p.Y - last.Y) * Bounds.Height;
                if (dx * dx + dy * dy < MinDistancePx * MinDistancePx) return;
            }

            Do(new AddPoint("Me", _currentId, (float)p.X, (float)p.Y));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _currentId = -1;
            e.Pointer.Capture(null);
        }

        public void Undo()
        {
            if (_strokes.Count > 0)
                Do(new RemoveStroke("Me", _strokes[^1].Id));
        }

        public void Clear() => Do(new ClearAll());

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

            foreach (var s in _strokes)
            {
                var brush = new SolidColorBrush(Color.FromUInt32(s.ColorArgb));

                if (s.Points.Count == 1)
                {
                    ctx.DrawEllipse(brush, null, ToPx(s.Points[0]), s.Thickness / 2, s.Thickness / 2);
                    continue;
                }

                var pen = new Pen(brush, s.Thickness,
                    lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

                for (int i = 1; i < s.Points.Count; i++)
                    ctx.DrawLine(pen, ToPx(s.Points[i - 1]), ToPx(s.Points[i]));
            }
        }

        private Point ToPx(Point p) => new(p.X * Bounds.Width, p.Y * Bounds.Height);
    }
}
