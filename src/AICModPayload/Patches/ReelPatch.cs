using System;
using System.Reflection;
using HarmonyLib;
using nel;
using UnityEngine;

namespace AICMod.Patches
{
    public static class ReelPatch
    {
        // 1. 转速控制（区分首轮慢速与加成轮减速）：挂钩 ReelExecuter.progressRotate
        [HarmonyPatch]
        public static class Patch_ReelExecuter_progressRotate
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(ReelExecuter), "progressRotate", new[] { typeof(int) });
            }

            [HarmonyPrefix]
            public static void Prefix(ReelExecuter __instance)
            {
                if (__instance == null) return;
                var cfg = AICModConfig.Current;
                if (!cfg.EnableBestReelReward) return;

                var etype = __instance.getEType();
                if (etype == ReelExecuter.ETYPE.ITEMKIND)
                {
                    if (cfg.ReelFirstSlow)
                    {
                        // 正常转速为 1f / 9f (~0.1111)，首轮放慢约 5 倍至 1f / 45f (~0.0222)，便于看清候选并手动选择
                        ReflectionHelper.SetValue(__instance, 1f / 45f, "reel_speed");
                    }
                }
                else
                {
                    if (cfg.ReelBonusSlow)
                    {
                        // 加成轮放慢约 5 倍至 1f / 45f，便于看清加成词条手动停盘
                        ReflectionHelper.SetValue(__instance, 1f / 45f, "reel_speed");
                    }
                }
            }
        }

        // 2. 自动推进控制（区分首轮自动选择与加成轮自动选择）：挂钩 UiReelManager.runIRD
        [HarmonyPatch]
        public static class Patch_UiReelManager_run
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(UiReelManager), "runIRD", new[] { typeof(float) });
            }

            [HarmonyPrefix]
            public static void Prefix(UiReelManager __instance)
            {
                if (__instance == null) return;
                var cfg = AICModConfig.Current;
                if (!cfg.EnableBestReelReward) return;

                int rotateDecideId = ReflectionHelper.GetValue<int>(__instance, "rotate_decide_id");
                // rotate_decide_id == -1 为首轮抽取物品；>= 0 为加成轮
                bool autoDecide = (rotateDecideId == -1) ? cfg.ReelFirstAutoDecide : cfg.ReelBonusAutoDecide;

                var mstt = ReflectionHelper.GetValue<ReelManager.MSTATE>(__instance, "mstt");
                if (autoDecide)
                {
                    if (mstt == ReelManager.MSTATE.OPENING)
                    {
                        ReflectionHelper.SetValue(__instance, ReelManager.MSTATE.OPENING_AUTO, "mstt");
                    }
                    __instance.autodecide_progressable = true;
                }
                else
                {
                    if (mstt == ReelManager.MSTATE.OPENING_AUTO)
                    {
                        ReflectionHelper.SetValue(__instance, ReelManager.MSTATE.OPENING, "mstt");
                    }
                    __instance.autodecide_progressable = false;
                }
            }
        }

        // 3. 挂钩 ReelExecuter.decideRotate: 根据首轮/加成轮自动选择配置，分别锁定最优物品与最优词条
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
                var cfg = AICModConfig.Current;
                if (!cfg.EnableBestReelReward) return;

                var etype = __instance.getEType();
                bool autoDecide = (etype == ReelExecuter.ETYPE.ITEMKIND) ? cfg.ReelFirstAutoDecide : cfg.ReelBonusAutoDecide;
                if (!autoDecide) return; // 若未开启该轮次的“自动抉择”，则保留玩家纯手动卡点/随机结果

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
                    // 获取当前首盘已选中的物品信息（用于评估当前是否已达到 5 星 / 满星）
                    var reelIk = ReflectionHelper.GetValue<ReelExecuter>(ui, "ReelIK");
                    var currentIkRow = reelIk?.IKRow;
                    bool isRandomReel = etype == ReelExecuter.ETYPE.RANDOM;

                    // 词条盘：根据规则选出最优词条（若已达 5 星且出现随机盘优先翻倍）
                    int maxScore = -1;
                    for (int i = 0; i < acontent.Length; i++)
                    {
                        string s = acontent[i] ?? string.Empty;
                        int score = GetEffectScore(s, currentIkRow, isRandomReel);
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
        }

        // 4. 挂钩 ReelExecuter.applyEffectToIK: 确保结算时金币收益正常入账
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

                var ui = ReflectionHelper.GetValue<UiReelManager>(__instance, "Ui");
                if (ui != null && ui.added_money > 0)
                {
                    ui.added_money = Math.Max(ui.added_money, 1000);
                }
            }
        }

        private static int GetEffectScore(string s, NelItemEntry? ikRow, bool isRandomReel)
        {
            // 游戏中 grade 范围为 0..4，其中 4 即为第 5 档最高星级（5★满星）
            bool is5Star = ikRow != null && ikRow.grade >= 4;

            if (Enum.TryParse<ReelExecuter.EFFECT>(s, true, out var eff))
            {
                // 核心规则：如果已经 5 星，如果出现随机盘（RANDOM）优先翻倍！
                if (isRandomReel)
                {
                    if (is5Star)
                    {
                        // 已满 5 星：绝对优先数量翻倍 COUNT_MUL2
                        if (eff == ReelExecuter.EFFECT.COUNT_MUL2) return 5000;
                        // 品质已达上限，加星词条完全溢出失效
                        if (eff == ReelExecuter.EFFECT.GRADE4 || eff == ReelExecuter.EFFECT.GRADE3 ||
                            eff == ReelExecuter.EFFECT.GRADE2 || eff == ReelExecuter.EFFECT.GRADE1) return 0;
                        if (eff == ReelExecuter.EFFECT.COUNT_ADD3) return 800;
                        if (eff == ReelExecuter.EFFECT.COUNT_ADD2) return 600;
                        if (eff == ReelExecuter.EFFECT.COUNT_ADD1) return 400;
                        if (eff == ReelExecuter.EFFECT.ADD_MONEY100) return 700;
                    }
                    else
                    {
                        // 未满 5 星：随机盘优先选 GRADE3 快速拉满星级，其次翻倍/增加数量
                        if (eff == ReelExecuter.EFFECT.GRADE3) return 3000;
                        if (eff == ReelExecuter.EFFECT.GRADE2) return 2000;
                        if (eff == ReelExecuter.EFFECT.COUNT_MUL2) return 1500;
                        if (eff == ReelExecuter.EFFECT.COUNT_ADD3) return 1200;
                        if (eff == ReelExecuter.EFFECT.COUNT_ADD2) return 900;
                        if (eff == ReelExecuter.EFFECT.GRADE1) return 800;
                        if (eff == ReelExecuter.EFFECT.COUNT_ADD1) return 600;
                        if (eff == ReelExecuter.EFFECT.ADD_MONEY100) return 700;
                    }
                }

                // 常规非随机盘判定
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
                    case ReelExecuter.EFFECT.COUNT_MUL1: return 10; // 1倍等于不翻倍，必须垫底！
                    case ReelExecuter.EFFECT.COUNT_ADD0: return 5;
                    case ReelExecuter.EFFECT.GRADE0: return 5;
                    default: return 50;
                }
            }

            if (isRandomReel && is5Star && s.IndexOf("COUNT_MUL2", StringComparison.OrdinalIgnoreCase) >= 0) return 5000;
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
