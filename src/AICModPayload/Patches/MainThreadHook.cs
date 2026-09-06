using System;
using HarmonyLib;
using nel;
using UnityEngine;

namespace AICMod.Patches
{
    /// <summary>
    /// 挂钩 NelM2DBase.run，在游戏主逻辑循环中驱动状态锁定与 IPC 数据同步
    /// NelM2DBase 为纯 C# 托管类（非 MonoBehaviour），完全规避 Unity 原生 MonoBehaviour.Update/FixedUpdate JIT 桩代码冲突
    /// </summary>
    [HarmonyPatch(typeof(NelM2DBase), "run", new[] { typeof(float) })]
    public static class MainThreadHook
    {
        [HarmonyPrefix]
        public static void Prefix(NelM2DBase __instance, float fcnt)
        {
            try
            {
                AICModController.MainThreadTick();
            }
            catch { }
        }
    }
}
