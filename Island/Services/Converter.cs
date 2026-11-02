using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Island;

// Шаг 79: конвертер файлов без сторонних программ.
// Картинки — через WIC (то, что умеет «Фотографии»: PNG, JPG, BMP, GIF, TIFF, WebP и HEIC, если стоят расширения Windows),
// сохраняются в PNG, JPG, BMP, TIFF, GIF и ICO (ICO собираем сами: PNG внутри, до 256 px).
// Звук и видео — через Windows.Media.Transcoding (тот же движок, что у «Кино и ТВ»): MP3, M4A, WAV, FLAC, WMA, MP4, WMV,
// из видео можно вытащить только звук. Результат кладётся рядом с исходным файлом, имя не перезаписывается: «песня (2).mp3».
internal static class Converter
{
    private static readonly string[] ImageIn = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".ico", ".jfif" };
    private static readonly string[] AudioIn = { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".wma", ".ogg", ".opus", ".alac", ".aiff", ".aif" };
    private static readonly string[] VideoIn = { ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v", ".3gp", ".ts" };

    public enum Kind { None, Image, Audio, Video }

    public static Kind KindOf(string path)
    {
        string e = Path.GetExtension(path).ToLowerInvariant();
        return ImageIn.Contains(e) ? Kind.Image : AudioIn.Contains(e) ? Kind.Audio : VideoIn.Contains(e) ? Kind.Video : Kind.None;
    }

    // варианты для меню: (что показать, во что конвертировать). Свой же формат не предлагаем
    public static List<(string label, string target)> Targets(string path)
    {
        string e = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        if (e == "JPEG" || e == "JFIF") e = "JPG";
        if (e == "TIF") e = "TIFF";
        var list = KindOf(path) switch
        {
            Kind.Image => new[] { "PNG", "JPG", "BMP", "TIFF", "GIF", "ICO" }.Select(t => (t, t))
                .Append((Loc.T("Сжать для чата (JPG до 1 МБ)", "Shrink for chat (JPG up to 1 MB)"), "ZJ1")),
            Kind.Audio => new[] { "MP3", "M4A", "WAV", "FLAC", "WMA" }.Select(t => (t, t)),
            Kind.Video => new[] { ("MP4", "MP4"), ("WMV", "WMV"), (Loc.T("Только звук: MP3", "Audio only: MP3"), "MP3"),
                                  (Loc.T("Только звук: M4A", "Audio only: M4A"), "M4A"), (Loc.T("Только звук: WAV", "Audio only: WAV"), "WAV"),
                                  (Loc.T("Сжать до 10 МБ (Discord)", "Shrink to 10 MB (Discord)"), "Z10"), (Loc.T("Сжать до 25 МБ", "Shrink to 25 MB"), "Z25"),
                                  (Loc.T("Сжать до 50 МБ", "Shrink to 50 MB"), "Z50"), (Loc.T("Для Telegram: MP4 720p", "For Telegram: MP4 720p"), "T720") },
            _ => Array.Empty<(string, string)>()
        };
        return list.Where(x => x.Item2 != e).ToList();
    }

    // шаг 79: что можно сделать сразу со всеми выбранными файлами (общие варианты)
    public static List<(string label, string target)> TargetsAll(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return new();
        var first = Targets(paths[0]);
        return first.Where(t => paths.Skip(1).All(p => Targets(p).Any(x => x.target == t.target))).ToList();
    }

    // расширение и пометка в имени для «сжать» и «для Telegram»
    private static (string ext, string suffix) OutOf(string target) => target switch
    {
        "ZJ1" => ("JPG", " (" + Loc.T("для чата", "for chat") + ")"),
        "Z10" => ("MP4", " (10 " + Loc.T("МБ", "MB") + ")"),
        "Z25" => ("MP4", " (25 " + Loc.T("МБ", "MB") + ")"),
        "Z50" => ("MP4", " (50 " + Loc.T("МБ", "MB") + ")"),
        "T720" => ("MP4", " (720p)"),
        _ => (target, "")
    };

    // рядом с исходным, без перезаписи: «имя.mp3», «имя (2).mp3»…
    private static string OutPath(string src, string target)
    {
        string dir = Path.GetDirectoryName(src) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var (e, suffix) = OutOf(target);
        string name = Path.GetFileNameWithoutExtension(src) + suffix, ext = "." + e.ToLowerInvariant();
        string p = Path.Combine(dir, name + ext);
        for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{name} ({i}){ext}");
        return p;
    }

    public static Task<string> Convert(string src, string target, IProgress<double> progress) =>
        KindOf(src) == Kind.Image ? OnSta(() => ConvertImage(src, target, progress)) : ConvertMedia(src, target, progress);

    // картинки рисуются через DrawingVisual и RenderTargetBitmap: им нужен STA-поток, а не поток пула (там они падают)
    private static Task<string> OnSta(Func<string> work)
    {
        var tcs = new TaskCompletionSource<string>();
        var th = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true };
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        return tcs.Task;
    }

    // ---------- картинки ----------
    private static string ConvertImage(string src, string target, IProgress<double> progress)
    {
        progress.Report(0.1);
        BitmapSource bmp;
        using (var fs = File.OpenRead(src))
        {
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            bmp = dec.Frames[0];
            bmp.Freeze();
        }
        progress.Report(0.5);
        string outPath = OutPath(src, target);
        if (target == "ICO") { WriteIco(bmp, outPath); progress.Report(1); return outPath; }
        if (target == "ZJ1") { WriteSmallJpg(bmp, outPath, 1_000_000, progress); return outPath; }
        BitmapEncoder enc = target switch
        {
            "JPG" => new JpegBitmapEncoder { QualityLevel = 92 },
            "BMP" => new BmpBitmapEncoder(),
            "TIFF" => new TiffBitmapEncoder(),
            "GIF" => new GifBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        // в JPG и BMP нет прозрачности: прозрачное становится белым, а не чёрным
        if (target is "JPG" or "BMP") bmp = OnWhite(bmp);
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var o = File.Create(outPath)) enc.Save(o);
        progress.Report(1);
        return outPath;
    }

    private static BitmapSource OnWhite(BitmapSource b)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var r = new Rect(0, 0, b.PixelWidth, b.PixelHeight);
            dc.DrawRectangle(Brushes.White, null, r);
            dc.DrawImage(b, r);
        }
        var rtb = new RenderTargetBitmap(b.PixelWidth, b.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var res = new FormatConvertedBitmap(rtb, PixelFormats.Bgr24, null, 0);
        res.Freeze();
        return res;
    }

    // «для чата»: JPG до limit байт — сначала не больше 2560 px по длинной стороне, потом качество ниже, потом размер меньше
    private static void WriteSmallJpg(BitmapSource src, string path, long limit, IProgress<double> progress)
    {
        var flat = OnWhite(src);
        double side = Math.Min(2560, Math.Max(flat.PixelWidth, flat.PixelHeight));
        for (int round = 0; round < 8; round++)
        {
            double k = side / Math.Max(flat.PixelWidth, flat.PixelHeight);
            BitmapSource img = k < 0.999 ? new TransformedBitmap(flat, new ScaleTransform(k, k)) : flat;
            if (img.CanFreeze) img.Freeze();
            foreach (int q in new[] { 88, 80, 72, 64, 56 })
            {
                var enc = new JpegBitmapEncoder { QualityLevel = q };
                enc.Frames.Add(BitmapFrame.Create(img));
                using var ms = new MemoryStream();
                enc.Save(ms);
                if (ms.Length <= limit || (q == 56 && round == 7)) { File.WriteAllBytes(path, ms.ToArray()); progress.Report(1); return; }
            }
            side *= 0.8;
            progress.Report(0.5 + round * 0.06);
        }
    }

    // ICO: один кадр PNG до 256 px (Windows Vista и новее такие понимают)
    private static void WriteIco(BitmapSource b, string path)
    {
        int side = Math.Min(256, Math.Max(b.PixelWidth, b.PixelHeight));
        double k = (double)side / Math.Max(b.PixelWidth, b.PixelHeight);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double w = b.PixelWidth * k, h = b.PixelHeight * k;
            dc.DrawImage(b, new Rect((side - w) / 2, (side - h) / 2, w, h));   // квадрат, картинка по центру
        }
        var rtb = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        png.Save(ms);
        byte[] data = ms.ToArray();
        using var o = new BinaryWriter(File.Create(path));
        o.Write((short)0); o.Write((short)1); o.Write((short)1);              // ICONDIR: тип 1 (значок), 1 картинка
        o.Write((byte)(side >= 256 ? 0 : side)); o.Write((byte)(side >= 256 ? 0 : side));
        o.Write((byte)0); o.Write((byte)0); o.Write((short)1); o.Write((short)32);
        o.Write(data.Length); o.Write(6 + 16);                               // размер и смещение данных
        o.Write(data);
    }

    // ---------- звук и видео ----------
    private static async Task<string> ConvertMedia(string src, string target, IProgress<double> progress)
    {
        string outPath = OutPath(src, target);
        var inFile = await StorageFile.GetFileFromPathAsync(src);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outPath)!);
        var outFile = await folder.CreateFileAsync(Path.GetFileName(outPath), CreationCollisionOption.GenerateUniqueName);
        var profile = target is "Z10" or "Z25" or "Z50" or "T720" ? await SmallProfile(inFile, target) : target switch
        {
            "MP3" => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
            "M4A" => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
            "WAV" => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
            "FLAC" => MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High),
            "WMA" => MediaEncodingProfile.CreateWma(AudioEncodingQuality.High),
            "WMV" => MediaEncodingProfile.CreateWmv(VideoEncodingQuality.Auto),
            _ => MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto)   // Auto: размер и частота кадров как у исходника
        };
        try
        {
            var tr = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prep = await tr.PrepareFileTranscodeAsync(inFile, outFile, profile);
            if (!prep.CanTranscode) throw new InvalidOperationException(Reason(prep.FailureReason));
            var op = prep.TranscodeAsync();
            op.Progress = (_, p) => progress.Report(Math.Clamp(p / 100.0, 0, 1));
            await op;
            progress.Report(1);
            return outFile.Path;
        }
        catch
        {
            try { await outFile.DeleteAsync(); } catch { /* уже нет */ }
            throw;
        }
    }

    // шаг 79: сжатие видео под размер (Discord 10 МБ и т.д.) и «для Telegram» (MP4 H.264 до 720p).
    // Битрейт = нужный размер / длительность (с запасом 10 %) минус звук; чем меньше битрейт, тем ниже разрешение, чтобы не было «мыла».
    private static async Task<MediaEncodingProfile> SmallProfile(StorageFile file, string target)
    {
        var vp = await file.Properties.GetVideoPropertiesAsync();
        double sec = vp.Duration.TotalSeconds;
        uint sw = vp.Width, sh = vp.Height;
        if (vp.Orientation is Windows.Storage.FileProperties.VideoOrientation.Rotate90 or Windows.Storage.FileProperties.VideoOrientation.Rotate270) (sw, sh) = (sh, sw);
        if (sw == 0 || sh == 0) { sw = 1280; sh = 720; }
        var p = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        p.Audio = AudioEncodingProperties.CreateAac(44100, 2, 96000);
        double maxShort;
        if (target == "T720") maxShort = 720;
        else
        {
            if (sec <= 0.5) throw new InvalidOperationException(Loc.T("Не удалось узнать длину видео", "Couldn't get the video length"));
            double mb = target switch { "Z10" => 10, "Z25" => 25, _ => 50 };
            double vbr = mb * 1024 * 1024 * 8 * 0.9 / sec - 96000;
            if (vbr < 120_000) throw new InvalidOperationException(Loc.T("Видео слишком длинное, чтобы уложить его в ", "The video is too long to fit in ") + mb + Loc.T(" МБ", " MB"));
            p.Video.Bitrate = (uint)Math.Min(vbr, 8_000_000);
            maxShort = vbr >= 2_500_000 ? 1080 : vbr >= 1_200_000 ? 720 : vbr >= 600_000 ? 480 : 360;
        }
        // короткая сторона не больше maxShort, пропорции как у исходника, размеры чётные
        double k = Math.Min(1, maxShort / Math.Min(sw, sh));
        p.Video.Width = (uint)(Math.Round(sw * k / 2) * 2);
        p.Video.Height = (uint)(Math.Round(sh * k / 2) * 2);
        if (vp.Bitrate > 0 && target == "T720") p.Video.Bitrate = Math.Min(p.Video.Bitrate, Math.Max(1_500_000u, vp.Bitrate));
        return p;
    }

    private static string Reason(TranscodeFailureReason r) => r switch
    {
        TranscodeFailureReason.CodecNotFound => Loc.T("Windows не умеет читать или писать этот формат (нет кодека)", "Windows can't read or write this format (no codec)"),
        TranscodeFailureReason.InvalidProfile => Loc.T("Неподходящий формат для этого файла", "This format doesn't fit this file"),
        _ => Loc.T("Файл не получилось прочитать", "Couldn't read the file")
    };
}
