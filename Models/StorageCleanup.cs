using System;
using System.Collections.Generic;
using System.Linq;

namespace CryoTray.Models
{
    public enum StorageCleanupCategory
    {
        TempFiles,
        BrowserCache,
        GpuShaderCache,
        CrashDumps,
        Prefetch
    }

    public enum StorageCleanupPhase
    {
        Scanning,
        Cleaning
    }

    public sealed record StorageCleanupCategoryDefinition(
        StorageCleanupCategory Category,
        string TitleKey,
        string ShortTitleKey,
        string DescriptionKey,
        string BadgeKey,
        bool CheckedByDefault);

    public static class StorageCleanupCatalog
    {
        public static IReadOnlyList<StorageCleanupCategoryDefinition> All { get; } = new[]
        {
            new StorageCleanupCategoryDefinition(
                StorageCleanupCategory.TempFiles,
                "StorageCleanup_TempTitle",
                "StorageCleanup_ShortTemp",
                "StorageCleanup_TempDesc",
                "StorageCleanup_Recommended",
                true),
            new StorageCleanupCategoryDefinition(
                StorageCleanupCategory.BrowserCache,
                "StorageCleanup_BrowserTitle",
                "StorageCleanup_ShortBrowser",
                "StorageCleanup_BrowserDesc",
                "StorageCleanup_OptIn",
                false),
            new StorageCleanupCategoryDefinition(
                StorageCleanupCategory.GpuShaderCache,
                "StorageCleanup_GpuTitle",
                "StorageCleanup_ShortGpu",
                "StorageCleanup_GpuDesc",
                "StorageCleanup_Gamers",
                false),
            new StorageCleanupCategoryDefinition(
                StorageCleanupCategory.CrashDumps,
                "StorageCleanup_CrashTitle",
                "StorageCleanup_ShortCrash",
                "StorageCleanup_CrashDesc",
                "StorageCleanup_OptIn",
                false),
            new StorageCleanupCategoryDefinition(
                StorageCleanupCategory.Prefetch,
                "StorageCleanup_PrefetchTitle",
                "StorageCleanup_ShortPrefetch",
                "StorageCleanup_PrefetchDesc",
                "StorageCleanup_Advanced",
                false)
        };

        public static StorageCleanupCategoryDefinition Get(StorageCleanupCategory category)
        {
            return All.First(item => item.Category == category);
        }
    }

    public sealed record StorageCleanupTargetScan(
        string RootPath,
        long Bytes,
        int Files);

    public sealed record StorageCleanupCategoryScan(
        StorageCleanupCategory Category,
        long Bytes,
        int Files,
        IReadOnlyList<StorageCleanupTargetScan>? Targets = null);

    public sealed record StorageCleanupProgress(
        StorageCleanupPhase Phase,
        StorageCleanupCategory Category,
        int CategoriesCompleted,
        int TotalCategories,
        long Bytes,
        int Files);

    public sealed class StorageCleanupScan
    {
        public StorageCleanupScan(
            IReadOnlyDictionary<StorageCleanupCategory, StorageCleanupCategoryScan> categories)
        {
            Categories = categories;
        }

        public IReadOnlyDictionary<StorageCleanupCategory, StorageCleanupCategoryScan> Categories { get; }

        public long TotalBytes => Categories.Values.Sum(item => item.Bytes);

        public StorageCleanupCategoryScan Get(StorageCleanupCategory category)
        {
            return Categories.TryGetValue(category, out var result)
                ? result
                : new StorageCleanupCategoryScan(category, 0, 0);
        }
    }

    public sealed record StorageCleanupRunResult(
        long BytesFreed,
        int FilesDeleted,
        int SkippedLockedFiles,
        IReadOnlyDictionary<StorageCleanupCategory, long> FreedBytesByCategory);
}
