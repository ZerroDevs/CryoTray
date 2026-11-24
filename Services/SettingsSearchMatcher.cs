using System;
using System.Collections.Generic;
using System.Linq;

namespace CryoTray.Services
{
    public sealed class SettingsSearchItem
    {
        public string Title { get; init; } = "";
        public string Keywords { get; init; } = "";
        public string TabKey { get; init; } = "";
        public object? Target { get; init; }
    }

    public static class SettingsSearchMatcher
    {
        public static IReadOnlyList<SettingsSearchItem> Rank(
            IEnumerable<SettingsSearchItem> items,
            string? query,
            int maxResults = 12)
        {
            string needle = Normalize(query);
            if (needle.Length == 0)
            {
                return Array.Empty<SettingsSearchItem>();
            }

            string[] tokens = needle.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries);
            return items
                .Select(item => new
                {
                    Item = item,
                    Score = Score(item, needle, tokens),
                    Length = item.Title.Length
                })
                .Where(match => match.Score >= 0)
                .OrderBy(match => match.Score)
                .ThenBy(match => match.Length)
                .ThenBy(match => match.Item.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(Math.Max(1, maxResults))
                .Select(match => match.Item)
                .ToList();
        }

        private static string Normalize(string? query)
        {
            string lower = (query ?? "").Trim().ToLowerInvariant();
            return lower.Length == 0
                ? string.Empty
                : string.Join(" ", lower.Split(
                    (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static int Score(
            SettingsSearchItem item,
            string needle,
            IReadOnlyList<string> tokens)
        {
            string title = item.Title.ToLowerInvariant();
            string keywords = item.Keywords.ToLowerInvariant();
            if (title.Length == 0)
            {
                return -1;
            }

            if (title == needle)
            {
                return 0;
            }

            if (title.StartsWith(needle, StringComparison.Ordinal))
            {
                return 1;
            }

            int score = 2;
            foreach (string token in tokens)
            {
                int titleIndex = title.IndexOf(token, StringComparison.Ordinal);
                if (titleIndex >= 0)
                {
                    score += IsWordStart(title, titleIndex) ? 0 : 1;
                    continue;
                }

                int keywordIndex = keywords.IndexOf(token, StringComparison.Ordinal);
                if (keywordIndex < 0)
                {
                    return -1;
                }

                score += IsWordStart(keywords, keywordIndex) ? 3 : 4;
            }

            return score;
        }

        private static bool IsWordStart(string text, int index)
        {
            return index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        }
    }
}
