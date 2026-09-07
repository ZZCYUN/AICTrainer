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
                    // 首盘：选择宝箱池中最优质/最稀有道具（技能书/配方/任务道具 > 高稀有度/高品质 > 普通素材）
                    var ir = ui?.getCurrentItemReelDrawer()?.IR;
                    if (ir != null && ir.Count > 0)
                    {
                        double maxScore = -1;
                        for (int i = 0; i < ir.Count; i++)
                        {
                            var entry = ir[i];
                            if (entry?.Data == null) continue;

                            double score = 0;
                            // 1. 独占书籍、配方、强化器、魔杖、贵重品具备绝对最高优先级
                            if (entry.Data.is_skillbook || entry.Data.is_recipe || entry.Data.is_enhancer || entry.Data.is_precious || entry.Data.is_cane)
                            {
                                score += 10000000;
                            }
                            // 2. 任务目标道具次高优先级
                            if (m2d.M2DBase.Instance is NelM2DBase nelM2D && nelM2D.QUEST?.isQuestTargetItem(entry.Data) == true)
                            {
                                score += 5000000;
                            }
                            // 3. 稀有度、初始星级、数量、售价综合权重
                            score += entry.Data.rare * 10000 + (int)entry.grade * 1000 + entry.count * 10 + entry.Data.price;

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
                    // 获取当前已选中的物品（用于词条动态收益评估）
                    var reelIk = ReflectionHelper.GetValue<ReelExecuter>(ui, "ReelIK");
                    var currentIkRow = reelIk?.IKRow;

                    // 词条盘：优先选择数量翻倍(COUNT_MUL2)、品质+4/+3、数量+5/+3等最佳强化词条
                    int maxScore = -1;
                    for (int i = 0; i < acontent.Length; i++)
                    {
                        string s = acontent[i] ?? string.Empty;
                        int score = GetEffectScore(s, currentIkRow);
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

        private static int GetEffectScore(string s, NelItemEntry? ikRow = null)
        {
            if (Enum.TryParse<ReelExecuter.EFFECT>(s, true, out var eff))
            {
                switch (eff)
                {
                    case ReelExecuter.EFFECT.COUNT_MUL2:
                        // 翻倍收益评估：基础数量越多，翻倍价值呈指数级爆发
                        if (ikRow != null)
                        {
                            if (ikRow.count >= 3) return 1000; // 净增 >= 3 个，全场最高
                            if (ikRow.count == 2) return 800;  // 净增 2 个
                            return 650;                        // 净增 1 个 (稍低于固定加3)
                        }
                        return 1000;

                    case ReelExecuter.EFFECT.GRADE4:
                        // 品质直接拉满至最高4星顶级品质
                        if (ikRow != null && ikRow.grade >= 4) return 10; // 若已满星则无实际收益
                        return 950;

                    case ReelExecuter.EFFECT.GRADE3:
                        // 品质+3星
                        if (ikRow != null)
                        {
                            if (ikRow.grade >= 4) return 10;  // 已满星
                            if (ikRow.grade == 3) return 450; // 溢出2星，实际收益+1星
                            return 920;                       // 0~1星直接提升3星，质变级收益
                        }
                        return 900;

                    case ReelExecuter.EFFECT.COUNT_ADD5: return 850;
                    case ReelExecuter.EFFECT.COUNT_ADD4: return 800;
                    case ReelExecuter.EFFECT.COUNT_ADD3: return 750;
                    case ReelExecuter.EFFECT.ADD_MONEY100: return 700;

                    case ReelExecuter.EFFECT.GRADE2:
                        if (ikRow != null && ikRow.grade >= 4) return 10;
                        return 600;

                    case ReelExecuter.EFFECT.COUNT_ADD2: return 550;
                    case ReelExecuter.EFFECT.ADD_MONEY30: return 500;
                    case ReelExecuter.EFFECT.ADD_MONEY20: return 450;

                    case ReelExecuter.EFFECT.GRADE1:
                        if (ikRow != null && ikRow.grade >= 4) return 10;
                        return 400;

                    case ReelExecuter.EFFECT.COUNT_ADD1: return 350;
                    case ReelExecuter.EFFECT.ADD_MONEY10: return 300;
                    case ReelExecuter.EFFECT.COUNT_MUL1: return 10; // 1倍等于不翻倍，垫底！
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
