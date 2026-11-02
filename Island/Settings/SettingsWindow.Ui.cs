using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Island;

// Шаг 98: куски раздела «Внешний вид» (Pages.cs): положение, цвета, стекло, шрифт. В конце «Системы» — авторы и плашка «Нашли баг?».
public sealed partial class SettingsWindow
{
    private static readonly string[] Swatches =
    {
        "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#FFFFFF"
    };

    private void PositionCard(StackPanel p)
    {
        StackPanel c = Card(p, "ПОЛОЖЕНИЕ");
        Choice(c, "Где стоит остров", new[] { "По центру", "Слева", "Справа", "Свободно" },
            () => _s.IslandPos == 4 ? 3 : Math.Clamp(_s.IslandPos, 0, 2), v => _s.IslandPos = v == 3 ? 4 : v);
        Row(c, "Перетаскивать с Ctrl", "Зажми Ctrl и тащи остров мышью вдоль верхнего края. У центра и краёв он прилипает, в другом месте остаётся там, куда поставил. Пузыри и значки едут за ним",
            _s.CtrlDrag, v => _s.CtrlDrag = v);
    }

    private void ColorsCard(StackPanel p)
    {
        StackPanel c = Card(p, "ЦВЕТА");
        Choice(c, "Цвет интерфейса", new[] { "Из обложки", "Акцент Windows", "Свой", "Белый" },
            () => !_s.AccentUi ? 3 : Math.Clamp(_s.AccentSource, 0, 2),
            v => { _s.AccentUi = v != 3; if (v != 3) _s.AccentSource = v; });
        InfoRow(c, "Прогресс, ползунки, громкость, кромка острова и переключатели в настройках. «Из обложки»: пока ничего не играет — акцент Windows");
        SwatchRow(c, "Свой цвет интерфейса", () => _s.AccentHex, v => { _s.AccentHex = v; _s.AccentUi = true; _s.AccentSource = 2; });
        Choice(c, "Цвет эквалайзера", new[] { "Как интерфейс", "Свой", "Белый" }, () => Math.Clamp(_s.EqColor, 0, 2), v => _s.EqColor = v);
        SwatchRow(c, "Свой цвет эквалайзера", () => _s.EqHex, v => { _s.EqHex = v; _s.EqColor = 1; });
    }

    private void SeekRows(StackPanel c)
    {
        Choice(c, "Полоска перемотки", MainWindow.SeekStyleNames,
            () => _s.SeekStyle is >= 0 and <= 4 ? _s.SeekStyle : _s.WaveSeek ? 1 : 0,
            v => { _s.SeekStyle = v; _s.WaveSeek = v is 1 or 3 or 4; });
        InfoRow(c, "Линия: тонкая с ползунком. Волна: как голосовые в Telegram. Капсула: толстая, без ползунка, как в iOS. Точки. Змейка: волнистая линия течёт, пока играет музыка");
    }

    private void GlassCard(StackPanel p)
    {
        StackPanel c = Card(p, "LIQUID GLASS");
        Row(c, "Стеклянный остров", "Под островом видно размытое то, что на экране за ним, с бликом и светлой кромкой. Пока включено, остров не виден на скриншотах, в записи экрана и в OBS — так Windows не даёт ему попасть в собственное стекло",
            _s.Glass, v => _s.Glass = v);
        SliderRow(c, "Размытие", "Слева: фон почти чёткий, справа: мягкое пятно цвета", () => _s.GlassBlur, v => _s.GlassBlur = v);
        SliderRow(c, "Прозрачность", "Слева: тёмное стекло, текст читается на любом фоне. Справа: почти прозрачное", () => _s.GlassClear, v => _s.GlassClear = v);
    }

