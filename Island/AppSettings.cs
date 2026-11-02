using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Island;

public sealed class AppSettings
{
    public bool Notifications { get; set; } = true;
    public bool VolumeToasts { get; set; } = true;
    public bool BrightnessToasts { get; set; } = true;
    public bool PlaybackToasts { get; set; } = true;
    public bool CopyToast { get; set; } = true;
    public bool Equalizer { get; set; } = true;
    public bool CoverTint { get; set; } = true;
    public bool Lyrics { get; set; } = true;
    public bool LyricsKaraoke { get; set; } = true;
    public bool LyricsExtra { get; set; } = true;
    public bool LyricsPlain { get; set; } = true;
    public bool LyricsBlur { get; set; } = true;
    public bool LyricsGlow { get; set; } = true;
    public bool LyricsInFull { get; set; } = true;
    public bool WheelControl { get; set; } = true;
    public bool Gestures { get; set; } = true;
    public bool PlayerViz { get; set; } = true;
    public bool PlayerGlow { get; set; } = true;
    public bool PlayerLiveBg { get; set; } = true;   // шаг 77: живой фон в плеере на весь экран
    public double MiniX { get; set; } = -99999;      // шаг 77: где мини-плеер (меньше -90000: ещё не двигали, справа внизу)
    public double MiniY { get; set; } = -99999;
    public string YearShown { get; set; } = "";      // шаг 77: за какой год уже предлагали итоги
    public bool CoverFlip { get; set; } = true;
    public bool LiquidFx { get; set; } = true;
    public bool PressJelly { get; set; } = true;       // шаг 80: кнопки и плитки мнутся при нажатии (Ui\PressFx.cs)
    public bool KaraokeMic { get; set; } = true;       // шаг 80: караоке слушает микрофон и считает очки (Player\PlayerKaraoke.cs)
    public bool KaraokeHintShown { get; set; }         // подсказка «лучше в наушниках» показана
    public int LiquidPower { get; set; } = 1;
    public bool AccentUi { get; set; } = true;
    public bool Cover3D { get; set; } = true;
    public bool LyricsTranslate { get; set; } = false;
    public int TranslateTo { get; set; } = 0;
    public bool SeekClick { get; set; } = true;
    public bool CopyProgress { get; set; } = true;
    public bool LetterAnim { get; set; } = false;
    public bool IslandLetters { get; set; } = false;
    public bool MiniProgress { get; set; } = true;
    public bool WaveSeek { get; set; } = true;
    public bool DiscordRpc { get; set; } = false;
    // гифка справа в развёрнутом острове
    public bool GifEnabled { get; set; } = true;
    public string GifPath { get; set; } = "";
    public bool GifRemoveBg { get; set; } = false;
    public bool GifHoles { get; set; } = false;         // вырезать цвет фона и внутри фигуры (между руками, в дырках)
    public string GifKeyColor { get; set; } = "auto";   // "auto" (цвет из угла) или #RRGGBB
    public int GifTolerance { get; set; } = 1;          // 0 мало, 1 средне, 2 сильно
    // true после кнопки «Больше не показывать» в конце приветствия; пока false, приветствие открывается при каждом запуске
    public bool HideWelcome { get; set; } = false;
    public bool ShowClock { get; set; } = true;
    public bool MicCamera { get; set; } = true;
    public bool KeyToasts { get; set; } = true;
    public bool NetworkToasts { get; set; } = true;
    public bool Downloads { get; set; } = true;
    public bool Captures { get; set; } = true;
    public bool Battery { get; set; } = true;
    public bool NoteReminders { get; set; } = true;
    public bool Hotkeys { get; set; } = true;
    public bool ClipboardHistory { get; set; } = true;
    public bool CollapseOnOutside { get; set; } = true;
    public bool Autostart { get; set; } = false;
    public int Language { get; set; } = 0;
    public int Monitor { get; set; } = 0;
    public int EqStyle { get; set; } = 0;
    public int CoverShape { get; set; } = 0;
    // остров прижат к верхнему краю экрана, как вырез на iPhone 13, с плавным переходом в рамку
    public bool NotchMode { get; set; } = false;
    public bool DuoMode { get; set; } = false;   // шаг 84: вертикальный остров справа, как iPhone Duo (Main\Duo.cs)
    public int DuoPlace { get; set; } = 0;       // шаг 88: 0 — правый верхний угол, 1 — посередине справа
    public bool CompactMini { get; set; } = false;   // шаг 88: в свёрнутом острове только обложка и эквалайзер, название при наведении
    public bool CallTimer { get; set; } = true;      // шаг 88: время звонка в пузыре микрофона (MicToggle.cs)
    // экономный режим: 0 выкл, 1 авто (от батареи или когда остров не успевает рисовать), 2 всегда
    public int PerfMode { get; set; } = 1;
    // шаг 97: игровой режим: 0 выкл, 1 спрашивать, 2 всегда; выбор «всегда» и «нет» по программам (MainGameMode.cs)
    public int GameMode { get; set; } = 1;
    public Dictionary<string, bool> GameChoices { get => _gameChoices; set => _gameChoices = value ?? new(); }   // в импортированном файле может быть null
    private Dictionary<string, bool> _gameChoices = new();
    // таймер: последняя выбранная длительность, секунды
    public int TimerLast { get; set; } = 300;
    // свой фон острова: картинка, гифка или видео (копия в %AppData%\Island), затемнение 0..3 и размытие 0..2
    public bool BgEnabled { get; set; } = false;
    public string BgPath { get; set; } = "";
    public int BgDim { get; set; } = 1;
    public int BgBlur { get; set; } = 0;
    // значки открытых приложений: 0 выкл, 1 слева от острова, 2 справа, 3 в развёрнутом острове; плашка, когда приложение запустилось
    public int AppsPlace { get; set; } = 2;
    public bool AppLaunchToast { get; set; } = true;
    // свой шрифт: файл в %AppData%\Island\fonts ("" = системный Segoe UI); FontInPlayer: и в плеере на весь экран
    public string FontFile { get; set; } = "";
    public bool FontInPlayer { get; set; } = true;
    // шаг 49: сколько секунд висит уведомление; шторка уведомлений (история); таймер и копирование отдельным пузырём рядом с островом
    public int NotifSeconds { get; set; } = 7;
    public bool NotifCenter { get; set; } = true;
    public bool BubbleSplit { get; set; } = true;
    // шаг 51: что показывать в пузыре, с какой стороны (0 справа, 1 слева), фон как у острова; фон дока программ как у острова
    public bool BubbleTimer { get; set; } = true;
    public bool BubbleWatch { get; set; } = true;
    public bool BubbleCopy { get; set; } = true;
    public int BubbleSide { get; set; } = 0;
    public bool BubbleIslandBg { get; set; } = true;
    public bool AppsIslandBg { get; set; } = true;
    // шаг 52: плашки и уведомления в пузыре рядом с островом, остров не раскрывается
    public bool EventsInBubble { get; set; } = false;
    // шаг 53: звук напоминания о заметках; пузырь всегда (время, дата или батарея); закреплённые программы в доке
    public bool ReminderSound { get; set; } = true;
    public bool BubbleAlways { get; set; } = false;
    public bool LiveExpand { get; set; } = true;       // шаг 80: пузырь раскрывается в карточку при наведении (Main\LiveActivity.cs)
    public bool RainAlert { get; set; } = true;        // шаг 80: «через 15 минут дождь» плашкой (Main\RainAlert.cs)
    public int BubbleIdle { get; set; } = 0;   // 0 время, 1 дата и время, 2 заряд батареи, 3 погода, 4 интернет (ServicesNetSpeed.cs)
    public List<string> PinnedApps { get; set; } = new();
    // шаг 71: плитки на панели быстрых действий (Id из Actions.cs); null = набор по умолчанию
    public List<string>? ActionTiles { get; set; }
    // шаг 73: за какой месяц уже предлагали итоги ("2026-09"); какие советы уже показаны
    public string RecapShown { get; set; } = "";
    public List<string> TipsShown { get; set; } = new();
    public bool Tips { get; set; } = true;
    // шаг 74: прятаться в полноэкранных приложениях; положение сверху: 0 по центру, 1 слева, 2 справа
    public bool HideFullscreen { get; set; } = true;
    public int FsStyle { get; set; } = 0;
    public int DockBottom { get; set; } = 0;   // шаг 76: док программ снизу экрана: 0 выкл, 1 остров, 2 вырез   // шаг 76: 0 тонкая полоска, 1 прятать совсем
    public int IslandPos { get; set; } = 0;
    // шаг 54: переводчик (куда переводить), фокус (минуты работы и перерыва, уведомления в шторку), погода (город, "" = по IP),
    // праздники в острове, картинки в истории буфера
    public int TranslateQuickTo { get; set; } = 0;
    public int FocusMin { get; set; } = 25;
    public int BreakMin { get; set; } = 5;
    public bool FocusQuiet { get; set; } = true;
    public string WeatherCity { get; set; } = "";
    public bool HolidayToasts { get; set; } = true;
    public string HolidayShown { get; set; } = "";
    public bool ClipboardImages { get; set; } = true;
    // шаг 68: прогресс загрузки игры в Steam в пузыре (Main\SteamDl.cs)
    public bool SteamDownloads { get; set; } = true;
    // шаг 67: режим стримера — чат Twitch под островом и уведомления без текста (Main\Streamer.cs)
    public bool StreamerMode { get; set; } = false;
    public string TwitchChannel { get; set; } = "";
    public bool StreamerHideNotifs { get; set; } = true;
    // шаг 66: кинул один файл на остров — сразу окно с QR для телефона (Main\PhoneShare.cs)
    public bool DropQr { get; set; } = false;   // шаг 79: по умолчанию выкл — QR только кнопкой на карточке, а не на каждый брошенный файл
    public bool DropQrReset79 { get; set; }
    public DateTime LastCrashCheck { get; set; }
    public bool IdleStrip { get; set; } = true;   // шаг 79: полоска, когда ничего не играет (Screens.cs)   // шаг 79: вылеты новее этого — показать при запуске (Services\CrashReport.cs)       // один раз выключить у тех, у кого было включено по старому умолчанию
    // шаг 65: остров понимает, что ты делаешь (IDE, Discord, браузер, сборки, загрузки), Main\Context.cs
    public bool ContextAware { get; set; } = true;
    // шаг 64: заголовок и значок страницы для скопированной ссылки (запрос на этот сайт; в локальную сеть не ходит)
    public bool LinkPreviews { get; set; } = false;   // 3.9.7: по умолчанию выкл: запрос мог «сжечь» одноразовую ссылку и выдавал IP сайту
    public bool LinkPreviewsReset397 { get; set; }
    // шаг 55: автообновление с GitHub; версия, которую попросили не предлагать
    public bool AutoUpdate { get; set; } = true;
    public string SkipVersion { get; set; } = "";
    // шаг 56: быстрые ответы на уведомления (через «;»), вставлять ли их сразу
    public string QuickReplies { get; set; } = "Щас; Позже напишу; Ок 👍; Не могу говорить, напишу позже; Спасибо!";
    public bool QuickReplyPaste { get; set; } = false;
    // шаг 57: «Что нового» после обновления; последняя версия, которую человек уже видел
    public bool ShowWhatsNew { get; set; } = true;
    // шаг 58: реклама в Spotify — пропускать перезапуском Spotify, показывать «Реклама · 0:23» вместо трека, глушить её звук
    public bool AdDetect { get; set; } = false;
    public bool AdSkip { get; set; } = false;
    // шаг 59: пузырь звонка с кнопкой микрофона, пока микрофон кто-то использует
    public bool BubbleMic { get; set; } = true;
    public bool AdMute { get; set; } = false;
    public string LastSeenVersion { get; set; } = "";
    public int LastSeenCount { get; set; }   // шаг 79: сколько новинок этой версии уже показано (Update.cs)
    // настроек ещё не было (первый запуск): «Что нового» не нужно, всё и так новое
    [System.Text.Json.Serialization.JsonIgnore] public bool IsFresh { get; private set; }
    // звуки интерфейса (0 тихо, 1 средне, 2 громче) и упругость острова (0 мягко, 1 обычно, 2 пружинисто)
    public bool UiSounds { get; set; } = true;
    public int UiSoundVol { get; set; } = 1;
    public int UiSoundStyle { get; set; }   // шаг 79: 0 капли, 1 колокольчики (как раньше) (Services\Sfx.cs)
    // шаг 63: «желеобразность» 0..1 (затухание пружин острова) вместо трёх ступеней «Упругости»
    public double Jelly { get; set; } = 0.45;
    // 3.9.4, раздел «Интерфейс» (Settings\SettingsWindow.Ui.cs)
    public int AccentSource { get; set; } = 0;           // цвет интерфейса: 0 из обложки, 1 акцент Windows, 2 свой (AccentHex)
    public string AccentHex { get; set; } = "#0A84FF";
    public int EqColor { get; set; } = 0;                // эквалайзер: 0 как интерфейс, 1 свой (EqHex), 2 белый
    public string EqHex { get; set; } = "#30D158";
    public int SeekStyle { get; set; } = -1;             // полоска перемотки: 0 линия, 1 волна, 2 капсула, 3 точки, 4 змейка; -1 — по старому WaveSeek
    public bool Glass { get; set; } = false;             // Liquid Glass: настоящее размытие того, что под островом (Main\Glass.cs)
    public double GlassBlur { get; set; } = 0.6;
    public double GlassClear { get; set; } = 0.45;       // прозрачность стекла: 0 почти чёрное, 1 почти прозрачное
    public bool CtrlDrag { get; set; } = true;           // Ctrl + перетаскивание острова вдоль верхнего края (Main\IslandDrag.cs)
    public double IslandFreeX { get; set; } = 0.5;       // положение «Свободно» (IslandPos = 4; 3 занято старым Duo): центр острова, доля ширины монитора
    // шаг 76: режим notch — размер перехода к рамке («уши») и скругление нижних углов, 0..1 (по умолчанию как было: 10 и 15 px)
    public double NotchEarK { get; set; } = 8.0 / 22;
    public double NotchRoundK { get; set; } = 11.0 / 15;
    // стиль морфинга: 0 жидкий, 1 стекло (чётко, без перехлёста), 2 упругий (с отскоком и сплющиванием)
    public int MorphStyle { get; set; } = 0;
    // свайпы по свёрнутому острову (действия в Main\Gestures.cs)
    public int GestureLeft { get; set; } = 1;
    public int GestureRight { get; set; } = 2;
    public int GestureDown { get; set; } = 4;
    public int GestureUp { get; set; } = 3;
    // статистика прослушиваний (%AppData%\Island\stats)
    public bool ListenStats { get; set; } = true;
    public bool StatsMusicOnly { get; set; } = true;
    public bool StatsCleaned79 { get; set; }           // ролики из браузера убраны из старой статистики один раз (ListenStats.CleanVideos)   // шаг 79: в статистику и итоги только музыка, без видео из браузера (Main\Stats.cs)

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string text = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(text) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // файл испорчен: откладываем его копию (чтобы можно было достать настройки руками) и начинаем с чистых
            App.Log("Settings.Load", ex);
            try { File.Copy(FilePath, Path.ChangeExtension(FilePath, ".broken.json"), true); } catch { }
        }
        return new AppSettings { IsFresh = !File.Exists(FilePath) };
    }

    public void Save()
    {
        try { SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch (Exception ex) { App.Log("Settings.Save", ex); }
    }

    // Автозапуск: запись в HKCU\...\Run плюс снятие «выключено» в Диспетчере задач (StartupApproved).
    // Если когда-то отключить ShyFlow на вкладке «Автозагрузка», Windows игнорирует запись в Run, сколько её ни пиши.
    // Шаг 80: «выключено» из Диспетчера задач снимаем, только когда человек сам включает автозапуск в настройках ShyFlow.
    // Раньше это делалось при каждом запуске и каждом изменении настроек — выключить ShyFlow в Диспетчере задач было нельзя.
    private static bool? _autoApplied;   // что уже применено в этом запуске (null — ещё ничего)

    public void ApplyAutostart()
    {
        bool turnedOn = Autostart && _autoApplied == false, turnedOff = !Autostart && _autoApplied == true;
        _autoApplied = Autostart;
        if (AppInfo.IsPackaged) { _ = ApplyStartupTask(Autostart); return; }   // шаг 75: версия из Store
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)
                          ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k == null) return;
            k.DeleteValue("Island", false); // старое имя ключа (до переименования в ShyFlow)
            string? exe = Environment.ProcessPath;
            if (Autostart && !string.IsNullOrEmpty(exe))
            {
                // --autostart: при запуске вместе с Windows программа сначала дожидается панели задач и звука
                string want = "\"" + exe + "\" --autostart";
                if (!(k.GetValue("ShyFlow") is string cur && cur == want)) k.SetValue("ShyFlow", want);
                if (turnedOn) SetStartupApproved(true);
            }
            else
            {
                k.DeleteValue("ShyFlow", false);
                if (turnedOff) SetStartupApproved(false);
            }
        }
        catch (Exception ex) { App.Log("Autostart", ex); }
    }

    // шаг 75: в пакете MSIX запись в Run не работает (реестр у пакета свой), автозапуск — задача ShyFlowStartup из манифеста
    private static async Task ApplyStartupTask(bool on)
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync("ShyFlowStartup");
            if (on && task.State == Windows.ApplicationModel.StartupTaskState.Disabled) await task.RequestEnableAsync();
            else if (!on && task.State == Windows.ApplicationModel.StartupTaskState.Enabled) task.Disable();
        }
        catch (Exception ex) { App.Log("Autostart.Store", ex); }
    }

    private static void SetStartupApproved(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
            if (k == null) return;
            if (on)
            {
                // первый байт 02 или 06 = включено, 03 = выключено (так пишет Диспетчер задач); выключенное возвращаем в «включено»
                if (k.GetValue("ShyFlow") is byte[] b && b.Length > 0 && (b[0] & 1) == 1)
                    k.SetValue("ShyFlow", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            }
            else k.DeleteValue("ShyFlow", false);
        }
        catch (Exception ex) { App.Log("Autostart.Approved", ex); }
    }
}
