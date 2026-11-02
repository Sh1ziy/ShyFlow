using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Island;

// Шаг 79: пузырь «вытекает» из острова, как капля. Пока он отделяется (и когда втягивается обратно), между краем острова
// и пузырём тянется жидкая перемычка того же цвета: сначала толстая, утончается по мере удаления и рвётся,
// а пузырь от рывка чуть сплющивается и отскакивает. Работает и в обычном режиме, и в notch (перемычка у рамки).
// Перемычка — один Path за островом и пузырём, рисуется по кадрам только пока идёт анимация (~0,7 с).
public partial class MainWindow
{
    private Path? _neck;
    private DateTime _neckUntil;
    private bool _neckHooked, _neckSnapped, _neckOut;
    private const double NeckBreak = 12.5;   // на каком зазоре рвётся, px: в покое зазор 8, пузырь с пружиной пролетает дальше и рвёт её

    // outward: пузырь выходит (порвалась — больше не срастается); иначе втягивается (перемычка снова растёт)
    private void DripStart(int ms, bool outward)
    {
        _neckOut = outward;
        if (!LiquidOn) return;   // жидкость выключена или экономный режим: пузырь просто выезжает, как раньше
        if (_neck == null)
        {
            _neck = new Path { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            Stage.Children.Insert(0, _neck);   // за островом и пузырём
        }
        _neck.Fill = Pill.Background;
        _neck.Visibility = Visibility.Visible;
        _neckSnapped = false;
        _neckUntil = DateTime.UtcNow.AddMilliseconds(ms + 120);
        if (!_neckHooked) { CompositionTarget.Rendering += NeckFrame; _neckHooked = true; }
    }

    private void NeckFrame(object? s, EventArgs e)
    {
        if (_neck == null) return;
        if (DateTime.UtcNow > _neckUntil || Bubble.Visibility != Visibility.Visible || _hidden)
        {
            // время вышло, а перемычка не порвалась (пружина не дотянула): рвём сейчас, с тем же отскоком
            if (_neckOut && !_neckSnapped && _neck.Data != null && Bubble.Visibility == Visibility.Visible) SnapWobble();
            _neck.Data = null;
            _neck.Visibility = Visibility.Collapsed;
            CompositionTarget.Rendering -= NeckFrame;
            _neckHooked = false;
            return;
        }
        try
        {
            var a = Pill.TransformToVisual(Stage).TransformBounds(new Rect(Pill.RenderSize));
            var b = Bubble.TransformToVisual(Stage).TransformBounds(new Rect(Bubble.RenderSize));
            bool right = b.Left + b.Width / 2 > a.Left + a.Width / 2;
            double gap = right ? b.Left - a.Right : a.Left - b.Right;
            if (gap > NeckBreak || b.Width < 4 || (_neckOut && _neckSnapped))
            {
                _neck.Data = null;
                if (!_neckSnapped && gap > NeckBreak) { _neckSnapped = true; if (_neckOut) SnapWobble(); }
                return;
            }
            double cy = b.Top + b.Height / 2;
            double hEnd = Math.Min(a.Height, b.Height) * 0.78;                  // у краёв перемычка почти во всю высоту пузыря
            double k = Math.Clamp(Math.Max(0, gap) / NeckBreak, 0, 1);
            double hMid = Math.Max(1.2, hEnd * Math.Pow(1 - k, 1.6) * 0.9);     // а в середине утончается и рвётся
            const double inset = 8;                                             // концы заходят под остров и пузырь
            double x0 = right ? a.Right - inset : a.Left + inset;
            double x1 = right ? b.Left + inset : b.Right - inset;
            double xm = (x0 + x1) / 2;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x0, cy - hEnd / 2), true, true);
                c.BezierTo(new Point(xm, cy - hEnd / 2), new Point(xm, cy - hMid / 2), new Point(xm, cy - hMid / 2), true, true);
                c.BezierTo(new Point(xm, cy - hMid / 2), new Point(xm, cy - hEnd / 2), new Point(x1, cy - hEnd / 2), true, true);
                c.LineTo(new Point(x1, cy + hEnd / 2), true, true);
                c.BezierTo(new Point(xm, cy + hEnd / 2), new Point(xm, cy + hMid / 2), new Point(xm, cy + hMid / 2), true, true);
                c.BezierTo(new Point(xm, cy + hMid / 2), new Point(xm, cy + hEnd / 2), new Point(x0, cy + hEnd / 2), true, true);
            }
            g.Freeze();
            _neck.Data = g;
        }
        catch (Exception ex) { App.LogOnce("Drip", ex); }
    }

    // перемычка порвалась: пузырь от рывка сплющивается и отскакивает, как капля
    private void SnapWobble()
    {
        double cx = BubbleScale.ScaleX, cyS = BubbleScale.ScaleY;
        var sx = new System.Windows.Media.Animation.DoubleAnimation(cx * 1.12, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase(6, 13) };
        var sy = new System.Windows.Media.Animation.DoubleAnimation(cyS * 0.88, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase(6, 13) };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
    }
}
