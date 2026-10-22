using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Winddy.Services;

public enum NotifySound { Knock, Write }

/// <summary>Copy notifications and sounds (Maccy's "Notifications and sounds").</summary>
public static class Notifier
{
    private static readonly byte[] Knock = Load("Knock.wav");
    private static readonly byte[] Write = Load("Write.wav");

    public static Action<string>? ShowNotification { get; set; }

    private static byte[] Load(string name)
    {
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
        using var ms = new MemoryStream();
        info!.Stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static void Notify(string? body, NotifySound sound)
    {
        var s = AppSettings.Current;
        if (s.PlaySounds)
        {
            try { new SoundPlayer(new MemoryStream(sound == NotifySound.Knock ? Knock : Write)).Play(); } catch { /* no audio device */ }
        }
        if (s.ShowNotifications && !string.IsNullOrWhiteSpace(body))
            ShowNotification?.Invoke(body.Length > 200 ? body[..200] + "…" : body);
    }
}

/// <summary>Extracts text from copied images with the built-in Windows OCR (like Maccy's Vision text recognition).</summary>
public static class TextRecognizer
{
    public static async Task<string?> RecognizeAsync(string imagePath)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return null;

            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);

            var transform = new BitmapTransform();
            var max = OcrEngine.MaxImageDimension;
            if (decoder.PixelWidth > max || decoder.PixelHeight > max)
            {
                var scale = (double)max / Math.Max(decoder.PixelWidth, decoder.PixelHeight);
                transform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
                transform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
            }

            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var result = await engine.RecognizeAsync(bitmap);
            var text = string.Join("\n", result.Lines.Select(l => l.Text)).Trim();
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
