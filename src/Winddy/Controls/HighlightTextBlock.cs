using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Winddy.Services;

namespace Winddy.Controls;

/// <summary>Single-line text block that emphasizes search matches using the configured highlight style.</summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText), typeof(string), typeof(HighlightTextBlock), new PropertyMetadata("", (d, _) => ((HighlightTextBlock)d).Rebuild()));

    public static readonly DependencyProperty RangesProperty = DependencyProperty.Register(
        nameof(Ranges), typeof(IReadOnlyList<(int Start, int Length)>), typeof(HighlightTextBlock), new PropertyMetadata(null, (d, _) => ((HighlightTextBlock)d).Rebuild()));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public IReadOnlyList<(int Start, int Length)>? Ranges
    {
        get => (IReadOnlyList<(int Start, int Length)>?)GetValue(RangesProperty);
        set => SetValue(RangesProperty, value);
    }

    private void Rebuild()
    {
        Inlines.Clear();
        var text = SourceText ?? "";
        var ranges = Ranges;
        if (ranges == null || ranges.Count == 0)
        {
            Inlines.Add(new Run(text));
            return;
        }

        var pos = 0;
        foreach (var (start, length) in ranges)
        {
            if (start < pos || start >= text.Length) continue;
            if (start > pos) Inlines.Add(new Run(text[pos..start]));
            var len = System.Math.Min(length, text.Length - start);
            Inlines.Add(Emphasize(new Run(text.Substring(start, len))));
            pos = start + len;
        }
        if (pos < text.Length) Inlines.Add(new Run(text[pos..]));
    }

    private Run Emphasize(Run run)
    {
        switch (AppSettings.Current.HighlightMatch)
        {
            case HighlightMatch.Italic:
                run.FontStyle = FontStyles.Italic;
                break;
            case HighlightMatch.Underline:
                run.TextDecorations = System.Windows.TextDecorations.Underline;
                break;
            case HighlightMatch.Color:
                run.SetResourceReference(TextElement.ForegroundProperty, "HighlightColor");
                break;
            default:
                run.FontWeight = FontWeights.Bold;
                break;
        }
        return run;
    }
}
