using System;
using System.Reflection;
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
        public static bool Prefix_canJump(M2FootManager? __instance, ref bool __result)
        {
            try
            {
                if (__instance == null)
                {
                    __result = true;
                    return false; // 防止对象未就绪时的空引用崩溃
                }
                if (AICModConfig.Current != null && AICModConfig.Current.InfiniteJump)
                {
                    __result = true;
                    return false; // 随时允许跳跃
                }
            }
            catch
            {
                __result = true;
                return false;
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

        // 7. 圣光爆发不触发眩晕 (NoBurstTired)
        // 7.1 计算眩晕概率始终归零 (UI与底层逻辑保持 0% 风险)
        [HarmonyPatch(typeof(MDAT), "calcBurstFaintedRatio")]
        [HarmonyPrefix]
        public static bool Prefix_MDAT_calcBurstFaintedRatio(ref float __result)
        {
            if (AICModConfig.Current.NoBurstTired)
            {
                __result = 0f;
                return false;
            }
            return true;
        }

        // 7.2 拦截向玩家施加 BURST_TIRED 与 OVERRUN_TIRED 状态
        [HarmonyPatch(typeof(M2Ser), "Add", new[] { typeof(SER), typeof(int), typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        public static bool Prefix_M2Ser_Add(M2Ser __instance, SER ser, ref M2SerItem? __result)
        {
            if (AICModConfig.Current.NoBurstTired && (ser == SER.BURST_TIRED || ser == SER.OVERRUN_TIRED))
            {
                if (__instance.Mv is PR)
                {
                    __result = null;
                    return false; // 豁免圣光爆发带来的眩晕与力竭
                }
            }
            return true;
        }

        // 7.3 爆发后清零魔力透支计数，并清除残余疲劳状态
        [HarmonyPatch(typeof(M2PrSkill), "runBurst")]
        [HarmonyPostfix]
        public static void Postfix_M2PrSkill_runBurst(M2PrSkill __instance)
        {
            if (AICModConfig.Current.NoBurstTired && __instance != null)
            {
                __instance.mp_overused = 0f;
                if (__instance.Pr?.Ser != null)
                {
                    __instance.Pr.Ser.Cure(SER.BURST_TIRED);
                    __instance.Pr.Ser.Cure(SER.OVERRUN_TIRED);
                    __instance.Pr.Ser.Cure(SER.FAINTED);
                }
            }
        }

        // =========================================================================
        // =========================================================================
        // 8. 展开护盾就触发精准防御 (无需受击 - JustGuardNoHit)
        // =========================================================================
        [HarmonyPatch(typeof(M2Shield), "activate", new[] { typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        public static void Prefix_M2Shield_activate(M2Shield __instance)
        {
            if (AICModConfig.Current.JustGuardNoHit && __instance != null && __instance.alpha <= 0f)
            {
                if (__instance.Mv is PR pr && pr.Skill?.ShE != null)
                {
                    // 展开护盾瞬间直接触发精准防御（视效/音效/慢镜头/居合反击就绪），无需受到任何敌方攻击
                    pr.Skill.ShE.initJustGuard(null);
                }
            }
        }

        // =========================================================================
        // 9. 展开护盾受到攻击就精准防御 (拉长时间 - ExtendedJustGuard)
        // =========================================================================
        [HarmonyPatch(typeof(M2Shield), "checkShield", new[] { typeof(AttackInfo), typeof(float), typeof(bool) })]
        [HarmonyPostfix]
        public static void Postfix_M2Shield_checkShield(M2Shield __instance, AttackInfo Atk, bool from_away, ref M2Shield.RESULT __result)
        {
            if (AICModConfig.Current.ExtendedJustGuard && __instance != null && __instance.isActive())
            {
                // 举盾受击必定触发精准防御：若成功格挡或受到敌方攻击，均升级为精准防御成功
                if (__result == M2Shield.RESULT.GUARD || __result == M2Shield.RESULT.GUARD_CONTINUE)
                {
                    __result |= M2Shield.RESULT._JUSTGUARD_SUCCESS;
                }
                else if (!from_away && Atk is NelAttackInfo nelAtk && nelAtk.Caster as M2Mover != __instance.Mv)
                {
                    __result = M2Shield.RESULT.GUARD_CONTINUE | M2Shield.RESULT._JUSTGUARD_SUCCESS;
                }
            }
        }

        // =========================================================================
        // 10. 精准防御通用支持 (技能解锁与反击衔接优化)
        // =========================================================================
        // 10.1 豁免技能树解锁需求：强制允许居合闪避、精准防御、护盾反击与格挡
        [HarmonyPatch(typeof(M2PrSkill), "isEnable", new[] { typeof(SkillManager.SKILL_TYPE) })]
        [HarmonyPostfix]
        public static void Postfix_M2PrSkill_isEnable(SkillManager.SKILL_TYPE type, ref bool __result)
        {
            if (AICModConfig.Current.JustGuardNoHit || AICModConfig.Current.ExtendedJustGuard)
            {
                if (type == SkillManager.SKILL_TYPE.evade_dancing
                    || type == SkillManager.SKILL_TYPE.justguard
                    || type == SkillManager.SKILL_TYPE.shield_counter
                    || type == SkillManager.SKILL_TYPE.guard)
                {
                    __result = true;
                }
            }
        }

        // 10.2 举盾反击衔接：在精准防御就绪期间，即便按住举盾，输入 方向+攻击 也优先衔接居合反击
        [HarmonyPatch(typeof(M2PrSkillShieldEvade), "getPunchVariation", new[] { typeof(bool), typeof(PR.STATE), typeof(bool) }, new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref })]
        [HarmonyPrefix]
        public static bool Prefix_M2PrSkillShieldEvade_getPunchVariation(M2PrSkillShieldEvade __instance, ref PR.STATE ret, ref bool change_dir, ref bool __result)
        {
            if ((AICModConfig.Current.JustGuardNoHit || AICModConfig.Current.ExtendedJustGuard) && __instance != null)
            {
                if (__instance.enabled_evadecounter_input && (__instance.Pr.isLO() || __instance.Pr.isRO()))
                {
                    ret = (__instance.Skill.getCurMagic() != null) ? PR.STATE.EVADECOUNTER_SHOTGUN : PR.STATE.EVADECOUNTER;
                    change_dir = true;
                    __result = true;
                    return false;
                }
            }
            return true;
        }

        // =========================================================================
        // 11. 禁止受击判定 (DisableHitCheck)
        // =========================================================================
        // 11.1 敌方普通攻击直接忽略 (不触发受击判定)
        [HarmonyPatch(typeof(M2PrSkill), "normalAttackIgnorable")]
        [HarmonyPostfix]
        public static void Postfix_M2PrSkill_normalAttackIgnorable(ref bool __result)
        {
            if (AICModConfig.Current.DisableHitCheck)
            {
                __result = true;
            }
        }

        // 11.2 核心伤害与受击拦截：豁免一切技能/魔法/重击伤害、硬直与受击动作
        [HarmonyPatch]
        public static class Patch_M2PrADmg_applyDamage
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(M2PrADmg), "applyDamage", new[] {
                    typeof(NelAttackInfo),
                    typeof(HITTYPE).MakeByRefType(),
                    typeof(bool),
                    typeof(string),
                    typeof(bool),
                    typeof(bool)
                });
            }

            [HarmonyPrefix]
            public static bool Prefix(ref int __result)
            {
                if (AICModConfig.Current.DisableHitCheck)
                {
                    __result = 0;
                    return false;
                }
                return true;
            }
        }

        // 11.3 拦截一切抓取、吸收与拘束
        [HarmonyPatch]
        public static class Patch_PR_initAbsorb
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(PR), "initAbsorb", new[] {
                    typeof(NelAttackInfo),
                    typeof(NelM2Attacker),
                    typeof(AbsorbManager),
                    typeof(bool)
                });
            }

            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (AICModConfig.Current.DisableHitCheck)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }
    }
}
