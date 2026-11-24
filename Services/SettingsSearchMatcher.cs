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
            string needle = (query ?? "").Trim().ToLowerInvariant();
            if (needle.Length == 0)
            {
                return Array.Empty<SettingsSearchItem>();
            }

            return items
                .Select(item => new
                {
                    Item = item,
                    Score = Score(item, needle),
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

        private static int Score(SettingsSearchItem item, string needle)
        {
            string title = item.Title.ToLowerInvariant();
            string keywords = item.Keywords.ToLowerInvariant();

            if (title.StartsWith(needle, StringComparison.Ordinal))
            {
                return 0;
            }

            if (keywords.StartsWith(needle, StringComparison.Ordinal))
            {
                return 1;
            }

            if (title.Contains(needle, StringComparison.Ordinal))
            {
                return 2;
            }

            if (keywords.Contains(needle, StringComparison.Ordinal))
            {
                return 3;
            }

            return -1;
        }
    }
}
