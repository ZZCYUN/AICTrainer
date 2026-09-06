using System;
using HarmonyLib;
using m2d;
using nel;
using XX;

namespace AICMod.Patches
{
    public static class CombatPatch
    {
        // 1. 秒杀：Hook NelEnemy 的核心伤害结算与伤害倍率，并同步游戏原生 DEBUGMIGHTY 标志
        [HarmonyPatch(typeof(NelEnemy), "applyDamage", new[] { typeof(NelAttackInfo), typeof(HITTYPE), typeof(bool) }, new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
        [HarmonyPrefix]
        public static void Prefix_NelEnemy_applyDamage(NelEnemy __instance, NelAttackInfo Atk)
        {
            if (AICModConfig.Current.OneHitKill && Atk != null && __instance != null)
            {
                // 仅当玩家（Noel）或其魔法/法球造成伤害时触发秒杀，避免怪物间互动出现异常
                if (Atk.Caster is M2AttackableP || Atk.Caster is PR || (Atk.Caster is MagicItem mg && mg.Caster is PR))
                {
                    int maxHp = (int)__instance.get_maxhp();
                    int lethal = Math.Max(9999999, maxHp * 64);
                    Atk.hpdmg0 = lethal;
                    Atk.hpdmg_current = lethal;
                }
            }
        }

        [HarmonyPatch(typeof(NelEnemy), "applyHpDamageRatio", new[] { typeof(AttackInfo) })]
        [HarmonyPostfix]
        public static void Postfix_NelEnemy_applyHpDamageRatio(NelEnemy __instance, AttackInfo Atk, ref float __result)
        {
            if (AICModConfig.Current.OneHitKill && Atk != null && __instance != null)
            {
                object? caster = (Atk as NelAttackInfo)?.Caster ?? (object?)Atk.AttackFrom;
                if (caster is M2AttackableP || caster is PR || (caster is MagicItem mg && mg.Caster is PR))
                {
                    __result = Math.Max(__result, 100f);
                }
            }
        }

        // 2. 护盾不碎
        [HarmonyPatch(typeof(M2Shield), "breakShield")]
        [HarmonyPrefix]
        public static bool Prefix_ShieldBreak(M2Shield __instance, ref M2Shield.RESULT __result)
        {
            if (AICModConfig.Current.ShieldNeverBreak)
            {
                __result = M2Shield.RESULT.GUARD;
                return false; // 拦截盾破执行
            }
            return true;
        }

        // 3. 可连跳 (无限多段跳)
        [HarmonyPatch(typeof(M2FootManager), "canJump")]
        [HarmonyPrefix]
        public static bool Prefix_canJump(ref bool __result)
        {
            if (AICModConfig.Current.InfiniteJump)
            {
                __result = true;
                return false; // 随时允许跳跃
            }
            return true;
        }

        // 4. 停止抓地力修改 (防滑防摔，锁定冰面/斜坡摩擦力)
        [HarmonyPatch(typeof(M2Phys), "addOnIce", new[] { typeof(bool), typeof(float) })]
        [HarmonyPrefix]
        public static bool Prefix_addOnIce()
        {
            if (AICModConfig.Current.NoGripReduction)
            {
                return false; // 阻止施加冰面打滑
            }
            return true;
        }

        [HarmonyPatch(typeof(M2Phys), "LockSlopeSlideFrame")]
        [HarmonyPrefix]
        public static bool Prefix_LockSlopeSlideFrame()
        {
            if (AICModConfig.Current.NoGripReduction)
            {
                return false; // 阻止斜坡滑步锁定
            }
            return true;
        }

        // 5. 异常状态无效
        [HarmonyPatch(typeof(M2Ser), "applySerDamage")]
        [HarmonyPrefix]
        public static bool Prefix_applySerDamage(M2Ser __instance, ref bool __result)
        {
            if (AICModConfig.Current.ImmuneAbnormalStatus)
            {
                if (__instance.Mv is PR)
                {
                    __result = false;
                    return false; // 豁免玩家异常状态施加
                }
            }
            return true;
        }

        // 6. 魔法蓄力秒完成 (瞬发满蓄)
        [HarmonyPatch(typeof(PR), "getCastingTimeScale", new[] { typeof(MagicItem) })]
        [HarmonyPostfix]
        public static void Postfix_PR_getCastingTimeScale(ref float __result)
        {
            if (AICModConfig.Current.InstantMagicCharge)
            {
                __result = Math.Max(__result, 500f);
            }
        }
    }
}
