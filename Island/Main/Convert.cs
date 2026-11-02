using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Island;

// Шаг 79: конвертер файлов (Services\Converter.cs).
// Где: кнопка с двумя стрелками на карточке файла в кармане (видна при наведении) и плитка «Конвертер» в быстрых действиях
// (выбрать файл). Меню форматов — по типу файла. Пока идёт, плашка наливается жидкостью; готово — файл сразу в кармане,
// клик по плашке показывает его в папке. Не получилось — плашка с причиной.
public partial class MainWindow
{
    private bool _converting;

    private void ConvertMenu(UIElement anchor, string path) => ConvertMenu(anchor, new[] { path });

    // шаг 79: несколько файлов сразу — в меню только то, что подходит всем
    private void ConvertMenu(UIElement anchor, IReadOnlyList<string> paths)
    {
        var targets = Converter.TargetsAll(paths);
        if (targets.Count == 0)
        {
            ShowToast("triangle-alert", Loc.T("Этот файл не конвертируется: только картинки, музыка и видео", "This file can't be converted: images, music and video only"), null, "key");
            return;
        }
        var cm = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        cm.Items.Add(new MenuItem { Header = paths.Count > 1 ? Loc.T("Файлов: ", "Files: ") + paths.Count + Loc.T(". Конвертировать в…", ". Convert to…") : Loc.T("Конвертировать в…", "Convert to…"), IsEnabled = false });
        foreach (var (label, target) in targets)
        {
            var mi = new MenuItem { Header = label };
            string t = target;
            mi.Click += (_, _) => _ = ConvertFiles(paths, t);
            cm.Items.Add(mi);
        }
        cm.IsOpen = true;
    }

    private async Task ConvertFiles(IReadOnlyList<string> paths, string target)
    {
        if (_converting) { ShowToast("hourglass", Loc.T("Подожди, прошлый файл ещё конвертируется", "Wait, the previous file is still converting"), null, "key"); return; }
        _converting = true;
        string label = target.StartsWith("Z") ? Loc.T("Сжимаю", "Shrinking") : target == "T720" ? "720p" : target;
        var done = new List<string>();
        string? lastError = null;
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                string what = (target.StartsWith("Z") ? label : Loc.T("Конвертирую в ", "Converting to ") + label) + (paths.Count > 1 ? $"  {i + 1}/{paths.Count}" : "");
                double shown = -1;
                int idx = i, n = paths.Count;
                var progress = new Progress<double>(v =>
                {
                    // плашку обновляем шагами по 4 %, а не на каждое событие; несколько файлов — общий прогресс
                    double all = (idx + v) / n;
                    if (v < 1 && all - shown < 0.04) return;
                    shown = all;
                    ShowToast("arrow-left-right", what, all, "dl");
                });
                ShowToast("arrow-left-right", what, (double)i / n, "dl");
                try
                {
                    string result = await Converter.Convert(path, target, progress);
                    done.Add(result);
                    _files.Remove(result);
                    _files.Insert(0, result);
                }
                catch (Exception ex)
                {
                    App.Log("Convert " + Path.GetFileName(path) + " -> " + target, ex);
                    lastError = ex is InvalidOperationException ? ex.Message : Loc.T("файл занят, повреждён или формат не поддерживается", "the file is busy, damaged or not supported");
                }
            }
            if (done.Count > 0)
            {
                SaveFiles();
                if (_pocketOpen) RenderPocket();
                try { Sfx.Ui("unlock"); } catch { /* нет звука */ }
                string size = done.Count == 1 ? "  ·  " + FmtSize(new FileInfo(done[0]).Length) : "";
                string text = done.Count == 1 ? Loc.T("Готово: ", "Done: ") + Path.GetFileName(done[0]) + size
                            : Loc.T("Готово: ", "Done: ") + done.Count + "/" + paths.Count + Loc.T(" файлов", " files");
                if (lastError != null) text += " · " + Loc.T("часть не вышла", "some failed");
                ShowToast("check", text, null, "copy");
                string show = done[^1];
                _osdClick = () =>
                {
                    _toastTimer.Stop();
                    ApplyMode(Rest());
                    try { Process.Start("explorer.exe", "/select,\"" + show + "\""); } catch (Exception ex) { App.LogOnce("Convert.Show", ex); }
                };
            }
            else ShowToast("triangle-alert", Loc.T("Не получилось: ", "Failed: ") + lastError, null, "key");
        }
        finally { _converting = false; }
    }

    // плитка «Конвертер»: выбрать файл и формат
    private void PickAndConvert()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Что конвертировать (можно несколько)", "What to convert (several is fine)"), Multiselect = true,
            Filter = Loc.T("Картинки, музыка и видео", "Images, music and video") +
                     "|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.ico;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.wma;*.ogg;*.opus;*.aiff;*.aif;*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.webm;*.m4v;*.3gp;*.ts" +
                     "|" + Loc.T("Все файлы", "All files") + "|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        ConvertMenu(Pill, dlg.FileNames);
    }
}
