using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace Island;

// Быстрые действия и выбор устройств звука и микрофона.
public partial class MainWindow
{
    // ============================================================
    //  БЫСТРЫЕ ДЕЙСТВИЯ
    // ============================================================
    private bool _actionsOpen;

    private void Actions_Click(object s, RoutedEventArgs e) { _actionsOpen = true; _pocketOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }
    private void ActionsClose_Click(object s, RoutedEventArgs e) { _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }

    private void CloseActions() { _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }

    // Вид (шаг 36): плитки как в Пункте управления. Сверху кружок со значком, снизу подпись.
    // Переключатели (тёмная тема, без звука) не закрывают панель: кружок становится белым, значок тёмным, и видно, что включено.
    //
    // Шаг 71: плиток стало 20, и в пять колонок подписи обрезались («Блокиро…», «Калькул…»). Теперь четыре колонки,
    // а на панели только закреплённые плитки (по умолчанию семь) и «Ещё». «Ещё» показывает все действия, их можно листать
    // колесом. Правый клик по плитке закрепляет её или убирает. Список закреплённых хранится в настройках (ActionTiles).
    private const int ActCols = 4;
    private const double ActRowH = 78.5;   // плитка 70.5 + поля по 4
    private int ActionsRows => Math.Clamp((int)Math.Ceiling(ActionsGrid.Children.Count / (double)ActCols), 1, 4);
    private double ActionsH => 58 + ActionsRows * ActRowH;   // высота острова: поля, заголовок, ряды плиток
    private DateTime _tileMuteAt = DateTime.MinValue;

    private sealed class Tile
    {
        public SBorder Circle = null!;
        public VIcon Icon = null!;
        public LiveBrush CircleBg = null!, IconFg = null!;   // шаг 79: замороженные кисти (Ui\LiveBrush.cs)
        public Func<bool>? IsOn;
        public string IconOff = "", IconOn = "";
        public bool Shown;
    }
    private readonly List<Tile> _tiles = new();

    // действие для плитки: Id не переводится и хранится в настройках
    private sealed record ActSpec(string Id, string Label, string Icon, Action Act, Func<bool>? IsOn = null, string? IconOn = null);
    private readonly List<ActSpec> _acts = new();
    private bool _actsAll;   // показаны все действия, а не только закреплённые
    private static readonly string[] DefaultActTiles = { "shot", "theme", "mute", "timer", "mixer", "collection", "focus" };

    private List<string> PinnedActs() =>
        (_cfg.ActionTiles ?? DefaultActTiles.ToList()).Where(id => _acts.Any(a => a.Id == id)).Distinct().ToList();

    private static readonly Color TileC = Color.FromArgb(0x1A, 255, 255, 255);
    private static readonly Color TileHoverC = Color.FromArgb(0x2C, 255, 255, 255);
    private static readonly Color CircleOffC = Color.FromArgb(0x26, 255, 255, 255);
    private static readonly Color IconOnC = Color.FromRgb(0x1C, 0x1C, 0x1E);

