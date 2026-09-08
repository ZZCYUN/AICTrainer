using System;
using HarmonyLib;
using m2d;
using nel;
using nel.gm;
using XX;

namespace AICMod.Patches
{
    public static class WorldPatch
    {
        private static NelChipBench? _dummyBench;
        public static NelChipBench DummyBench
        {
            get
            {
                if (_dummyBench == null)
                {
                    _dummyBench = (NelChipBench)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(NelChipBench));
                }
                return _dummyBench;
            }
        }

        // 1. 全天可快速移动 (打破夜间战斗、恶劣天气与剧情封锁)
        [HarmonyPatch(typeof(NelM2DBase), "cantFastTravel")]
        [HarmonyPrefix]
        public static bool Prefix_cantFastTravel(ref string? __result)
        {
            if (AICModConfig.Current.FastTravelAnytime || AICModConfig.Current.FastTravelAnywhere)
            {
                __result = null; // null 表示无阻碍，允许传送
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(SCN), "isBenchCmdEnable")]
        [HarmonyPrefix]
        public static bool Prefix_isBenchCmdEnable(string key, ref bool __result)
        {
            if ((AICModConfig.Current.FastTravelAnytime || AICModConfig.Current.FastTravelAnywhere) &&
                (key == "fast_travel" || key == "fast_travel_home"))
            {
                __result = true;
                return false;
            }
            return true;
        }

        // 2. 随地可快速移动 (无需寻找长椅，大地图界面即可快速传送至任意探索点)
        [HarmonyPatch(typeof(UiGameMenu), "activate")]
        [HarmonyPostfix]
        public static void Postfix_UiGameMenu_activate(UiGameMenu __instance)
        {
            if (AICModConfig.Current.FastTravelAnywhere)
            {
                Traverse.Create(__instance).Field("can_use_fasttravel").SetValue(Map2d.can_handle);
            }
        }

