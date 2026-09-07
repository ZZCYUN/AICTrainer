using System;
using System.Reflection;
using HarmonyLib;
using m2d;
using nel;
using UnityEngine;
using XX;

namespace AICMod.Patches
{
    public static class CombatPatch
    {
        // 1. 伤害倍率与秒杀比例：Hook NelEnemy.applyHpDamageRatio 保证底层伤害结算与 DamageCounter 飘字完全同步放大
        [HarmonyPatch(typeof(NelEnemy), "applyHpDamageRatio", new[] { typeof(AttackInfo) })]
        [HarmonyPostfix]
        public static void Postfix_NelEnemy_applyHpDamageRatio(NelEnemy __instance, AttackInfo Atk, ref float __result)
        {
            if (Atk == null || __instance == null) return;

            object? caster = (Atk as NelAttackInfo)?.Caster ?? (object?)Atk.AttackFrom ?? (Atk as NelAttackInfo)?.PublishMagic?.Caster;
            bool isPlayer = caster is M2AttackableP || caster is PR || caster is M2MoverPr || (caster is MagicItem mg && (mg.Caster is PR || mg.Caster is M2MoverPr));
            if (!isPlayer) return;

            var cfg = AICModConfig.Current;
            if (cfg.OneHitKill)
            {
                __result = Math.Max(__result, 100f);
            }
            else if (cfg.EnableDamageMultiplier && cfg.DamageMultiplier > 0f && Math.Abs(cfg.DamageMultiplier - 1.0f) > 0.001f)
            {
                __result = SafeMultiplyDamageRatio(__result, cfg.DamageMultiplier);
            }
        }

        public static float SafeMultiplyDamageRatio(float baseRatio, float multiplier)
        {
            if (multiplier <= 0f) return 0f;
            double scaled = (double)baseRatio * multiplier;
            if (scaled > 2000000000.0) scaled = 2000000000.0;
            if (double.IsNaN(scaled) || double.IsInfinity(scaled)) scaled = 2000000000.0;
            return (float)scaled;
        }

        public static int SafeMultiplyDamageInt(int baseDamage, float multiplier)
        {
            if (multiplier <= 0f) return 0;
            double scaled = (double)baseDamage * multiplier;
            if (scaled > 2000000000.0) scaled = 2000000000.0;
            if (double.IsNaN(scaled) || double.IsInfinity(scaled)) scaled = 2000000000.0;
            return (int)Math.Max(1, Math.Round(scaled));
        }

        [ThreadStatic]
        private static bool _insideApplyDamage;

        public struct AttackDamageBackup
        {
            public bool Active;
            public int hpdmg0;
            public int hpdmg_current;
            public int mpdmg0;
            public int mpdmg_current;
        }

