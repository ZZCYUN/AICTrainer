using System;
using System.Reflection;
using HarmonyLib;
using nel;
using UnityEngine;

namespace AICMod.Patches
{
    public static class ReelPatch
    {
        // 1. 挂钩 ReelExecuter.decideRotate: 强制锁定最优物品与最优升级效果槽位
        [HarmonyPatch]
        public static class Patch_ReelExecuter_decideRotate
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(ReelExecuter), "decideRotate", new[] { typeof(bool) });
            }

            [HarmonyPrefix]
            public static void Prefix(ReelExecuter __instance, ref bool randomise)
            {
                if (__instance == null) return;
                if (!AICModConfig.Current.EnableBestReelReward) return;

                var acontent = ReflectionHelper.GetValue<string[]>(__instance, "Acontent");
                if (acontent == null)
                {
                    __instance.initReelContent(null);
                    acontent = ReflectionHelper.GetValue<string[]>(__instance, "Acontent");
                }

                if (acontent == null || acontent.Length == 0) return;
                if (__instance.content_id_dec >= 0) return;

                var ui = ReflectionHelper.GetValue<UiReelManager>(__instance, "Ui");
                int bestIndex = 0;
                var etype = __instance.getEType();

                if (etype == ReelExecuter.ETYPE.ITEMKIND)
                {
                    // 首盘：选择宝箱池中最优质/最稀有道具
                    var ir = ui?.getCurrentItemReelDrawer()?.IR;
                    if (ir != null && ir.Count > 0)
                    {
                        double maxScore = -1;
                        for (int i = 0; i < ir.Count; i++)
                        {
                            var entry = ir[i];
                            if (entry?.Data == null) continue;

                            double score = entry.Data.rare * 10000 + (int)entry.grade * 1000 + entry.count * 10 + entry.Data.price;
                            if (score > maxScore)
                            {
                                maxScore = score;
                                bestIndex = i;
                            }
                        }
                    }
                }
                else
                {
                    // 词条盘：优先选择数量翻倍(COUNT_MUL2)、品质+4/+3、数量+5/+3等最佳强化词条
                    int maxScore = -1;
                    for (int i = 0; i < acontent.Length; i++)
                    {
                        string s = acontent[i] ?? string.Empty;
                        int score = GetEffectScore(s);
                        if (score > maxScore)
                        {
                            maxScore = score;
                            bestIndex = i;
                        }
                    }
                }

                // 强制将旋转目标指向最优选项，并取消随机散布
                ReflectionHelper.SetValue(__instance, (float)bestIndex, "content_id");
                randomise = false;
            }

            [HarmonyPostfix]
            public static void Postfix(ReelExecuter __instance)
            {
                if (__instance == null) return;
                if (!AICModConfig.Current.EnableBestReelReward) return;

                // 若已生成结算物品行，保证星级拉至 4 星且保底数量充裕
                if (__instance.IKRow != null)
                {
                    __instance.IKRow.grade = 4;
                    if (__instance.IKRow.count < 5)
                    {
                        __instance.IKRow.count = 5;
                    }
                }
            }
        }

        // 2. 挂钩 ReelExecuter.applyEffectToIK: 确保结算时词条与金币收益最大化
        [HarmonyPatch]
        public static class Patch_ReelExecuter_applyEffectToIK
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(ReelExecuter), "applyEffectToIK");
            }

            [HarmonyPostfix]
            public static void Postfix(ReelExecuter __instance)
            {
                if (__instance == null) return;
                if (!AICModConfig.Current.EnableBestReelReward) return;

                if (__instance.IKRow != null)
                {
                    __instance.IKRow.grade = 4;
                    if (__instance.IKRow.count < 5)
                    {
                        __instance.IKRow.count = 5;
                    }
                }

                var ui = ReflectionHelper.GetValue<UiReelManager>(__instance, "Ui");
                if (ui != null && ui.added_money > 0)
                {
                    ui.added_money = Math.Max(ui.added_money, 9999);
                }
            }
        }

        private static int GetEffectScore(string s)
        {
            if (Enum.TryParse<ReelExecuter.EFFECT>(s, true, out var eff))
            {
                switch (eff)
                {
                    case ReelExecuter.EFFECT.COUNT_MUL2: return 1000;
                    case ReelExecuter.EFFECT.GRADE4: return 950;
                    case ReelExecuter.EFFECT.GRADE3: return 900;
                    case ReelExecuter.EFFECT.COUNT_ADD5: return 850;
                    case ReelExecuter.EFFECT.COUNT_ADD4: return 800;
                    case ReelExecuter.EFFECT.COUNT_ADD3: return 750;
                    case ReelExecuter.EFFECT.ADD_MONEY100: return 700;
                    case ReelExecuter.EFFECT.GRADE2: return 600;
                    case ReelExecuter.EFFECT.COUNT_ADD2: return 550;
                    case ReelExecuter.EFFECT.ADD_MONEY30: return 500;
                    case ReelExecuter.EFFECT.ADD_MONEY20: return 450;
                    case ReelExecuter.EFFECT.GRADE1: return 400;
                    case ReelExecuter.EFFECT.COUNT_ADD1: return 350;
                    case ReelExecuter.EFFECT.ADD_MONEY10: return 300;
                    case ReelExecuter.EFFECT.COUNT_MUL1: return 10; // 1倍，必须垫底，绝不选它！
                    case ReelExecuter.EFFECT.COUNT_ADD0: return 5;
                    case ReelExecuter.EFFECT.GRADE0: return 5;
                    default: return 50;
                }
            }

            if (s.IndexOf("COUNT_MUL2", StringComparison.OrdinalIgnoreCase) >= 0) return 1000;
            if (s.IndexOf("GRADE4", StringComparison.OrdinalIgnoreCase) >= 0) return 950;
            if (s.IndexOf("GRADE3", StringComparison.OrdinalIgnoreCase) >= 0) return 900;
            if (s.IndexOf("COUNT_ADD5", StringComparison.OrdinalIgnoreCase) >= 0) return 850;
            if (s.IndexOf("COUNT_ADD4", StringComparison.OrdinalIgnoreCase) >= 0) return 800;
            if (s.IndexOf("COUNT_ADD3", StringComparison.OrdinalIgnoreCase) >= 0) return 750;
            if (s.IndexOf("COUNT_MUL1", StringComparison.OrdinalIgnoreCase) >= 0) return 10;
            if (s.IndexOf("GRADE2", StringComparison.OrdinalIgnoreCase) >= 0) return 600;
            if (s.IndexOf("COUNT_ADD2", StringComparison.OrdinalIgnoreCase) >= 0) return 550;
            if (s.IndexOf("ADD_MONEY", StringComparison.OrdinalIgnoreCase) >= 0) return 500;
            return 10;
        }
    }
}
