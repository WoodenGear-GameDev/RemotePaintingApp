using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
        private readonly Dictionary<string, (Point Pos, long Seen)> _cursors = new();
        private int _currentId = -1;
        private int _nextId;
        private bool _forExport;

        private DispatcherTimer? _cursorTimer;
        private long _lastCursorSent;

        private const double MinDistancePx = 1.5;
        private const long CursorSendIntervalMs = 33;
        private const long CursorTimeoutMs = 10_000;

        private static readonly Color[] Palette =
        {
            Color.Parse("#e6194b"), Color.Parse("#3cb44b"), Color.Parse("#4363d8"),
            Color.Parse("#f58231"), Color.Parse("#911eb4"), Color.Parse("#008080"),
            Color.Parse("#9a6324"), Color.Parse("#800000")
        };

        public event Action<DrawCommand>? CommandProduced;
        private bool _boardIsActive = false;

        public void SetActive(bool active)
        {
            _boardIsActive = active;
        }

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

                case CursorMove c:
                    _cursors[c.Author] = (new Point(c.X, c.Y), Environment.TickCount64);
                    break;

                case CursorHide h:
                    _cursors.Remove(h.Author);
                    break;
            }
            InvalidateVisual();
        }

        private void Do(DrawCommand cmd)
        {
            if (!_boardIsActive)
                return;

            Apply(cmd);
            CommandProduced?.Invoke(cmd);
        }

        public List<DrawCommand> GetAllStrokeCommands()
        {
            List<DrawCommand> strokes = new List<DrawCommand>();
            foreach (var stroke in _strokes)
            {
                strokes.Add(new BeginStroke(stroke.Author, stroke.Id, stroke.ColorArgb, stroke.Thickness,
                                      (float)stroke.Points[0].X, (float)stroke.Points[0].Y));

                foreach (var point in (stroke.Points).Skip(1))
                    strokes.Add(new AddPoint(stroke.Author, stroke.Id, (float)point.X, (float)point.Y));
            }
            return strokes;
        }

        private Point Norm(Point p) => new(p.X / Bounds.Width, p.Y / Bounds.Height);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cursorTimer.Tick += (_, _) =>
            {
                var now = Environment.TickCount64;
                var stale = _cursors.Where(kv => now - kv.Value.Seen > CursorTimeoutMs)
                                    .Select(kv => kv.Key).ToList();
                if (stale.Count == 0) return;
                foreach (var k in stale) _cursors.Remove(k);
                InvalidateVisual();
            };
            _cursorTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _cursorTimer?.Stop();
            _cursorTimer = null;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!_boardIsActive)
                return;

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
            if (Bounds.Width <= 0 || Bounds.Height <= 0 || !_boardIsActive) return;

            var p = Norm(e.GetPosition(this));

            // Позиция курсора для других пользователей (с ограничением частоты)
            var now = Environment.TickCount64;
            if (now - _lastCursorSent >= CursorSendIntervalMs)
            {
                _lastCursorSent = now;
                CommandProduced?.Invoke(new CursorMove("Me",
                    (float)Math.Clamp(p.X, 0, 1), (float)Math.Clamp(p.Y, 0, 1)));
            }

            if (_currentId < 0) return;

            if (_byId.TryGetValue(("Me", _currentId), out var stroke) && stroke.Points.Count > 0)
            {
                var last = stroke.Points[^1];
                var dx = (p.X - last.X) * Bounds.Width;
                var dy = (p.Y - last.Y) * Bounds.Height;
                if (dx * dx + dy * dy < MinDistancePx * MinDistancePx) return;
            }

            Do(new AddPoint("Me", _currentId, (float)p.X, (float)p.Y));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            CommandProduced?.Invoke(new CursorHide("Me"));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _currentId = -1;
            e.Pointer.Capture(null);
        }

        public void Undo()
        {
            if (_strokes.Count > 0)
            {
                for (int i = _strokes.Count - 1; i >= 0; i--)
                {
                    var s = _strokes[i];
                    if (s.Author == "Me")
                    {
                        Do(new RemoveStroke("Me", s.Id));
                        break;
                    }
                }
            }
        }

        public void Clear() => Do(new ClearAll());

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(_forExport ? Brushes.White : Brushes.Transparent, new Rect(Bounds.Size));

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

            if (!_forExport)
            {
                foreach (var (name, info) in _cursors)
                    DrawCursorLabel(ctx, name, info.Pos);
            }
        }

        private void DrawCursorLabel(DrawingContext ctx, string name, Point pos)
        {
            var px = ToPx(pos);
            var color = Palette[name.Sum(ch => (int)ch) % Palette.Length];
            var brush = new SolidColorBrush(color);

            ctx.DrawEllipse(brush, new Pen(Brushes.White, 1.5), px, 5, 5);

            var text = new FormattedText(name, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White);

            const double padX = 6, padY = 2;
            var w = text.Width + padX * 2;
            var h = text.Height + padY * 2;
            var x = px.X + 10;
            var y = px.Y + 10;
            if (x + w > Bounds.Width) x = px.X - 10 - w;
            if (y + h > Bounds.Height) y = px.Y - 10 - h;

            ctx.DrawRectangle(brush, null, new Rect(x, y, w, h), 4, 4);
            ctx.DrawText(text, new Point(x + padX, y + padY));
        }

        public void SaveToPng(Stream stream)
        {
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var bmp = new RenderTargetBitmap(size, new Vector(96, 96));
            _forExport = true;
            try { bmp.Render(this); }
            finally { _forExport = false; }
            bmp.Save(stream);
        }

        private Point ToPx(Point p) => new(p.X * Bounds.Width, p.Y * Bounds.Height);
    }
}