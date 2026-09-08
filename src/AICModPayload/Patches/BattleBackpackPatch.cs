using HarmonyLib;
using nel;

namespace AICMod.Patches
{
    public static class BattleBackpackPatch
    {
        // 战斗地图内访问背包：强制 can_open_gamemenu 返回 true，
        // 使战斗中被锁定的游戏菜单（含背包）也能打开
        [HarmonyPatch(typeof(NelM2DBase), "can_open_gamemenu", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Prefix_can_open_gamemenu(ref bool __result)
        {
            if (AICModConfig.Current.AllowBattleBackpack)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
