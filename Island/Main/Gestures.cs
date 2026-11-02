namespace Island;

// Свои жесты (шаг 63): свайп по свёрнутому острову влево, вправо, вниз и вверх делает то, что выбрано в настройках
// («Управление», «Жесты»). По умолчанию как раньше: влево следующий трек, вправо предыдущий, вниз раскрыть; вверх пауза.
public partial class MainWindow
{
    // порядок важен: номера хранятся в настройках
    internal static readonly string[] GestureNames =
    {
        "Ничего", "Следующий трек", "Предыдущий трек", "Пауза и play", "Раскрыть остров", "Карман", "Быстрые действия",
        "Шторка уведомлений", "Таймер", "Громкость программ", "Микрофон", "Скрыть остров",
    };

    // Шаг 80: свайп двумя пальцами по тачпаду над островом. Windows присылает его как горизонтальную прокрутку
    // (WM_MOUSEHWHEEL, то же у наклона колеса мыши вбок). Пальцы влево — как свайп мышью влево (по умолчанию следующий трек),
    // вправо — как вправо. Пока пальцы едут, остров тянется вбок желе; один жест — одно действие (инерция прокрутки
    // после отрыва пальцев второй раз не срабатывает). Над пузырём со стеком — листает активности.
    // Три пальца Windows забирает себе (переключение окон), до программ они не доходят.
    private int _hwAcc;
    private DateTime _hwLast;
    private bool _hwDone;

    private bool OnHWheel(int delta)
    {
        var now = DateTime.UtcNow;
        if ((now - _hwLast).TotalMilliseconds > 320) { _hwAcc = 0; _hwDone = false; }   // пауза в потоке — новый жест
        _hwLast = now;
        bool overBubble = Bubble.IsMouseOver && _stackKinds.Length > 1;
        bool overIsland = Pill.IsMouseOver && MiniLike && _cfg.Gestures;
        if (!overBubble && !overIsland) return false;
        if (_hwDone || delta == 0) return true;
        _hwAcc += delta;
        double k = Lq;
        if (overIsland && k > 0 && !NotchDrip)
        {
            _js.V += -Math.Sign(delta) * 9 * k;   // перекос по ходу пальцев
            EnsureLoop();
        }
        if (Math.Abs(_hwAcc) < 150) return true;
        int dir = Math.Sign(_hwAcc);
        _hwDone = true;
        if (overBubble) { StackSwitch(dir); return true; }
        if (k > 0 && !NotchDrip)
        {
            _jx.V += 1.1 * k;              // рывок: вытянулся вбок
            _js.V += -dir * 70 * k;        // и качнулся туда, куда ушли пальцы
            EnsureLoop();
        }
        DoGesture(dir > 0 ? _cfg.GestureLeft : _cfg.GestureRight);
        return true;
    }

    private void DoGesture(int action)
    {
        switch (action)
        {
            case 1: Swipe(1); return;
            case 2: Swipe(-1); return;
            case 4: SwipeDown(); return;
        }
        _swiped = true;   // дальше мышь не слушаем, но держим до отпускания (как в Swipe)
        ReleaseRubber();
        if (action == 0) return;
        Sfx.Ui("swipe");
        switch (action)
        {
            case 3: OnHotkey(2); break;    // как Ctrl+Alt+Пробел
            case 5: OnHotkey(5); break;    // карман
            case 6: OnHotkey(7); break;    // быстрые действия
            case 7: ToggleCenter(); break;
            case 8: ToggleTimerPanel(); break;
            case 9: ToggleMixer(); break;
            case 10: ToggleMic(); break;
            case 11: ToggleHidden(); break;
        }
    }
}