        // 1b. 伤害倍率修改与一击秒杀：挂钩 NelEnemy.applyDamage 确保秒杀、固定伤害(fix_damage)及 MP 伤害完全同步
        [HarmonyPatch]
        public static class Patch_NelEnemy_applyDamage
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(NelEnemy), "applyDamage", new[] {
                    typeof(NelAttackInfo),
                    typeof(HITTYPE).MakeByRefType(),
                    typeof(bool)
                });
            }

            [HarmonyPrefix]
            public static void Prefix(NelEnemy __instance, NelAttackInfo Atk, out AttackDamageBackup __state)
            {
                __state = default;
                if (Atk == null || __instance == null) return;

                var cfg = AICModConfig.Current;
                object? caster = Atk.Caster ?? (object?)Atk.AttackFrom ?? Atk.PublishMagic?.Caster;
                bool isPlayer = caster is M2AttackableP || caster is PR || caster is M2MoverPr || (caster is MagicItem mg && (mg.Caster is PR || mg.Caster is M2MoverPr));
                if (!isPlayer) return;

                _insideApplyDamage = true;

                if (cfg.OneHitKill)
                {
                    __state = new AttackDamageBackup
                    {
                        Active = true,
                        hpdmg0 = Atk.hpdmg0,
                        hpdmg_current = Atk.hpdmg_current,
                        mpdmg0 = Atk.mpdmg0,
                        mpdmg_current = Atk.mpdmg_current
                    };
                    int maxHp = (int)__instance.get_maxhp();
                    int killVal = Math.Max(9999999, maxHp * 64);
                    Atk.hpdmg0 = killVal;
                    if (Atk.hpdmg_current != -1000)
                    {
                        Atk.hpdmg_current = killVal;
                    }
                    return;
                }

                if (cfg.EnableDamageMultiplier && cfg.DamageMultiplier > 0f && Math.Abs(cfg.DamageMultiplier - 1.0f) > 0.001f)
                {
                    __state = new AttackDamageBackup
                    {
                        Active = true,
                        hpdmg0 = Atk.hpdmg0,
                        hpdmg_current = Atk.hpdmg_current,
                        mpdmg0 = Atk.mpdmg0,
                        mpdmg_current = Atk.mpdmg_current
                    };

                    // 若攻击指定固定伤害 (fix_damage)，此时 applyHpDamageRatio 不会参与运算，因此直接放大 Atk 的基础伤害
                    if (Atk.fix_damage)
                    {
                        Atk.hpdmg0 = SafeMultiplyDamageInt(Atk.hpdmg0, cfg.DamageMultiplier);
                        if (Atk.hpdmg_current != -1000)
                        {
                            Atk.hpdmg_current = SafeMultiplyDamageInt(Atk.hpdmg_current, cfg.DamageMultiplier);
                        }
                    }
                    if (Atk.mpdmg0 > 0)
                    {
                        Atk.mpdmg0 = SafeMultiplyDamageInt(Atk.mpdmg0, cfg.DamageMultiplier);
                    }
                    if (Atk.mpdmg_current != -1000 && Atk.mpdmg_current > 0)
                    {
                        Atk.mpdmg_current = SafeMultiplyDamageInt(Atk.mpdmg_current, cfg.DamageMultiplier);
                    }
                }
            }

            [HarmonyPostfix]
            public static void Postfix(NelAttackInfo Atk, AttackDamageBackup __state)
            {
                _insideApplyDamage = false;

                if (!__state.Active || Atk == null) return;

                Atk.hpdmg0 = __state.hpdmg0;
                Atk.hpdmg_current = __state.hpdmg_current;
                Atk.mpdmg0 = __state.mpdmg0;
                Atk.mpdmg_current = __state.mpdmg_current;
            }
        }

        // 1c. 兜底挂钩 NelEnemy.applyHpDamage（针对未走 applyDamage 的底层直接扣血，避免二次放大）
        [HarmonyPatch]
        public static class Patch_NelEnemy_applyHpDamage
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(NelEnemy), "applyHpDamage", new[] {
                    typeof(int),
                    typeof(int).MakeByRefType(),
                    typeof(bool),
                    typeof(NelAttackInfo)
                });
            }

            [HarmonyPrefix]
            public static void Prefix(NelEnemy __instance, ref int val, ref int mpdmg, NelAttackInfo Atk)
            {
                if (_insideApplyDamage) return; // applyDamage 已统一放大处理，避免二次复合乘算
                if (Atk == null || __instance == null) return;

                var cfg = AICModConfig.Current;
                object? caster = Atk.Caster ?? (object?)Atk.AttackFrom ?? Atk.PublishMagic?.Caster;
                bool isPlayer = caster is M2AttackableP || caster is PR || caster is M2MoverPr || (caster is MagicItem mg && (mg.Caster is PR || mg.Caster is M2MoverPr));
                if (!isPlayer) return;

                if (cfg.OneHitKill)
                {
                    int maxHp = (int)__instance.get_maxhp();
                    val = Math.Max(9999999, maxHp * 64);
                    return;
                }

                if (cfg.EnableDamageMultiplier && cfg.DamageMultiplier > 0f && Math.Abs(cfg.DamageMultiplier - 1.0f) > 0.001f)
                {
                    val = SafeMultiplyDamageInt(val, cfg.DamageMultiplier);
                    if (mpdmg > 0)
                    {
                        mpdmg = SafeMultiplyDamageInt(mpdmg, cfg.DamageMultiplier);
                    }
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

        // 12. 诺艾尔攻击判定区增大倍率 (攻击范围倍率)
        public struct RayBackupState
        {
            public bool Active;
            public float Radius;
            public float Len;
            public float LenMp;
            public int AttackMax;
        }

        [HarmonyPatch]
        public static class Patch_M2Ray_Cast
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(M2Ray), "Cast", new[] {
                    typeof(bool),
                    typeof(RaycastHit2D[]),
                    typeof(bool)
                });
            }

            public static bool IsNoelAttack(M2Ray ray, out MGKIND kind)
            {
                kind = MGKIND.NONE;
                if (ray == null) return false;

                bool isPr = ray.Caster is PR || (ray.Caster is M2Mover mv && mv is PR);
                var nelAtk = ray.Atk as NelAttackInfo;
                if (!isPr && nelAtk != null)
                {
                    object? caster = nelAtk.Caster ?? (object?)nelAtk.AttackFrom ?? nelAtk.PublishMagic?.Caster;
                    isPr = caster is PR || caster is M2MoverPr || (caster is MagicItem mgc && (mgc.Caster is PR || mgc.Caster is M2MoverPr));
                }

                if (isPr)
                {
                    var mg = nelAtk?.PublishMagic;
                    if (mg != null)
                    {
                        kind = mg.kind;
                    }
                    return true;
                }
                return false;
            }

            public static bool IsMeleeKind(MGKIND kind)
            {
                return kind == MGKIND.PR_PUNCH ||
                       kind == MGKIND.PR_SHOTGUN ||
                       kind == MGKIND.PR_SLIDING ||
                       kind == MGKIND.PR_WHEEL ||
                       kind == MGKIND.PR_COMET ||
                       kind == MGKIND.PR_SHIELD_BUSH ||
                       kind == MGKIND.PR_SHIELD_LARIAT ||
                       kind == MGKIND.PR_DASHPUNCH ||
                       kind == MGKIND.PR_EVADECOUNTER ||
                       kind == MGKIND.PR_SMASH ||
                       kind == MGKIND.PR_BURST ||
                       kind == MGKIND.PR_SHINEBOOSTER ||
                       kind == MGKIND.PR_EVADE_JUMP ||
                       kind == MGKIND.NONE;
            }

            [HarmonyPrefix]
            public static void Prefix(M2Ray __instance, out RayBackupState __state)
            {
                __state = default;
                if (__instance == null) return;

                var cfg = AICModConfig.Current;
                if (!cfg.EnableNoelAttackScale) return;

                float scale = Math.Max(0f, Math.Min(10f, cfg.NoelAttackScale));
                if (Math.Abs(scale - 1.0f) < 0.001f) return;

                if (!IsNoelAttack(__instance, out var kind)) return;
                if (cfg.NoelAttackOnlyMelee && !IsMeleeKind(kind)) return;

                __state = new RayBackupState
                {
                    Active = true,
                    Radius = __instance.radius,
                    Len = __instance.len,
                    LenMp = __instance.lenmp,
                    AttackMax = (__instance.Atk as NelAttackInfo)?.attack_max0 ?? 0
                };

                __instance.radius *= scale;
                if (__instance.len > 0f)
                {
                    __instance.len *= scale;
                    __instance.lenmp *= scale;
                }

                if (__instance.Atk is NelAttackInfo nelAtk)
                {
                    if (nelAtk.attack_max0 > 0 && nelAtk.attack_max0 < 16)
                    {
                        nelAtk.attack_max0 = 16;
                        nelAtk.resetAttackCount();
                    }
                }
            }

            [HarmonyPostfix]
            public static void Postfix(M2Ray __instance, RayBackupState __state)
            {
                if (!__state.Active || __instance == null) return;

                __instance.radius = __state.Radius;
                __instance.len = __state.Len;
                __instance.lenmp = __state.LenMp;
                if (__instance.Atk is NelAttackInfo nelAtk && __state.AttackMax > 0)
                {
                    nelAtk.attack_max0 = __state.AttackMax;
                    nelAtk.resetAttackCount();
                }
            }
        }

        // 12b. 针对诺艾尔近战冲撞/挥击/脚踢等基础招式，挂钩 runTackle 扩大判定范围与距离
        [HarmonyPatch(typeof(MagicItem), "runTackle")]
        public static class Patch_MagicItem_runTackle
        {
            [HarmonyPrefix]
            public static void Prefix(MagicItem Mg, out (bool Active, float sx, float sy, float sz) __state)
            {
                __state = default;
                if (Mg == null) return;
                var cfg = AICModConfig.Current;
                if (!cfg.EnableNoelAttackScale) return;
                float scale = Math.Max(0f, Math.Min(10f, cfg.NoelAttackScale));
                if (Math.Abs(scale - 1.0f) < 0.001f) return;

                bool isPr = Mg.Caster is PR || Mg.Caster is M2MoverPr;
                if (!isPr) return;
                if (cfg.NoelAttackOnlyMelee && !Patch_M2Ray_Cast.IsMeleeKind(Mg.kind)) return;

                __state = (true, Mg.sx, Mg.sy, Mg.sz);
                Mg.sx *= scale;
                Mg.sy *= scale;
                Mg.sz *= scale;
            }

            [HarmonyPostfix]
            public static void Postfix(MagicItem Mg, (bool Active, float sx, float sy, float sz) __state)
            {
                if (!__state.Active || Mg == null) return;
                Mg.sx = __state.sx;
                Mg.sy = __state.sy;
                Mg.sz = __state.sz;
            }
        }
    }
}
