using System;
using System.Reflection;
using HarmonyLib;
using nel;
using Spine;
using UnityEngine;
using XX;

namespace AICMod.Patches
{
    /// <summary>
    /// 全场景无死角马赛克彻底清除补丁
    /// 涵盖：
    /// 1. 游戏立绘与全身图 (UIPicture / UIPictureBodyData / UIPictureBodySpine)
    /// 2. 动画切入演出 (AnimateCutin)
    /// 3. 处刑与特写画面 (FatalShower / FtMosaic)
    /// 4. 相机后处理绘制回调与网格生成 (MosaicShower.FnDrawMosaic / setTarget)
    /// </summary>
    public static class MosaicPatch
    {
        // 1. 相机绘制回调拦截：直接标记完成，跳过 GL 渲染
        [HarmonyPatch(typeof(MosaicShower), "FnDrawMosaic")]
        public static class Patch_MosaicShower_FnDrawMosaic
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

        // 2. 目标设定拦截：将马赛克目标置空，防止相机挂载与着色器网格初始化
        [HarmonyPatch(typeof(MosaicShower), "setTarget")]
        public static class Patch_MosaicShower_setTarget
        {
            [HarmonyPrefix]
            public static void Prefix(ref IMosaicDescriptor _Targ, MosaicShower __instance)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    _Targ = null!;
                    if (__instance != null)
                    {
                        __instance.enabled = false;
                    }
                }
            }
        }

        // 3. 立绘马赛克开关属性拦截
        [HarmonyPatch(typeof(UIPictureBase), "get_mosaic_enabled")]
        public static class Patch_UIPictureBase_get_mosaic_enabled
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UIPictureBase), "set_mosaic_enabled")]
        public static class Patch_UIPictureBase_set_mosaic_enabled
        {
            [HarmonyPrefix]
            public static void Prefix(ref bool value)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    value = false;
                }
            }
        }

        // 4. 立绘与Spine部件计数拦截：返回 0 处马赛克，使上层完全不触发网格绘制
        [HarmonyPatch(typeof(UIPictureBodyData), "countMosaic", new[] { typeof(bool) })]
        public static class Patch_UIPictureBodyData_countMosaic1
        {
            [HarmonyPrefix]
            public static bool Prefix(ref int __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = 0;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UIPictureBodyData), "countMosaic", new[] { typeof(bool), typeof(UIPictureBase.EMSTATE) })]
        public static class Patch_UIPictureBodyData_countMosaic2
        {
            [HarmonyPrefix]
            public static bool Prefix(ref int __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = 0;
                    return false;
                }
                return true;
            }
        }

        // 5. 立绘部位敏感区/马赛克矩形矩阵获取拦截
        [HarmonyPatch(typeof(UIPictureBodyData), "getSensitiveOrMosaicRect")]
        public static class Patch_UIPictureBodyData_getSensitiveOrMosaicRect
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // 6. Spine 骨骼附加马赛克获取拦截
        [HarmonyPatch(typeof(UIPictureBodySpine), "getSensitiveOrMosaicRect", new[] { typeof(Matrix4x4), typeof(int), typeof(MeshAttachment), typeof(Slot) }, new[] { ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref })]
        public static class Patch_UIPictureBodySpine_getSensitiveOrMosaicRect1
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UIPictureBodySpine), "getSensitiveOrMosaicRect", new[] { typeof(Matrix4x4), typeof(UIPictureBase.EMSTATE), typeof(int), typeof(MeshAttachment), typeof(Slot), typeof(SpineViewer) }, new[] { ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Normal })]
        public static class Patch_UIPictureBodySpine_getSensitiveOrMosaicRect2
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // 7. 特写与动画切入 (AnimateCutin) 马赛克拦截
        [HarmonyPatch(typeof(AnimateCutin), "countMosaic")]
        public static class Patch_AnimateCutin_countMosaic
        {
            [HarmonyPrefix]
            public static bool Prefix(ref int __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = 0;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(AnimateCutin), "getSensitiveOrMosaicRect")]
        public static class Patch_AnimateCutin_getSensitiveOrMosaicRect
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // 8. 败北特写/处刑动画 (FtMosaic) 马赛克拦截 (由于 FtMosaic 是 internal 类，采用 TargetMethod 动态解析)
        public static class Patch_FtMosaic_countMosaic
        {
            public static MethodBase? TargetMethod()
            {
                var type = AccessTools.TypeByName("nel.fatal.FtMosaic");
                return type != null ? AccessTools.Method(type, "countMosaic") : null;
            }

            [HarmonyPrefix]
            public static bool Prefix(ref int __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = 0;
                    return false;
                }
                return true;
            }
        }

        public static class Patch_FtMosaic_getSensitiveOrMosaicRect
        {
            public static MethodBase? TargetMethod()
            {
                var type = AccessTools.TypeByName("nel.fatal.FtMosaic");
                return type != null ? AccessTools.Method(type, "getSensitiveOrMosaicRect") : null;
            }

            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (AICModConfig.Current.DisableMosaic)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        public static class Patch_FtMosaic_initMosaic
        {
            public static MethodBase? TargetMethod()
            {
                var type = AccessTools.TypeByName("nel.fatal.FtMosaic");
                return type != null ? AccessTools.Method(type, "initMosaic") : null;
            }

            [HarmonyPrefix]
            public static void Prefix(object __instance)
            {
                if (AICModConfig.Current.DisableMosaic && __instance is MosaicShower ms)
                {
                    ms.enabled = false;
                }
            }
        }
    }
}