        [HarmonyPatch]
        public static class UiGMCMapCanUseFastTravelPatch
        {
            public static System.Reflection.MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("nel.gm.UiGMCMap"), "get_can_use_fasttravel");
            }

            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (AICModConfig.Current.FastTravelAnywhere)
                {
                    __result = Map2d.can_handle;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch]
        public static class UiGMCMapBenchChipPatch
        {
            public static System.Reflection.MethodBase TargetMethod()
            {
                return AccessTools.PropertyGetter(AccessTools.TypeByName("nel.gm.UiGMCMap"), "BenchChip");
            }

            [HarmonyPostfix]
            public static void Postfix(object __instance, ref NelChipBench __result)
            {
                if (AICModConfig.Current.FastTravelAnywhere && __result == null)
                {
                    __result = DummyBench;
                }
            }
        }

        [HarmonyPatch]
        public static class UiGMCMapFnClickFastTravelCmdPatch
        {
            public static System.Reflection.MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("nel.gm.UiGMCMap"), "fnClickFastTravelCmd");
            }

            [HarmonyPostfix]
            public static void Postfix(object __instance, aBtn B)
            {
                if (AICModConfig.Current.FastTravelAnywhere && B != null && B.title != "&&Cancel")
                {
                    try
                    {
                        Traverse.Create(__instance).Method("quitCmd", false).GetValue();
                        var gm = Traverse.Create(__instance).Field<UiGameMenu>("GM").Value;
                        gm?.deactivate(false);
                    }
                    catch { }
                }
            }
        }

        [HarmonyPatch(typeof(ButtonSkinWholeMapArea), "set_fast_travel_active")]
        [HarmonyPostfix]
        public static void Postfix_set_fast_travel_active(ButtonSkinWholeMapArea __instance, bool value)
        {
            if (AICModConfig.Current.FastTravelAnywhere && value)
            {
                var trav = Traverse.Create(__instance);
                if (trav.Field("ABenchList").GetValue() == null)
                {
                    var wm = trav.Field<WholeMapItem>("WM").Value;
                    if (wm != null)
                    {
                        trav.Field("ABenchList").SetValue(wm.getNoticedIconList((WMIcon.TYPE)2));
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ButtonSkinWholeMapArea), "setWholeMapTarget", new[] { typeof(WholeMapItem), typeof(float), typeof(float) })]
        [HarmonyPostfix]
        public static void Postfix_setWholeMapTarget(ButtonSkinWholeMapArea __instance)
        {
            if (AICModConfig.Current.FastTravelAnywhere && __instance.fast_travel_active)
            {
                var trav = Traverse.Create(__instance);
                var wm = trav.Field<WholeMapItem>("WM").Value;
                if (wm != null)
                {
                    trav.Field("ABenchList").SetValue(wm.getNoticedIconList((WMIcon.TYPE)2));
                }
            }
        }

        [HarmonyPatch(typeof(WholeMapItem), "free_travel_available", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Prefix_free_travel_available(ref bool __result)
        {
            if (AICModConfig.Current.FastTravelAnywhere)
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(WholeMapItem), "free_travel_enable", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Prefix_free_travel_enable(ref bool __result)
        {
            if (AICModConfig.Current.FastTravelAnywhere)
            {
                __result = true;
                return false;
            }
            return true;
        }

        // 3. 始终空腹加成 (进食始终获得 1.5 倍加成)
        [HarmonyPatch(typeof(Stomach), "applyableDoubleBonus")]
        [HarmonyPrefix]
        public static bool Prefix_applyableDoubleBonus(ref bool __result)
        {
            if (AICModConfig.Current.AlwaysHungryBonus)
            {
                __result = true;
                return false;
            }
            return true;
        }

        // 4. 突破食物属性上限 (取消原版的 1.0f 钳位)
        [HarmonyPatch(typeof(M2PrSkill), "addRELevel")]
        [HarmonyPrefix]
        public static bool Prefix_addRELevel(M2PrSkill __instance, RCP.RPI_EFFECT effect, float val, ref M2PrSkill __result)
        {
            if (AICModConfig.Current.BreakFoodAttrLimit)
            {
                var dict = Traverse.Create(__instance).Field<System.Collections.Generic.Dictionary<RCP.RPI_EFFECT, float>>("Oeffect01").Value;
                if (dict != null)
                {
                    dict.TryGetValue(effect, out float cur);
                    dict[effect] = cur + val; // 不限制 1.0f 上限
                }
                __result = __instance;
                return false;
            }
            return true;
        }

        // 5. 启用随意保存
        [HarmonyPatch(typeof(SCN), "canSave")]
        [HarmonyPrefix]
        public static bool Prefix_SCN_canSave(ref bool __result)
        {
            if (AICModConfig.Current.SaveAnywhere)
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(PR), "canSave")]
        [HarmonyPrefix]
        public static bool Prefix_PR_canSave(ref bool __result)
        {
            if (AICModConfig.Current.SaveAnywhere)
            {
                __result = true;
                return false;
            }
            return true;
        }

        // 6. 启用锁定倒计时 (冻结 Gameover 倒计时)
        [HarmonyPatch(typeof(UiGOContinuer), "run")]
        [HarmonyPrefix]
        public static void Prefix_UiGOContinuer_run(UiGOContinuer __instance, ref float fcnt)
        {
            if (AICModConfig.Current.FreezeCountdown)
            {
                fcnt = 0f;
                Traverse.Create(__instance).Field("t_count").SetValue(0f);
            }
        }

        // 7. 抑制未知大地图区域异常（防止进入 mine、labo、debug 等特殊地图时抛出不明なWARecord导致调试器异常中断）
        [HarmonyPatch(typeof(WAManager), "GetWa", new[] { typeof(string), typeof(bool) })]
        [HarmonyPrefix]
        public static void Prefix_WAManager_GetWa(ref bool no_error)
        {
            no_error = true;
        }

        // 8. 修复无 WholeMap 图层地图的元数据检查（防止进入无全景图层元数据的地图时抛出 WM レイヤーを指定しない場合 异常）
        [HarmonyPatch(typeof(WholeMapItem), "initS", new[] { typeof(Map2d), typeof(WholeMapItem.WMRegex) })]
        [HarmonyPrefix]
        public static bool Prefix_WholeMapItem_initS(WholeMapItem __instance, Map2d Mp, WholeMapItem.WMRegex MatchWr)
        {
            if (Mp == null) return true;
            if (__instance.GetWmi(Mp) == null)
            {
                string? s = Mp.Meta != null ? Mp.Meta.GetS("wholemap_pos") : null;
                if (TX.noe(s))
                {
                    __instance.CurMap = Mp;
                    __instance.CurPosition = default;
                    return false;
                }
            }
            return true;
        }

        // 9. 传送后清除多余的移动脚本（防止自动走入墙壁或触发“移動先が通行できないため”错误）
        [HarmonyPatch(typeof(M2LpMapTransferBase), "executeTransferFastTravel", new[] { typeof(Map2d), typeof(int), typeof(int), typeof(int) })]
        [HarmonyPostfix]
        public static void Postfix_executeTransferFastTravel(Map2d DepMp)
        {
            try
            {
                var keyPr = DepMp?.getKeyPr();
                if (keyPr != null)
                {
                    keyPr.quitMoveScript();
                    keyPr.getPhysic()?.killSpeedForce();
                }
            }
            catch { }
        }
    }
}
