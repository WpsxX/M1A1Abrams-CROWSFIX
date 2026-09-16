using System;

namespace M1A1Abrams
{
    internal static class AmmoLoadoutRules
    {
        // Clamp invalid counts, then fit the capacity without discarding a small third/fourth
        // selection just because the first two selections already fill the tank.
        internal static int[] Fit(int[] requested, int capacity)
        {
            var result = new int[requested.Length];
            long total = 0;
            int kinds = 0;
            for (int i = 0; i < requested.Length; i++)
            {
                result[i] = Math.Max(0, requested[i]);
                total += result[i];
                if (result[i] > 0) kinds++;
            }
            capacity = Math.Max(0, capacity);
            if (total <= capacity) return result;

            var weights = new long[requested.Length];
            var remainders = new long[requested.Length];
            int reserved = capacity >= kinds ? 1 : 0;
            long weightTotal = total - (long)reserved * kinds;
            int budget = capacity - reserved * kinds;
            int assigned = 0;
            for (int i = 0; i < result.Length; i++)
            {
                weights[i] = Math.Max(0, result[i] - reserved);
                int minimum = result[i] > 0 ? reserved : 0;
                long scaled = weights[i] * budget;
                result[i] = minimum + (weightTotal == 0 ? 0 : (int)(scaled / weightTotal));
                remainders[i] = weightTotal == 0 ? 0 : scaled % weightTotal;
                assigned += result[i];
            }
            while (assigned < capacity)
            {
                int best = -1;
                for (int i = 0; i < result.Length; i++)
                    if (weights[i] > 0 && (best < 0 || remainders[i] > remainders[best])) best = i;
                if (best < 0) break;
                result[best]++;
                remainders[best] = -1;
                assigned++;
            }
            return result;
        }
    }
}
