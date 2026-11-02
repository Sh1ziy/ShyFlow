using System.Windows;
using System.Windows.Input;

namespace Island;

// 3.9.4: Ctrl + перетаскивание острова вдоль верхнего края экрана (Настройки, «Интерфейс»).
// Зажал Ctrl, взял остров мышью и повёл — он едет за курсором; отпустил — встал. Рядом с центром и краями остров
// прилипает к ним и становится обычным «По центру», «Слева» или «Справа», иначе запоминается как «Свободно»
// (IslandPos = 4, IslandFreeX — доля ширины монитора). Пузыри и значки едут за ним (Display.cs).
// Обработчики стоят раньше жестов (InitGestures): пока тянем, свайпы, клик и раскрытие не срабатывают.
public partial class MainWindow
{
    private bool _islandDrag;
    private double _dragGrab;          // где взяли остров: от курсора до центра острова, DIP
    private const double SnapDip = 28;

    private void InitIslandDrag()
    {
        Pill.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_cfg.CtrlDrag || Duo || _hidden || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (!Pill.CaptureMouse()) return;
            _islandDrag = true;
            _dragGrab = CursorMonX(e) - IslandCenterMonX();
            Pill.Cursor = Cursors.SizeWE;
            HoverScale(1.04);   // «приподнять» остров, пока его несут
            e.Handled = true;
        };
        Pill.PreviewMouseMove += (_, e) =>
        {
            if (!_islandDrag) return;
            MoveIslandTo(CursorMonX(e) - _dragGrab, false);
            e.Handled = true;
        };
        Pill.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_islandDrag) return;
            e.Handled = true;   // иначе Pill_Click раскрыл бы остров
            EndIslandDrag(CursorMonX(e) - _dragGrab);
        };
        Pill.LostMouseCapture += (_, _) => { if (_islandDrag) EndIslandDrag(IslandCenterMonX()); };
    }

    private double CursorMonX(MouseEventArgs e) => e.GetPosition(this).X + Left - MonitorDip.Left;

    private double IslandCenterMonX() => Left - MonitorDip.Left + (_winW > 0 ? _winW : Width) / 2 + _stageMove.X;

    private void MoveIslandTo(double center, bool snap)
    {
        var r = MonitorDip;
        if (r.Width <= 0) return;
        double half = Math.Max(24, Pill.Width) / 2;
        center = Math.Clamp(center, Edge + half, Math.Max(Edge + half, r.Width - Edge - half));
        int pos = 4;
        if (snap)
        {
            if (Math.Abs(center - r.Width / 2) < SnapDip) pos = 0;
            else if (center - half - Edge < SnapDip) pos = 1;
            else if (r.Width - Edge - half - center < SnapDip) pos = 2;
        }
        _cfg.IslandPos = pos;
        _cfg.IslandFreeX = center / r.Width;
        SetWinW(_winW > 0 ? _winW : NeededWinW());
        PlaceAppsBar();
        PlaceDock();
    }

    private void EndIslandDrag(double center)
    {
        _islandDrag = false;
        if (Pill.IsMouseCaptured) Pill.ReleaseMouseCapture();
        Pill.ClearValue(CursorProperty);
        MoveIslandTo(center, true);
        HoverScale(Pill.IsMouseOver ? 1.015 : 1.0);
        Sfx.Ui("tap");
        try { _cfg.Save(); } catch (Exception ex) { App.Log("IslandDrag.Save", ex); }
    }
}
