using System;
using AICShared;

namespace AICMod
{
    public static class AICModConfig
    {
        private static readonly object _lock = new object();
        public static ModConfigDto Current { get; private set; } = new ModConfigDto();

        public static void Update(ModConfigDto newConfig)
        {
            lock (_lock)
            {
                Current = newConfig ?? new ModConfigDto();
            }
        }

        public static ModConfigDto Clone()
        {
            lock (_lock)
            {
                return Current.Clone();
            }
        }
    }
}
