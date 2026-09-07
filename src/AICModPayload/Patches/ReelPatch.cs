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
                    // 词条盘：选择品质+3、数量翻倍、数量+3等最佳强化词条
                    int maxScore = -1;
                    for (int i = 0; i < acontent.Length; i++)
                    {
                        string s = acontent[i] ?? string.Empty;
                        int score = 10;
                        if (s.IndexOf("GRADE3", StringComparison.OrdinalIgnoreCase) >= 0) score = 100;
                        else if (s.IndexOf("COUNT_MUL", StringComparison.OrdinalIgnoreCase) >= 0) score = 95;
                        else if (s.IndexOf("COUNT_ADD3", StringComparison.OrdinalIgnoreCase) >= 0) score = 90;
                        else if (s.IndexOf("RARE", StringComparison.OrdinalIgnoreCase) >= 0) score = 85;
                        else if (s.IndexOf("GRADE2", StringComparison.OrdinalIgnoreCase) >= 0) score = 80;
                        else if (s.IndexOf("ADD_MONEY", StringComparison.OrdinalIgnoreCase) >= 0) score = 75;
                        else if (s.IndexOf("COUNT_ADD2", StringComparison.OrdinalIgnoreCase) >= 0) score = 70;
                        else if (s.IndexOf("GRADE1", StringComparison.OrdinalIgnoreCase) >= 0) score = 60;
                        else if (s.IndexOf("COUNT_ADD1", StringComparison.OrdinalIgnoreCase) >= 0) score = 50;

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
    }
}
