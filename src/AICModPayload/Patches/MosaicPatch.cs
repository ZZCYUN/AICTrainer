using HarmonyLib;
using nel;

namespace AICMod.Patches
{
    [HarmonyPatch(typeof(MosaicShower), "FnDrawMosaic")]
    public static class MosaicPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (AICModConfig.Current.DisableMosaic)
            {
                __result = true; // 告知上层已完成绘制，从而跳过实际马赛克着色器与网格渲染
                return false;
            }
            return true;
        }
    }
}
