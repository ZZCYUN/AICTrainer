using HarmonyLib;
using m2d;
using nel;
using XX;

namespace AICMod.Patches
{
    public static class HazardPatch
    {
        // 1. 虫墙无效: 强行返回 false，无法被虫墙拖入
        [HarmonyPatch(typeof(PR), "canPullByWorm")]
        [HarmonyPrefix]
        public static bool Prefix_canPullByWorm(ref bool __result)
        {
            if (AICModConfig.Current.NoWormTrap || AICModConfig.Current.DisableHitCheck)
            {
                __result = false;
                return false;
            }
            return true;
        }

        // 2. 荆棘无效 & 电击无效 (地图伤害): 判定并豁免对应类型
        [HarmonyPatch(typeof(PR), "applyDamageFromMap", new[] { typeof(M2MapDamageContainer.M2MapDamageItem), typeof(AttackInfo), typeof(float), typeof(float), typeof(bool) })]
        [HarmonyPrefix]
        public static bool Prefix_applyDamageFromMap(M2MapDamageContainer.M2MapDamageItem MDI, ref AttackInfo? __result)
        {
            if (MDI != null)
            {
                if (AICModConfig.Current.NoSpikeDamage && MDI.kind == MAPDMG.SPIKE)
                {
                    __result = null;
                    return false;
                }
                if (AICModConfig.Current.NoThunderDamage && (MDI.kind == MAPDMG.THUNDER || MDI.kind == MAPDMG.THUNDER_STATIC))
                {
                    __result = null;
                    return false;
                }
            }
            return true;
        }

        // 3. 电击无效 (怪物攻击伤害)
        [HarmonyPatch(typeof(PR), "applyDamage", new[] { typeof(NelAttackInfo), typeof(bool) })]
        [HarmonyPrefix]
        public static bool Prefix_applyEnemyDamage(NelAttackInfo Atk, ref int __result)
        {
            if (AICModConfig.Current.NoThunderDamage && Atk != null && Atk.attr == MGATTR.THUNDER)
            {
                __result = 0;
                return false;
            }
            return true;
        }

        // 4. 溺水无效
        [HarmonyPatch(typeof(PR), "applyWaterChokeDamage")]
        [HarmonyPrefix]
        public static bool Prefix_applyWaterChokeDamage(ref int __result)
        {
            if (AICModConfig.Current.NoDrownDamage)
            {
                __result = 0;
                return false;
            }
            return true;
        }

        // 5. 酸液 / 毒气无效 (applyGasDamage)
        [HarmonyPatch(typeof(PR), "applyGasDamage", new[] { typeof(MistManager.MistKind), typeof(float) })]
        [HarmonyPrefix]
        public static bool Prefix_applyGasDamage1(ref int __result)
        {
            if (AICModConfig.Current.NoAcidDamage)
            {
                __result = 0;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(PR), "applyGasDamage", new[] { typeof(MistManager.MistKind), typeof(MistAttackInfo) })]
        [HarmonyPrefix]
        public static bool Prefix_applyGasDamage2()
        {
            if (AICModConfig.Current.NoAcidDamage)
            {
                return false; // 跳过毒雾/酸液伤害执行
            }
            return true;
        }
    }
}
