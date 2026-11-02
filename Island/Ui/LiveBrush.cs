using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Island;

// Шаг 79: ShyFlow больше не падает при переключении треков.
// Дампы падений (%LocalAppData%\CrashDumps) все одинаковые: поток отрисовки WPF, CMilSolidColorBrushDuce::ProcessUpdate ->
// CMilSlaveResource::NotifyOnChanged -> _purecall -> abort. Это ошибка самого WPF: кисть, у которой меняется цвет, сообщает
// об этом элементу, которого уже нет. Так бывает, когда одна изменяемая кисть стоит на элементах, которые перерисовываются
// каждый кадр (эквалайзер, полоска перемотки, буквы), и в это же время цвет кисти плавно меняется (смена трека -> цвет обложки).
// ColorTween (шаг 48) убрал ColorAnimation, но саму изменяемую кисть оставил, поэтому падения стали реже, но не исчезли.
// Теперь кисти, которые меняют цвет, не изменяются никогда: на каждый цвет — новая замороженная кисть (SafeBrush.Solid),
// а LiveBrush раздаёт её всем, кто ею пользуется. Замороженная кисть потоку отрисовки ни о чём не сообщает.
internal static class SafeBrush
{
    private static readonly Dictionary<uint, SolidColorBrush> Cache = new();

    public static SolidColorBrush Solid(Color c)
    {
        uint key = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
        if (Cache.TryGetValue(key, out var b)) return b;
        if (Cache.Count > 4096) Cache.Clear();   // при плавных переходах цветов много, держим не все
        b = new SolidColorBrush(c);
        b.Freeze();
        Cache[key] = b;
        return b;
    }

    public static SolidColorBrush Solid(byte a, byte r, byte g, byte b) => Solid(Color.FromArgb(a, r, g, b));
}

// Цвет, который меняется плавно, и список свойств, куда его ставить (Background, Fill, Stroke...).
// Use(элемент, свойство) — элемент берёт текущий цвет и дальше получает каждый новый. Если свойство потом выставили
// в другую кисть (например, плашка громкости без звука красится красным), LiveBrush его больше не трогает.
// Элементы держатся слабыми ссылками: пересобранные полоски эквалайзера и строки микшера не держатся в памяти.
internal sealed class LiveBrush
{
    private readonly List<(WeakReference<DependencyObject> el, DependencyProperty prop)> _uses = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private Color _from, _to;
    private double _start, _dur;
    private bool _hooked;

    public Color Color { get; private set; }
    public SolidColorBrush Brush => SafeBrush.Solid(Color);
    public event Action<Color>? Changed;   // для градиентов: подписчик сам собирает новую замороженную кисть

    public LiveBrush(Color c) => Color = c;

    public void Use(DependencyObject el, DependencyProperty prop)
    {
        if (_uses.Count > 64) Prune();
        _uses.Add((new WeakReference<DependencyObject>(el), prop));
        el.SetValue(prop, Brush);
    }

    // сразу, без перехода
    public void Set(Color c)
    {
        Stop();
        Apply(c);
    }

    // плавно за ms миллисекунд (EaseOut, как у прежних ColorAnimation с CubicEase)
    public void To(Color c, double ms)
    {
        if (ms <= 1) { Set(c); return; }
        if (c == Color && !_hooked) return;
        _from = Color; _to = c;
        _start = Clock.Elapsed.TotalMilliseconds;
        _dur = ms;
        if (!_hooked) { CompositionTarget.Rendering += Tick; _hooked = true; }
    }

    private void Stop()
    {
        if (!_hooked) return;
        CompositionTarget.Rendering -= Tick;
        _hooked = false;
    }

    private void Tick(object? sender, EventArgs e)
    {
        double t = Math.Clamp((Clock.Elapsed.TotalMilliseconds - _start) / _dur, 0, 1);
        double k = 1 - Math.Pow(1 - t, 3);
        Apply(t >= 1 ? _to : Lerp(_from, _to, k));
        if (t >= 1) Stop();
    }

    private void Apply(Color c)
    {
        if (c == Color) return;
        var old = Color;
        Color = c;
        var now = Brush;
        for (int i = _uses.Count - 1; i >= 0; i--)
        {
            var (wr, prop) = _uses[i];
            if (!wr.TryGetTarget(out var el)) { _uses.RemoveAt(i); continue; }
            // свойство поменяли в обход нас: оно теперь чужое
            if (!Ours(el.GetValue(prop), old)) { _uses.RemoveAt(i); continue; }
            el.SetValue(prop, now);
        }
        Changed?.Invoke(c);
    }

    private static bool Ours(object v, Color c) => v is SolidColorBrush { IsFrozen: true } sb && sb.Color == c;

    private void Prune() => _uses.RemoveAll(u => !u.el.TryGetTarget(out var el) || !Ours(el.GetValue(u.prop), Color));

    private static Color Lerp(Color a, Color b, double k)
    {
        byte L(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * k), 0, 255);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
}
