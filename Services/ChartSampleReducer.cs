using System;
using System.Collections.Generic;
using System.Linq;
using CryoTray.Models;

namespace CryoTray.Services
{
    /// <summary>
    /// Reduces a chart series while keeping each bucket's low and high point. The returned
    /// indices always point into the original sample list, so hover lookup and x positions
    /// remain unchanged.
    /// </summary>
    public static class ChartSampleReducer
    {
        public static List<int> ReduceRange(
            IReadOnlyList<TelemetrySample> samples,
            int start,
            int end,
            int maxPoints,
            Func<TelemetrySample, double> valueSelector)
        {
            var indices = new List<int>();
            if (samples == null || samples.Count == 0 || valueSelector == null)
            {
                return indices;
            }

            start = Math.Clamp(start, 0, samples.Count - 1);
            end = Math.Clamp(end, start, samples.Count - 1);
            int count = end - start + 1;
            if (count <= 0)
            {
                return indices;
            }

            if (count == 1)
            {
                indices.Add(start);
                return indices;
            }

            int budget = Math.Max(4, maxPoints);
            if (count <= budget)
            {
                for (int i = start; i <= end; i++)
                {
                    indices.Add(i);
                }

                return indices;
            }

            int bucketCount = Math.Max(1, (budget - 2) / 2);
            int bucketSize = (int)Math.Ceiling(count / (double)bucketCount);
            int index = start;
            while (index <= end)
            {
                int bucketEnd = Math.Min(end, index + bucketSize - 1);
                int minIndex = index;
                int maxIndex = index;
                double minValue = valueSelector(samples[index]);
                double maxValue = minValue;

                for (int i = index + 1; i <= bucketEnd; i++)
                {
                    double value = valueSelector(samples[i]);
                    if (!double.IsFinite(value))
                    {
                        continue;
                    }

                    if (!double.IsFinite(minValue) || value < minValue)
                    {
                        minValue = value;
                        minIndex = i;
                    }

                    if (!double.IsFinite(maxValue) || value > maxValue)
                    {
                        maxValue = value;
                        maxIndex = i;
                    }
                }

                AddOrdered(indices, minIndex);
                AddOrdered(indices, maxIndex);
                index = bucketEnd + 1;
            }

            AddOrdered(indices, start);
            AddOrdered(indices, end);
            indices.Sort();
            return indices.Distinct().ToList();
        }

        private static void AddOrdered(List<int> indices, int value)
        {
            if (indices.Count == 0 || indices[^1] != value)
            {
                indices.Add(value);
            }
        }
    }
}
