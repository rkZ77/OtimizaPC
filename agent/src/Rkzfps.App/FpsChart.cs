using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Rkzfps.Core.Benchmark;

namespace Rkzfps.App;

/// <summary>
/// Gráfico de FPS ao longo da partida, desenhado direto (sem biblioteca):
/// área azul com o FPS médio e linha laranja com o pior quadro de cada
/// trecho, que é onde aparecem os picos para baixo, as travadas que a pessoa
/// sente. Só redesenha quando os pontos mudam: não pesa no jogo.
/// </summary>
public sealed class FpsChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<FpsPoint>), typeof(FpsChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Taxa do monitor: linha de referência, porque FPS acima dela não aparece na tela.</summary>
    public static readonly DependencyProperty MonitorHzProperty = DependencyProperty.Register(
        nameof(MonitorHz), typeof(int?), typeof(FpsChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Quedas fortes com a causa provável: marca no gráfico e texto ao passar o mouse.</summary>
    public static readonly DependencyProperty DropsProperty = DependencyProperty.Register(
        nameof(Drops), typeof(IReadOnlyList<DropCause>), typeof(FpsChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<DropCause>? Drops
    {
        get => (IReadOnlyList<DropCause>?)GetValue(DropsProperty);
        set => SetValue(DropsProperty, value);
    }

    public IReadOnlyList<FpsPoint>? Points
    {
        get => (IReadOnlyList<FpsPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public int? MonitorHz
    {
        get => (int?)GetValue(MonitorHzProperty);
        set => SetValue(MonitorHzProperty, value);
    }

    private const double Left = 40, Bottom = 20, Top = 8, Right = 8;
    private int? _hover;

    public FpsChart()
    {
        Height = 190;
        Cursor = Cursors.Cross;
        // Desenho feito na mão lê as cores na hora de desenhar: na troca de tema, redesenha.
        Loaded += (_, _) => ThemeManager.Changed += InvalidateVisual;
        Unloaded += (_, _) => ThemeManager.Changed -= InvalidateVisual;
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pts = Points;
        if (pts is not { Count: > 1 })
            return;
        var x = e.GetPosition(this).X;
        var i = (int)Math.Round((x - Left) / Math.Max(1, ActualWidth - Left - Right) * (pts.Count - 1));
        i = Math.Clamp(i, 0, pts.Count - 1);
        if (i != _hover)
        {
            _hover = i;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        // Fundo transparente mas "acertável", para o mouse funcionar em cima do gráfico.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var muted = Res("Muted");
        var pts = Points;
        if (pts is not { Count: > 1 } || w < Left + Right + 20 || h < Top + Bottom + 20)
        {
            Text(dc, "O gráfico aparece quando houver quadros medidos.", muted, 12, Left, h / 2 - 8);
            return;
        }

        var plotW = w - Left - Right;
        var plotH = h - Top - Bottom;
        var max = NiceMax(Math.Max(pts.Max(p => p.Fps), MonitorHz ?? 0) * 1.08);
        double X(int i) => Left + i * plotW / (pts.Count - 1);
        double Y(double fps) => Top + plotH - Math.Clamp(fps / max, 0, 1) * plotH;

        // Grade e rótulos do eixo de FPS.
        var grid = new Pen(Res("Border"), 1);
        grid.Freeze();
        for (var k = 0; k <= 4; k++)
        {
            var v = max * k / 4;
            var y = Y(v);
            dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
            Text(dc, v.ToString("0", CultureInfo.InvariantCulture), muted, 11, 2, y - 8);
        }

        // Área e linha do FPS médio.
        var accent = Res("Accent");
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(X(0), Y(0)), true, true);
            for (var i = 0; i < pts.Count; i++)
                g.LineTo(new Point(X(i), Y(pts[i].Fps)), false, false);
            g.LineTo(new Point(X(pts.Count - 1), Y(0)), false, false);
        }

        area.Freeze();
        var fill = accent.Clone();
        fill.Opacity = 0.16;
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(accent, 1.6), Line(pts.Select(p => p.Fps).ToList(), X, Y));

        // Pior quadro de cada trecho: os picos para baixo.
        dc.DrawGeometry(null, new Pen(Res("Warn"), 1), Line(pts.Select(p => p.Low).ToList(), X, Y));

        if (MonitorHz is { } hz && hz > 0 && hz < max)
        {
            var dash = new Pen(muted, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(dash, new Point(Left, Y(hz)), new Point(w - Right, Y(hz)));
            Text(dc, $"monitor {hz} Hz", muted, 11, w - Right - 80, Y(hz) - 16);
        }

        // Marca de cada queda forte, na base do gráfico.
        var danger = Res("Danger");
        var step = pts.Count > 1 ? Math.Max(1, pts[1].T - pts[0].T) : 1;
        int IndexOf(int t) => Math.Clamp((int)Math.Round((t - pts[0].T) / (double)Math.Max(1, pts[^1].T - pts[0].T) * (pts.Count - 1)), 0, pts.Count - 1);
        foreach (var d in Drops ?? [])
        {
            var dx = X(IndexOf(d.T));
            var tri = new StreamGeometry();
            using (var g = tri.Open())
            {
                g.BeginFigure(new Point(dx, Top + plotH - 7), true, true);
                g.LineTo(new Point(dx - 4, Top + plotH), false, false);
                g.LineTo(new Point(dx + 4, Top + plotH), false, false);
            }

            tri.Freeze();
            dc.DrawGeometry(danger, null, tri);
        }

        // Eixo de tempo: início, meio e fim.
        foreach (var i in new[] { 0, pts.Count / 2, pts.Count - 1 })
        {
            var label = Clock(pts[i].T - pts[0].T);
            var x = Math.Clamp(X(i) - 14, Left, w - Right - 34);
            Text(dc, label, muted, 11, x, h - Bottom + 4);
        }

        if (_hover is { } hi && hi < pts.Count)
        {
            var p = pts[hi];
            var x = X(hi);
            dc.DrawLine(new Pen(muted, 1), new Point(x, Top), new Point(x, Top + plotH));
            dc.DrawEllipse(accent, null, new Point(x, Y(p.Fps)), 3.5, 3.5);
            var tip = $"{Clock(p.T - pts[0].T)}   {p.Fps:0} FPS   pior quadro {p.Low:0}";
            // Perto de uma queda, o texto diz o que estava acontecendo no PC.
            if ((Drops ?? []).Where(d => Math.Abs(d.T - p.T) <= step).OrderBy(d => Math.Abs(d.T - p.T)).FirstOrDefault() is { } cause)
                tip += Environment.NewLine + cause.Text;
            var ft = Format(tip, Res("Text"), 12);
            ft.MaxTextWidth = Math.Max(120, Math.Min(360, w - Left - Right - 12));
            var bx = Math.Clamp(x + 10, Left, w - Right - ft.Width - 12);
            dc.DrawRoundedRectangle(Res("Surface2"), new Pen(Res("Border"), 1), new Rect(bx, Top + 2, ft.Width + 12, ft.Height + 6), 6, 6);
            dc.DrawText(ft, new Point(bx + 6, Top + 5));
        }
    }

    private static StreamGeometry Line(IReadOnlyList<double> values, Func<int, double> x, Func<double, double> y)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(x(0), y(values[0])), false, false);
            for (var i = 1; i < values.Count; i++)
                g.LineTo(new Point(x(i), y(values[i])), true, true);
        }

        geo.Freeze();
        return geo;
    }

    /// <summary>Topo do eixo em número redondo (60, 120, 200, 300...), para a grade ser legível.</summary>
    private static double NiceMax(double v)
    {
        foreach (var step in new double[] { 30, 60, 90, 120, 150, 180, 240, 300, 360, 480, 600, 800, 1000 })
            if (v <= step)
                return step;
        return Math.Ceiling(v / 500) * 500;
    }

    private static string Clock(int seconds) =>
        seconds >= 3600 ? $"{seconds / 3600}h{seconds % 3600 / 60:00}" : $"{seconds / 60}:{seconds % 60:00}";

    private FormattedText Format(string s, Brush brush, double size) =>
        new(s, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void Text(DrawingContext dc, string s, Brush brush, double size, double x, double y) =>
        dc.DrawText(Format(s, brush, size), new Point(x, y));
}