    private void BuildActions()
    {
        _acts.Clear();

        // launch: открывает программу и закрывает панель. Ошибка запуска не роняет остров, а пишется в crash.log
        Action Launch(string file, string? args = null) => () =>
        {
            CloseActions();
            var psi = args == null ? new ProcessStartInfo(file) : new ProcessStartInfo(file, args);
            psi.UseShellExecute = true;
            Process.Start(psi);
        };
        void Add(string id, string label, string icon, Action act, Func<bool>? isOn = null, string? iconOn = null) =>
            _acts.Add(new ActSpec(id, label, icon, act, isOn, iconOn));

        Add("shot", "Скриншот", "camera", Launch("ms-screenclip:"));
        Add("theme", "Тёмная тема", "sun", ToggleWindowsTheme, () => !WindowsLight(), "moon");
        Add("mute", "Без звука", "volume-2", () =>
        {
            if (_dev == null) return;
            _tileMuteAt = DateTime.UtcNow;
            _dev.AudioEndpointVolume.Mute = !_dev.AudioEndpointVolume.Mute;
        }, () => _dev?.AudioEndpointVolume.Mute == true, "volume-x");
        Add("lock", "Блокировка", "lock", Launch("rundll32.exe", "user32.dll,LockWorkStation"));
        Add("timer", "Таймер", "hourglass", () => OpenTimer(0));
        Add("watch", "Секундомер", "timer", () => OpenTimer(1));
        Add("mixer", "Микшер", "sliders-vertical", OpenMixer);
        Add("devices", "Устройства звука", "headphones", () => OpenDevices());
        Add("calc", "Калькулятор", "calculator", Launch("calc.exe"));
        Add("taskmgr", "Диспетчер задач", "activity", Launch("taskmgr.exe"));
        Add("explorer", "Проводник", "folder-open", Launch("explorer.exe"));
        Add("settings", "Параметры", "settings", Launch("ms-settings:"));
        // шаг 49
        Add("notifs", "Уведомления", "bell", OpenCenter);
        Add("stats", "Статистика", "chart-column", () => { CloseActions(); OpenStats(); });
        Add("story", "Карточка для сторис", "share", () => { CloseActions(); MakeStory(); });
        Add("recap", "Итоги месяца", "sparkles", () => { CloseActions(); MakeRecap(DateTime.Today.Year, DateTime.Today.Month); });   // шаг 73 (Recap.cs)
        Add("year", "Итоги года", "trophy", () => { CloseActions(); MakeYear(DateTime.Today.Month == 1 ? DateTime.Today.Year - 1 : DateTime.Today.Year); });   // шаг 77 (Year.cs)
        Add("mini", "Мини-плеер", "music", () => { CloseActions(); MiniPlayerWindow.Toggle(this); });   // шаг 77
        Add("collection", "Коллекция", "star", () => { CloseActions(); CollectionWindow.ShowCollection(); });
        Add("notepad", "Блокнот", "sticky-note", () => { CloseActions(); NotepadWindow.ShowNotepad(); });   // шаг 53
        // шаг 54
        Add("translate", "Переводчик", "languages", () => OpenTranslator(null, IntPtr.Zero));
        Add("focus", "Фокус", "zap", ToggleFocus, () => _focusOn);
        Add("calendar", "Календарь", "calendar", () => { CloseActions(); CalendarWindow.ShowCalendar(); });
        Add("convert", "Конвертер", "arrow-left-right", () => { CloseActions(); PickAndConvert(); });   // шаг 79 (Convert.cs)
        RenderActionTiles();
    }

    // плитки на панели: закреплённые и «Ещё», или все действия и «Меньше»
    private void RenderActionTiles()
    {
        ActionsGrid.Children.Clear();
        _tiles.Clear();
        var pinned = PinnedActs();
        var list = _actsAll ? _acts : pinned.Select(id => _acts.First(a => a.Id == id)).ToList();
        foreach (var a in list) AddTile(a, pinned.Contains(a.Id));
        // «Ещё» всегда последняя: так панель не бывает пустой, даже если открепить всё
        AddTile(_actsAll
            ? new ActSpec("", Loc.T("Меньше", "Less"), "chevron-up", () => SetActionsAll(false))
            : new ActSpec("", Loc.T("Ещё", "More"), "layout-grid", () => SetActionsAll(true)), false);
        ActionsTitle.Text = _actsAll ? Loc.T("Все действия", "All actions") : Loc.T("Быстрые действия");
        RefreshActions(false);
    }

    private void SetActionsAll(bool all)
    {
        _actsAll = all;
        RenderActionTiles();
        ActionsScroll.ScrollToTop();
        ApplyMode(Rest());          // остров пружиной меняет высоту под новое число рядов
        Droplets(ActionItems());    // плитки «вливаются», как при открытии панели
    }

    // правый клик по плитке: закрепить на панели или убрать с неё
    private void TogglePinAct(string id)
    {
        var list = PinnedActs();
        if (!list.Remove(id)) list.Add(id);
        _cfg.ActionTiles = list;
        _cfg.Save();
        RenderActionTiles();
        if (_mode == Mode.Actions) ApplyMode(Rest());
    }