    private void FontCard(StackPanel p)
    {
        StackPanel c = Card(p, "ШРИФТ");
        {
            // 3.9.5: три готовых варианта — как было до Inter (Segoe UI), Inter и SF Pro из системы; дальше загруженные файлы
            var fonts = MainWindow.UserFonts();
            bool sf = UiFonts.SfInstalled;
            var names = new[] { "Как было (Segoe UI)", "Inter", sf ? "SF Pro" : "SF Pro — не установлен" }
                .Concat(fonts.Select(MainWindow.FontName)).ToArray();
            Choice(c, "Шрифт", names,
                () =>
                {
                    if (_s.FontFile == UiFonts.SegoeKey) return 0;
                    if (_s.FontFile == UiFonts.SfKey) return 2;
                    int i = fonts.FindIndex(f => string.Equals(f, _s.FontFile, StringComparison.OrdinalIgnoreCase));
                    return i >= 0 ? i + 3 : 1;
                },
                v =>
                {
                    _s.FontFile = v switch { 0 => UiFonts.SegoeKey, 2 => UiFonts.SfKey, >= 3 when v - 3 < fonts.Count => fonts[v - 3], _ => "" };
                    if (v == 2 && !UiFonts.SfInstalled)
                        Dispatcher.BeginInvoke(() => MacDialog.Info(this, Loc.T(
                            "SF Pro пока не установлен в Windows, поэтому остров пишет Inter.\n\nСкачай SF Pro с сайта Apple (кнопка «Где взять SF Pro» ниже), " +
                            "открой .dmg архиватором (7-Zip), установи файлы SF-Pro-*.otf двойным щелчком и перезапусти ShyFlow. " +
                            "Или загрузи один .otf кнопкой «Загрузить шрифт».",
                            "SF Pro isn't installed in Windows yet, so the island uses Inter.\n\nDownload SF Pro from Apple (the “Get SF Pro” button below), " +
                            "open the .dmg with an archiver (7-Zip), install the SF-Pro-*.otf files and restart ShyFlow. Or load one .otf with “Load font”.")));
                });
        }
        InfoRow(c, "Как было: Segoe UI Variable, шрифт Windows. Inter: свободный шрифт, ближе всего к SF Pro, уже внутри ShyFlow. " +
                   "SF Pro: шрифт Apple — положить его в программу нельзя по лицензии, но если установить его в Windows самому, ShyFlow его подхватит. " +
                   "Шрифт действует на остров сразу, на остальные окна — когда откроешь их заново");
        ActionRow(c, "Где взять SF Pro", UiFonts.SfInstalled ? "Установлен в Windows — можно выбрать выше" : "Официальная страница шрифтов Apple: developer.apple.com/fonts",
            "Открыть", () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UiFonts.SfUrl) { UseShellExecute = true }); }
                catch (Exception ex) { MacDialog.Info(this, ex.Message); }
            });
        ActionRow(c, "Загрузить шрифт", "Файл .ttf, .otf или .ttc. Копия хранится в папке ShyFlow, после загрузки шрифт сразу выбран", "Загрузить", PickFont);
        Row(c, "И в плеере на весь экран", "Название, исполнитель и слова песни своим шрифтом", _s.FontInPlayer, v => _s.FontInPlayer = v);
        ActionRow(c, "Удалить выбранный шрифт", "Убрать файл шрифта и вернуть Inter", "Удалить", RemoveFont);
    }

    // ряд кружков-цветов и поле HEX: клик по кружку или Enter в поле — цвет выбран сразу
    private void SwatchRow(StackPanel card, string title, Func<string> get, Action<string> set)
    {
        Divider(card);
        var root = new StackPanel { Margin = new Thickness(16, 10, 14, 11) };
        root.Children.Add(RowTitle(title));
        var line = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        var hex = new TextBox
        {
            Width = 86, FontSize = 12.5, Foreground = Brushes.White, Background = ChipBg, BorderThickness = new Thickness(0),
            CaretBrush = Brushes.White, Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0), Text = get()
        };
        var rings = new List<(Ellipse ring, string hex)>();
        void Mark(string cur)
        {
            foreach (var (ring, h) in rings)
                ring.Stroke = string.Equals(h, cur, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent;
        }
        void Commit(string v)
        {
            v = v.Trim();
            if (!v.StartsWith('#')) v = "#" + v;
            if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9A-Fa-f]{6}$")) { hex.Text = get(); return; }
            v = v.ToUpperInvariant();
            set(v);
            hex.Text = v;
            Mark(v);
            _s.Save(); _changed();
        }
        foreach (var h in Swatches)
        {
            var col = MainWindow.ParseHex(h, Colors.White);
            var g = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var ring = new Ellipse { StrokeThickness = 2, Stroke = Brushes.Transparent };
            g.Children.Add(ring);
            g.Children.Add(new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(col) });
            string hh = h;
            g.MouseLeftButtonUp += (_, e) => { Commit(hh); e.Handled = true; };
            rings.Add((ring, h));
            line.Children.Add(g);
        }
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(hex.Text); e.Handled = true; } };
        hex.LostFocus += (_, _) => Commit(hex.Text);
        line.Children.Add(hex);
        root.Children.Add(line);
        Mark(get());
        card.Children.Add(root);
    }

    // конец «Системы»: кто сделал ShyFlow и куда писать о баге
    private void BuildCredits(StackPanel p)
    {
        // плашка «Нашли баг?»: клик открывает чат поддержки (Ui\SupportWindow.cs)
        var accent = Color.FromRgb(0xFF, 0x9F, 0x0A);
        var bug = new SBorder
        {
            CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(16, 13, 14, 13), Cursor = Cursors.Hand,
            Background = new LinearGradientBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B), Color.FromArgb(0x12, accent.R, accent.G, accent.B), 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)), BorderThickness = new Thickness(1)
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new VIcon { Text = "message-circle", FontSize = 18, Foreground = new SolidColorBrush(accent), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = Loc.T("Нашли баг?", "Found a bug?"), Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
        texts.Children.Add(RowSub(Loc.T("Сообщите нам — нажмите, откроется чат поддержки", "Tell us — click to open the support chat")));
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        var go = new VIcon { Text = "chevron-right", FontSize = 16, Foreground = Gray, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(go, 2);
        row.Children.Add(go);
        bug.Child = row;
        bug.MouseLeftButtonUp += (_, e) => { e.Handled = true; SupportWindow.ShowSupport(); };
        p.Children.Add(bug);

        // авторы
        var who = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 4) };
        who.Children.Add(new TextBlock
        {
            Text = Loc.T("Создано командой Shykoe", "Made by the Shykoe team"), Foreground = Brushes.White, FontSize = 13.5,
            FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = UiFonts.Display
        });
        who.Children.Add(new TextBlock
        {
            Text = "ShyFlow " + AppInfo.CurrentVersion + "  ·  © " + DateTime.Now.Year + " Shykoe", Foreground = Gray, FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
        });
        who.Children.Add(new TextBlock
        {
            Text = Loc.T("Шрифт Inter — Rasmus Andersson, SIL Open Font License", "Inter typeface by Rasmus Andersson, SIL Open Font License"),
            Foreground = Gray, FontSize = 10.5, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0)
        });
        p.Children.Add(who);
    }
}
