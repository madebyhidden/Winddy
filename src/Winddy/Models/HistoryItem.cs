using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Winddy.Models;

/// <summary>One remembered clipboard entry. Persisted as JSON; image bytes live next to it as PNG files.</summary>
public sealed class HistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Text { get; set; }
    public string? Rtf { get; set; }
    public string? Html { get; set; }
    public List<string>? Files { get; set; }
    public string? ImageFile { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string? Application { get; set; }
    public DateTime FirstCopiedAt { get; set; } = DateTime.Now;
    public DateTime LastCopiedAt { get; set; } = DateTime.Now;
    public int NumberOfCopies { get; set; } = 1;
    public string? Pin { get; set; }
    public string? Title { get; set; }
    /// <summary>Text found in an image by OCR; makes screenshots searchable.</summary>
    public string? RecognizedText { get; set; }
    public string Hash { get; set; } = "";

    [JsonIgnore] public bool IsPinned => !string.IsNullOrEmpty(Pin);
    [JsonIgnore] public bool HasImage => ImageFile != null;
    [JsonIgnore] public bool HasFiles => Files is { Count: > 0 };
    [JsonIgnore] public bool IsPlainTextOnly => !HasImage && !HasFiles;

    private static readonly Regex HexColor = new("^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    /// <summary>Text that represents the item for searching and display.</summary>
    [JsonIgnore]
    public string SearchableText
    {
        get
        {
            if (!string.IsNullOrEmpty(Title)) return Title!;
            if (HasFiles) return string.Join(" ", Files!.Select(f => f));
            if (!string.IsNullOrEmpty(Text)) return Text!;
            if (!string.IsNullOrEmpty(RecognizedText)) return RecognizedText!;
            return "";
        }
    }

    [JsonIgnore]
    public string? HexColorValue
    {
        get
        {
            var t = Text?.Trim();
            if (t == null || !HexColor.IsMatch(t)) return null;
            // Bare 3/6/8 digit numbers like "123" are ambiguous; require '#' unless it contains a letter.
            if (!t.StartsWith('#') && t.All(char.IsDigit)) return null;
            return t.StartsWith('#') ? t : "#" + t;
        }
    }

    [JsonIgnore]
    public string ApplicationName => string.IsNullOrEmpty(Application) ? "" : Path.GetFileNameWithoutExtension(Application);

    public static string ShortenTitle(string text, bool showSpecialSymbols)
    {
        if (text.Length > 1000) text = text[..1000];
        if (showSpecialSymbols)
        {
            text = Regex.Replace(text, "^ +", m => new string('·', m.Length));
            text = Regex.Replace(text, " +$", m => new string('·', m.Length));
            text = text.Replace("\r\n", "⏎").Replace("\n", "⏎").Replace("\r", "⏎").Replace("\t", "⇥");
        }
        else
        {
            text = text.Trim();
        }
        return text;
    }
}