    private void AddTile(ActSpec spec, bool pinned)
    {
        var tile = new Tile { IsOn = spec.IsOn, IconOff = spec.Icon, IconOn = spec.IconOn ?? spec.Icon };
        tile.CircleBg = new LiveBrush(CircleOffC);
        tile.IconFg = new LiveBrush(Colors.White);
        tile.Icon = new VIcon
        {
            Text = spec.Icon, FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        tile.Circle = new SBorder
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = tile.Icon,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(9, 8, 0, 0)
        };
        tile.IconFg.Use(tile.Icon, VIcon.ForegroundProperty);
        tile.CircleBg.Use(tile.Circle, Border.BackgroundProperty);
        string label = Loc.T(spec.Label);
        var text = new TextBlock
        {
            Text = label, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 13.5, MaxHeight = 27,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 8, 7)
        };
        var face = new Grid();
        face.Children.Add(tile.Circle);
        face.Children.Add(text);
        // в списке всех действий закреплённые отмечены точкой в углу
        if (_actsAll && pinned)
            face.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 5, Height = 5, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 9, 0)
            });
        var bg = new LiveBrush(TileC);
        var chip = new SBorder
        {
            Child = face, CornerRadius = new CornerRadius(16), Margin = new Thickness(4), Height = ActRowH - 8,
            Cursor = Cursors.Hand
        };
        bg.Use(chip, Border.BackgroundProperty);
        if (spec.Id.Length > 0)
        {
            chip.ToolTip = label + "\n" + Loc.T(pinned ? "Правый клик: убрать с панели" : "Правый клик: закрепить на панели",
                                                 pinned ? "Right-click: remove from panel" : "Right-click: pin to panel");
            var item = new MenuItem { Header = Loc.T(pinned ? "Убрать с панели" : "Закрепить на панели", pinned ? "Remove from panel" : "Pin to panel") };
            item.Click += (_, _) => TogglePinAct(spec.Id);
            chip.ContextMenu = new ContextMenu { Items = { item } };
        }
        void Bg(Color c, int ms) => bg.To(c, ms);
        void Press(double to, int ms, IEasingFunction ease)
        {
            var t = Get(chip);
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        HoldOnPress(chip);   // шаг 70: плитка сжимается при нажатии, клик у края больше не теряется (Tap.cs)
        chip.MouseEnter += (_, _) => Bg(TileHoverC, 140);
        chip.MouseLeave += (_, _) => { Bg(TileC, 220); Press(1, 300, Spr()); };
        chip.MouseLeftButtonDown += (_, e) => { Press(0.93, 110, new CubicEase { EasingMode = EasingMode.EaseOut }); e.Handled = true; };
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Press(1, 460, Spr());
            Pop(tile.Circle, 0.7, 480);
            try { spec.Act(); }
            catch (Exception ex) { App.Log("Action " + spec.Label, ex); }
            if (tile.IsOn != null) RefreshActions();
        };
        _tiles.Add(tile);
        ActionsGrid.Children.Add(chip);
    }

    // переключатели показывают текущее состояние: белый кружок и тёмный значок = включено
    private void RefreshActions(bool animate = true)
    {
        var d = TimeSpan.FromMilliseconds(animate ? 220 : 1);
        foreach (var t in _tiles)
        {
            if (t.IsOn == null) continue;
            bool on;
            try { on = t.IsOn(); } catch { on = false; }
            if (t.Shown == on && animate) continue;
            t.Shown = on;
            t.Icon.Text = on ? t.IconOn : t.IconOff;   // значок перетекает в новый (VIcon)
            t.CircleBg.To(on ? Colors.White : CircleOffC, d.TotalMilliseconds);
            t.IconFg.To(on ? IconOnC : Colors.White, d.TotalMilliseconds);
        }
    }

    private static bool WindowsLight()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return Convert.ToInt32(k?.GetValue("AppsUseLightTheme", 1) ?? 1) != 0;
    }

    private void ToggleWindowsTheme()
    {
        using var k = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true);
        if (k == null) return;
        int cur = Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1));
        int nv = cur == 0 ? 1 : 0;
        k.SetValue("AppsUseLightTheme", nv, RegistryValueKind.DWord);
        k.SetValue("SystemUsesLightTheme", nv, RegistryValueKind.DWord);
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 2, 2000, out _);
        // из панели действий плашку не показываем: тема видна на самой плитке, а плашка закрыла бы панель
        if (_mode != Mode.Actions)
            ShowToast(nv == 0 ? "moon" : "sun", nv == 0 ? "Тема Windows: тёмная" : "Тема Windows: светлая", null, "key");
    }

    // ============================================================
    //  ЗВУК И МИКРОФОН: смена устройств по умолчанию прямо из острова
    // ============================================================
    private bool _devicesOpen;

    private void OpenDevices()
    {
        _pocketOpen = false; _actionsOpen = false; _autoOpened = false;
        _devicesOpen = true;
        RenderDevices();
        ApplyMode(Rest());
    }

    private void DevicesClose_Click(object s, RoutedEventArgs e)
    {
        StopDevMeters();   // шаг 80: микрофоны больше не слушаем
        _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false;
        ApplyMode(Rest());
    }

    private TextBlock DevHeader(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)), FontSize = 11.5,
        FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 6)
    };

    // Шаг 80: все устройства (раньше первые 4 — нужный микрофон мог не влезть), настоящие сверху, виртуальные отдельно внизу.
    // У каждого полоска уровня: скажи что-нибудь — и видно, какой микрофон тебя слышит. Микрофоны для этого слушаются, только пока
    // панель открыта; Bluetooth-микрофон не трогаем (наушники переключились бы в режим гарнитуры и стали бы звучать хуже).
    private int DevicesH { get; set; } = 232;

    private void RenderDevices()
    {
        StopDevMeters();
        DevOut.Children.Clear();
        DevIn.Children.Clear();
        var outs = AudioSwitch.List(DataFlow.Render);
        var ins = AudioSwitch.List(DataFlow.Capture);
        double hOut = DevColumn(DevOut, Loc.T("ЗВУК", "SOUND"), outs, false);
        double hIn = DevColumn(DevIn, Loc.T("МИКРОФОН", "MICROPHONE"), ins, true);

        // выбран виртуальный микрофон (Virtual Desktop, Steam, VB-Cable): программы слышат его, а не тебя
        var defIn = ins.FirstOrDefault(d => d.IsDefault);
        bool warn = defIn != null && defIn.Virtual && ins.Any(d => !d.Virtual);
        DevHint.Text = warn ? Loc.T("Сейчас выбран виртуальный микрофон «") + defIn!.Name + Loc.T("» — программы и ShyFlow тебя не слышат. Выбери свой справа: скажи что-нибудь, у твоего микрофона побежит полоска.") : "";
        DevHint.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
        DevicesH = (int)Math.Clamp(24 + 34 + 8 + (warn ? 52 : 0) + Math.Max(hOut, hIn), 232, 400);   // окно острова 420 (Display.cs)
        if (_mode == Mode.Devices) Go(480, DevicesH, 30);
        StartDevMeters();
    }

    // колонка: заголовок, настоящие устройства, «Виртуальные»; возвращает примерную высоту (для размера острова)
    private double DevColumn(StackPanel col, string title, List<AudioSwitch.Dev> list, bool capture)
    {
        col.Children.Add(DevHeader(title));
        double h = 22;
        bool virtHeader = false;
        foreach (var d in list)
        {
            if (d.Virtual && !virtHeader && list.Any(x => !x.Virtual))
            {
                virtHeader = true;
                var vh = DevHeader(Loc.T("ВИРТУАЛЬНЫЕ", "VIRTUAL"));
                vh.Margin = new Thickness(4, 6, 0, 6);
                vh.Opacity = 0.8;
                col.Children.Add(vh);
                h += 28;
            }
            col.Children.Add(DevChip(d, capture));
            h += 54;
        }
        if (list.Count == 0)
        {
            col.Children.Add(new TextBlock { Text = Loc.T("Нет устройств"), Foreground = ShyStyle.Gray, FontSize = 12, Margin = new Thickness(4, 2, 0, 0) });
            h += 20;
        }
        return h;
    }

    // ---------- полоски уровня ----------
    private readonly List<(MMDevice dev, WasapiCapture? cap, Border bar, double width)> _devMeters = new();
    private readonly Dictionary<Border, double> _devLevel = new();
    private DispatcherTimer? _devMeterTimer;

    private void AddDevMeter(string id, bool capture, bool bluetooth, Border bar, double width)
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            var dev = en.GetDevice(id);
            WasapiCapture? cap = null;
            if (capture)
            {
                // у микрофона уровень есть, только пока его кто-то слушает: слушаем сами, пока открыта панель
                if (bluetooth) return;
                cap = new WasapiCapture(dev) { ShareMode = AudioClientShareMode.Shared };
                cap.DataAvailable += (_, _) => { };
                cap.StartRecording();
            }
            _devMeters.Add((dev, cap, bar, width));
        }
        catch (Exception ex) { App.LogOnce("Devices.Meter", ex); }   // занят другой программой, отключился и т.п.: без полоски
    }

    private void StartDevMeters()
    {
        if (_devMeters.Count == 0) return;
        _devMeterTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(45), DispatcherPriority.Render, (_, _) => DevMeterTick(), Dispatcher);
        _devMeterTimer.Start();
    }

    private void DevMeterTick()
    {
        if (_mode != Mode.Devices || !_devicesOpen || _hidden) { StopDevMeters(); return; }
        foreach (var (dev, _, bar, width) in _devMeters)
        {
            double v = 0;
            try { v = dev.AudioMeterInformation.MasterPeakValue; } catch { }
            double shown = _devLevel.TryGetValue(bar, out var s) ? s : 0;
            double target = Math.Sqrt(Math.Clamp(v, 0, 1));   // тихую речь тоже видно
            shown += (target - shown) * (target > shown ? 0.6 : 0.18);   // вверх сразу, вниз плавно
            _devLevel[bar] = shown;
            bar.Width = Math.Max(0, width * shown);
        }
    }

    private void StopDevMeters()
    {
        _devMeterTimer?.Stop();
        foreach (var (dev, cap, _, _) in _devMeters)
        {
            try { cap?.StopRecording(); } catch { }
            try { cap?.Dispose(); } catch { }
            try { dev.Dispose(); } catch { }
        }
        _devMeters.Clear();
        _devLevel.Clear();
    }

    private Border DevChip(AudioSwitch.Dev d, bool capture)
    {
        var fg = d.IsDefault ? ShyStyle.Ink : Brushes.White;
        var tb = new TextBlock
        {
            Text = d.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = fg, FontWeight = d.IsDefault ? FontWeights.SemiBold : FontWeights.Normal
        };
        // подпись: что это за устройство, Bluetooth, для звонков (если звонки у Windows идут на другое устройство)
        var tags = new List<string>();
        if (d.Sub.Length > 0) tags.Add(d.Sub);
        if (d.Bluetooth) tags.Add("Bluetooth");
        if (d.CommDefault && !d.IsDefault) tags.Add(Loc.T("для звонков"));
        var sub = new TextBlock
        {
            Text = string.Join(" · ", tags), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.62, Foreground = fg,
            Margin = new Thickness(0, 1, 0, 0), Visibility = tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed
        };
        const double meterW = 188;   // колонка 217 px минус поля карточки
        var bar = new Border
        {
            Height = 3, Width = 0, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left,
            Background = d.IsDefault ? ShyStyle.Ink : Brushes.White, Opacity = 0.85
        };
        var meter = new Grid { Height = 3, Margin = new Thickness(0, 6, 0, 0), MaxWidth = meterW, HorizontalAlignment = HorizontalAlignment.Left, Width = meterW };
        meter.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(d.IsDefault ? Color.FromArgb(0x22, 0, 0, 0) : Color.FromArgb(0x22, 255, 255, 255)) });
        meter.Children.Add(bar);
        var body = new StackPanel();
        body.Children.Add(tb);
        body.Children.Add(sub);
        bool hasMeter = !(capture && d.Bluetooth);
        if (hasMeter) body.Children.Add(meter);
        var chip = new SBorder
        {
            Child = body, CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 7, 11, 8), Margin = new Thickness(0, 0, 0, 5),
            Background = d.IsDefault ? Brushes.White : ShyStyle.Tile,   // шаг 79: выбранное устройство белым, как включённые плитки
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = d.Sub.Length > 0 ? d.Name + " — " + d.Sub : d.Name,
            Opacity = d.Virtual && !d.IsDefault ? 0.75 : 1
        };
        if (hasMeter) AddDevMeter(d.Id, capture, d.Bluetooth, bar, meterW);
        if (!d.IsDefault)
        {
            chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255));
            chip.MouseLeave += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));
        }
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (d.IsDefault) return;
            Pop(chip, 0.88, 420);
            if (AudioSwitch.SetDefault(d.Id))
            {
                // громкость и значок микрофона теперь следят за новым устройством
                try { if (_dev != null) _dev.AudioEndpointVolume.OnVolumeNotification -= OnVol; } catch { }
                InitVolume();
                InitMicMute();
            }
            RenderDevices();
        };
        return chip;
    }
}
