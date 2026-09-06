using System;
using HarmonyLib;
using nel.mgm.dojo;
using UnityEngine;

namespace AICMod.Patches
{
    /// <summary>
    /// 武馆小游戏必胜补丁 (DjGM / DjLife / DjRPC)
    /// 保证任意出拳均判定为克制正解、节拍完美无惩罚，漏拍自动补拳，主角全免伤害必定通关
    /// </summary>
    public static class DojoPatch
    {
        private static int GetWinningRpc(DjGM gm)
        {
            try
            {
                var trav = Traverse.Create(gm);
                int sttVal = Convert.ToInt32(trav.Field("stt").GetValue());
                if (sttVal == 9 || sttVal == 15) return 0; // TUTO_PLAY0, TUTO_PLAY3 (石头)
                if (sttVal == 11) return 1; // TUTO_PLAY1 (剪刀)
                if (sttVal == 13) return 2; // TUTO_PLAY2 (布)

                int enemyHand = gm.HK != null ? gm.HK.cur_hand : 0;
                return enemyHand switch
                {
                    0 => 2, // 对手出石头 -> 玩家出布克制
                    1 => 0, // 对手出剪刀 -> 玩家出石头克制
                    2 => 1, // 对手出布 -> 玩家出剪刀克制
                    _ => 0
                };
            }
            catch
            {
                return 0;
            }
        }

        // 1. 拦截玩家出拳判定 (hitHand): 纠正手势为克制必胜手势，并将判定时间设为完美 0f
        [HarmonyPatch(typeof(DjGM), "hitHand")]
        [HarmonyPrefix]
        public static void Prefix_hitHand(DjGM __instance, ref int rpc)
        {
            if (!AICModConfig.Current.DojoAlwaysWin) return;

            rpc = GetWinningRpc(__instance);
            Traverse.Create(__instance).Field("t_input_alloc").SetValue(0f);
        }

        // 2. 后置消除失败标志位，强制确保置位 HAND__WIN (256)
        [HarmonyPatch(typeof(DjGM), "hitHand")]
        [HarmonyPostfix]
        public static void Postfix_hitHand(DjGM __instance)
        {
            if (!AICModConfig.Current.DojoAlwaysWin) return;

            var trav = Traverse.Create(__instance);
            uint bits = trav.Field<uint>("hand_type_bits").Value;
            // 清理 SLOW(16) | FAST(32) | WRONG(64) | LOSE(512) | IGNORED(1024)
            bits &= ~1648u;
            bits |= 256u; // HAND__WIN
            trav.Field<uint>("hand_type_bits").Value = bits;
        }

        // 3. 拦截节拍结算 (BeatAttacked): 若玩家漏拍或未输入，前置自动打出克制必胜拳
        [HarmonyPatch(typeof(DjGM), "BeatAttacked")]
        [HarmonyPrefix]
        public static void Prefix_BeatAttacked(DjGM __instance)
        {
            if (!AICModConfig.Current.DojoAlwaysWin) return;
            if (!__instance.isActive()) return;

            var trav = Traverse.Create(__instance);
            uint bits = trav.Field<uint>("hand_type_bits").Value;

            // 0x800 为结算拍 (HAND__NEXT_CALC)，若此时未成功置位 HAND__WIN (256)
            if ((bits & 0x800u) != 0 && (bits & 0x100u) == 0)
            {
                int winningRpc = GetWinningRpc(__instance);
                AccessTools.Method(typeof(DjGM), "hitHand", new[] { typeof(int) })?.Invoke(__instance, new object[] { winningRpc });
            }
        }

        // 4. 彻底阻止进入失败特写演出与失败状态 (changeCutinLoseB)
        [HarmonyPatch(typeof(DjGM), "changeCutinLoseB")]
        [HarmonyPrefix]
        public static bool Prefix_changeCutinLoseB(ref bool __result, ref DjCutin.CUTIN_TYPE type)
        {
            if (AICModConfig.Current.DojoAlwaysWin)
            {
                type = DjCutin.CUTIN_TYPE.OFFLINE;
                __result = false;
                return false;
            }
            return true;
        }

        // 5. 主角在武馆内免疫一切裂痕与破损 (DjLife.addCrack)
        [HarmonyPatch(typeof(DjLife), "addCrack")]
        [HarmonyPrefix]
        public static bool Prefix_addCrack(DjLife __instance, ref bool __result)
        {
            if (AICModConfig.Current.DojoAlwaysWin && __instance.is_pr)
            {
                __result = false;
                return false;
            }
            return true;
        }

        // 6. 主角在武馆内免疫扣除生命心 (DjLife.breakOne)
        [HarmonyPatch(typeof(DjLife), "breakOne")]
        [HarmonyPrefix]
        public static bool Prefix_breakOne(DjLife __instance)
        {
            if (AICModConfig.Current.DojoAlwaysWin && __instance.is_pr)
            {
                return false;
            }
            return true;
        }
    }
}
