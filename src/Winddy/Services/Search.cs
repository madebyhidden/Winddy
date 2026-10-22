using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Winddy.Models;

namespace Winddy.Services;

public sealed record SearchResult(HistoryItem Item, IReadOnlyList<(int Start, int Length)> Ranges, double Score);

/// <summary>Exact, fuzzy, regex and mixed search over item titles.</summary>
public static class Search
{
    private const int FuzzyLimit = 5000;

    public static List<SearchResult> Run(string query, IEnumerable<HistoryItem> items)
    {
        var list = items.ToList();
        if (string.IsNullOrEmpty(query))
            return list.Select(i => new SearchResult(i, Array.Empty<(int, int)>(), 0)).ToList();

        return AppSettings.Current.SearchMode switch
        {
            SearchMode.Fuzzy => Fuzzy(query, list),
            SearchMode.Regexp => Regexp(query, list),
            SearchMode.Mixed => Mixed(query, list),
            _ => Exact(query, list),
        };
    }

    private static List<SearchResult> Exact(string query, List<HistoryItem> items)
    {
        var results = new List<SearchResult>();
        foreach (var item in items)
        {
            var idx = item.SearchableText.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            if (idx >= 0) results.Add(new SearchResult(item, new[] { (idx, query.Length) }, 0));
        }
        return results;
    }

    private static List<SearchResult> Regexp(string query, List<HistoryItem> items)
    {
        Regex regex;
        try
        {
            regex = new Regex(query, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return new List<SearchResult>();
        }

        var results = new List<SearchResult>();
        foreach (var item in items)
        {
            try
            {
                var m = regex.Match(item.SearchableText);
                if (m.Success) results.Add(new SearchResult(item, new[] { (m.Index, m.Length) }, 0));
            }
            catch (RegexMatchTimeoutException)
            {
                // skip pathological items
            }
        }
        return results;
    }

    private static List<SearchResult> Mixed(string query, List<HistoryItem> items)
    {
        var results = Exact(query, items);
        if (results.Count > 0) return results;
        results = Regexp(query, items);
        if (results.Count > 0) return results;
        return Fuzzy(query, items);
    }

    /// <summary>
    /// Subsequence matching with a tolerance for a few missing characters, scored so that
    /// tight, early, word-aligned matches rank first (lower score is better).
    /// </summary>
    private static List<SearchResult> Fuzzy(string query, List<HistoryItem> items)
    {
        var q = query.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray();
        if (q.Length == 0) return items.Select(i => new SearchResult(i, Array.Empty<(int, int)>(), 0)).ToList();
        var allowedMisses = q.Length / 4;

        var results = new List<SearchResult>();
        foreach (var item in items)
        {
            var text = item.SearchableText;
            if (text.Length > FuzzyLimit) text = text[..FuzzyLimit];
            var lower = text.ToLowerInvariant();

            var ranges = new List<(int Start, int Length)>();
            int pos = 0, misses = 0, gaps = 0, first = -1, last = -1, wordStarts = 0;
            foreach (var c in q)
            {
                var found = lower.IndexOf(c, pos);
                if (found < 0)
                {
                    if (++misses > allowedMisses) break;
                    continue;
                }
                if (first < 0) first = found;
                if (last >= 0) gaps += found - last - 1;
                if (found == 0 || !char.IsLetterOrDigit(lower[found - 1])) wordStarts++;
                if (ranges.Count > 0 && ranges[^1].Start + ranges[^1].Length == found)
                    ranges[^1] = (ranges[^1].Start, ranges[^1].Length + 1);
                else
                    ranges.Add((found, 1));
                last = found;
                pos = found + 1;
            }
            if (misses > allowedMisses || first < 0) continue;

            var score = misses * 10.0 + gaps + first * 0.1 - wordStarts * 0.5 + text.Length * 0.001;
            results.Add(new SearchResult(item, ranges, score));
        }
        return results.OrderBy(r => r.Score).ToList();
    }
}
