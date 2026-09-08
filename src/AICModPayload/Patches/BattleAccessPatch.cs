using HarmonyLib;
using nel;

namespace AICMod.Patches
{
    public static class BattleAccessPatch
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

        // 战斗地图内访问仓库：强制 canAccesableToHouseInventory 返回 true，
        // 使非安全区域（战斗地图等）也能打开屋内仓库
        [HarmonyPatch(typeof(NelM2DBase), "canAccesableToHouseInventory", MethodType.Normal)]
        [HarmonyPrefix]
        public static bool Prefix_canAccesableToHouseInventory(ref bool __result)
        {
            if (AICModConfig.Current.AllowBattleWarehouse)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
