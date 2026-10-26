using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Unite2026Seoul_MapGenCommonLib;


namespace Unite2026Seoul_MapGenAgent
{
    internal static class RandUtil
    {
        public static bool GetChance(in float chance)
        {
            FastRandom random = new FastRandom((uint)MapGenAgentInputBuffer.Instance.Data.Seed);
            if ((float)random.NextDouble() <= chance)
            {
                random = null;
                return true;
            }
            else
            {
                random = null;
                return false;
            }
        }
        public static bool GetHalfChance()
        {
            FastRandom random = new FastRandom((uint)MapGenAgentInputBuffer.Instance.Data.Seed);
            if (random.Next(0, 2) == 0)
            {
                random = null;
                return true;
            }
            else
            {
                random = null;
                return false;
            }
        }

        public static int SelectOne(in float[] chances)
        {
            FastRandom random = new FastRandom((uint)MapGenAgentInputBuffer.Instance.Data.Seed);

            float chanceSum = 0;
            for (int i = 0; i < chances.Length; i++)
            {
                chanceSum += chances[i];
            }

            float randomValue = (float)random.NextDouble() * chanceSum;
            float sumBuffer = 0;
            for (int index = 0; index < chances.Length; index++)
            {
                sumBuffer += chances[index];
                if (randomValue <= sumBuffer)
                {
                    return index;
                }
            }
            return -1;
        }
    }
}
