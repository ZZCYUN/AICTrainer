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
                return new ModConfigDto
                {
                    DisableMosaic = Current.DisableMosaic,
                    LockHp = Current.LockHp,
                    TargetHp = Current.TargetHp,
                    LockMp = Current.LockMp,
                    TargetMp = Current.TargetMp,
                    ModifyMpCrack = Current.ModifyMpCrack,
                    TargetMpCrack = Current.TargetMpCrack,
                    ModifyInventoryCapacity = Current.ModifyInventoryCapacity,
                    TargetInventoryCapacity = Current.TargetInventoryCapacity,
                    NoWormTrap = Current.NoWormTrap,
                    NoSpikeDamage = Current.NoSpikeDamage,
                    NoThunderDamage = Current.NoThunderDamage,
                    NoDrownDamage = Current.NoDrownDamage,
                    NoAcidDamage = Current.NoAcidDamage,
                    OneHitKill = Current.OneHitKill,
                    ModifyMaxSatiety = Current.ModifyMaxSatiety,
                    TargetMaxSatiety = Current.TargetMaxSatiety,
                    ModifySatiety = Current.ModifySatiety,
                    TargetSatiety = Current.TargetSatiety,
                    ModifyEp = Current.ModifyEp,
                    TargetEp = Current.TargetEp,
                    ShieldNeverBreak = Current.ShieldNeverBreak,
                    FastTravelAnytime = Current.FastTravelAnytime,
                    FastTravelAnywhere = Current.FastTravelAnywhere,
                    AlwaysHungryBonus = Current.AlwaysHungryBonus,
                    LockWalkSpeed = Current.LockWalkSpeed,
                    WalkSpeedMultiplier = Current.WalkSpeedMultiplier,
                    LockRunSpeed = Current.LockRunSpeed,
                    RunSpeedMultiplier = Current.RunSpeedMultiplier,
                    ModifyKnockbackTime = Current.ModifyKnockbackTime,
                    TargetKnockbackTime = Current.TargetKnockbackTime,
                    BreakFoodAttrLimit = Current.BreakFoodAttrLimit,
                    ModifySlotCapacity = Current.ModifySlotCapacity,
                    TargetSlotCapacity = Current.TargetSlotCapacity,
                    SaveAnywhere = Current.SaveAnywhere,
                    FreezeCountdown = Current.FreezeCountdown,
                    ModifyDangerLevel = Current.ModifyDangerLevel,
                    TargetDangerLevel = Current.TargetDangerLevel,
                    LockDangerLevel = Current.LockDangerLevel,
                    LockGrip = Current.LockGrip,
                    TargetGrip = Current.TargetGrip,
                    NoGripReduction = Current.NoGripReduction,
                    InfiniteJump = Current.InfiniteJump,
                    ImmuneAbnormalStatus = Current.ImmuneAbnormalStatus,
                    InstantMagicCharge = Current.InstantMagicCharge,
                    NoBurstTired = Current.NoBurstTired,
                    JustGuardNoHit = Current.JustGuardNoHit,
                    ExtendedJustGuard = Current.ExtendedJustGuard,
                    LockGold = Current.LockGold,
                    TargetGold = Current.TargetGold,
                    LockCrafts = Current.LockCrafts,
                    TargetCrafts = Current.TargetCrafts,
                    LockJuice = Current.LockJuice,
                    TargetJuice = Current.TargetJuice,
                    LockBarScore = Current.LockBarScore,
                    TargetBarScore = Current.TargetBarScore,
                    LockGuildPoints = Current.LockGuildPoints,
                    TargetGuildPoints = Current.TargetGuildPoints,
                    LockLanthanum = Current.LockLanthanum,
                    TargetLanthanum = Current.TargetLanthanum
                };
            }
        }
    }
}
