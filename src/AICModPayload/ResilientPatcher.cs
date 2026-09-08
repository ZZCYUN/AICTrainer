using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AICMod
{
    /// <summary>
    /// 细粒度、沙箱隔离的 Harmony 容错补丁管理器
    /// 确保任何单一函数在游戏更新后签名变化时，仅自身降级，绝不影响其他补丁正常运转
    /// </summary>
    public static class ResilientPatcher
    {
        public const string HarmonyId = "com.aicedit.mod.resilient";
        private static Harmony? _harmony;

        public static int ActivePatches { get; private set; }
        public static int TotalPatches { get; private set; }
        private static readonly List<string> AppliedPatchNames = new List<string>();

        public static void PatchAllResilient()
        {
            _harmony = new Harmony(HarmonyId);
            ActivePatches = 0;
            TotalPatches = 0;
            AppliedPatchNames.Clear();

            var patchClasses = new[]
            {
                typeof(Patches.MainThreadHook),
                typeof(Patches.MosaicPatch),
                typeof(Patches.HazardPatch),
                typeof(Patches.CombatPatch),
                typeof(Patches.WorldPatch),
                typeof(Patches.DojoPatch),
                typeof(Patches.ReelPatch),
                typeof(Patches.BattleAccessPatch)
            };

            foreach (var patchClass in patchClasses)
            {
                PatchClassSafely(patchClass);
                foreach (var nested in patchClass.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    PatchClassSafely(nested);
                }
            }

            Debug.Log($"[AICMod] ResilientPatcher completed: {ActivePatches}/{TotalPatches} patches active.");
        }

        private static void PatchClassSafely(Type patchClass)
        {
            // 如果类声明了 TargetMethod 或 TargetMethods，采用 Harmony 原生 CreateClassProcessor 安全处理
            var targetMethodGetter = patchClass.GetMethod("TargetMethod", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                  ?? patchClass.GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (targetMethodGetter != null)
            {
                TotalPatches++;
                try
                {
                    var processor = _harmony!.CreateClassProcessor(patchClass);
                    processor.Patch();
                    ActivePatches++;
                    AppliedPatchNames.Add(patchClass.Name);
                    Debug.Log($"[AICMod] Successfully applied class processor patch: {patchClass.Name}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AICMod] Non-fatal patch failure on {patchClass.Name}: {ex.Message}");
                }
                return;
            }

            // 1. 检查类级别上的 HarmonyPatch 注解
            var classPatchAttrs = patchClass.GetCustomAttributes(typeof(HarmonyPatch), false);
            Type? classTargetType = null;
            string? classTargetMethod = null;
            Type[]? classTargetArgTypes = null;

            foreach (HarmonyPatch attr in classPatchAttrs)
            {
                var trav = Traverse.Create(attr);
                var targetType = trav.Field("info").Field<Type>("declaringType").Value;
                var methodName = trav.Field("info").Field<string>("methodName").Value;
                var argTypes = trav.Field("info").Field<Type[]>("argumentTypes").Value;
                if (targetType != null) classTargetType = targetType;
                if (!string.IsNullOrEmpty(methodName)) classTargetMethod = methodName;
                if (argTypes != null) classTargetArgTypes = argTypes;
            }

            // 2. 遍历方法级别的 Patch
            foreach (var method in patchClass.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var methodPatchAttrs = method.GetCustomAttributes(typeof(HarmonyPatch), false);
                var prefixAttr = method.GetCustomAttribute(typeof(HarmonyPrefix), false);
                var postfixAttr = method.GetCustomAttribute(typeof(HarmonyPostfix), false);

                if (methodPatchAttrs.Length == 0 && prefixAttr == null && postfixAttr == null)
                {
                    continue;
                }

                TotalPatches++;
                string patchDesc = $"{patchClass.Name}.{method.Name}";

                try
                {
                    Type? targetType = classTargetType;
                    string? targetMethodName = classTargetMethod;
                    Type[]? targetArgTypes = classTargetArgTypes;

                    foreach (HarmonyPatch attr in methodPatchAttrs)
                    {
                        var trav = Traverse.Create(attr);
                        var dt = trav.Field("info").Field<Type>("declaringType").Value;
                        var mn = trav.Field("info").Field<string>("methodName").Value;
                        var argTypes = trav.Field("info").Field<Type[]>("argumentTypes").Value;

                        if (dt != null) targetType = dt;
                        if (!string.IsNullOrEmpty(mn)) targetMethodName = mn;
                        if (argTypes != null) targetArgTypes = argTypes;
                    }

                    if (targetType == null || string.IsNullOrEmpty(targetMethodName))
                    {
                        // 采用 Harmony 默认反射机制尝试单方法补丁
                        var processor = _harmony!.CreateClassProcessor(patchClass);
                        processor.Patch();
                        ActivePatches++;
                        AppliedPatchNames.Add(patchDesc);
                        Debug.Log($"[AICMod] Applied standard patch: {patchDesc}");
                        break; // 类级别已整体处理
                    }

                    // 目标方法查找（支持精确查找与模糊签名查找降级）
                    MethodInfo? targetMethod = null;
                    if (targetArgTypes != null)
                    {
                        targetMethod = AccessTools.Method(targetType, targetMethodName, targetArgTypes);
                    }
                    if (targetMethod == null)
                    {
                        try
                        {
                            targetMethod = AccessTools.Method(targetType, targetMethodName);
                        }
                        catch (AmbiguousMatchException)
                        {
                            var allMethods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                            foreach (var m in allMethods)
                            {
                                if (m.Name == targetMethodName)
                                {
                                    targetMethod = m;
                                    break;
                                }
                            }
                        }
                    }
                    if (targetMethod == null)
                    {
                        targetMethod = AccessTools.PropertyGetter(targetType, targetMethodName);
                    }
                    if (targetMethod == null)
                    {
                        // 模糊匹配降级
                        targetMethod = ReflectionHelper.FindMethodFuzzy(targetType, targetMethodName);
                    }

                    if (targetMethod == null)
                    {
                        Debug.LogWarning($"[AICMod] Resilient patch skip: Method '{targetType.Name}.{targetMethodName}' not found in current game version.");
                        continue;
                    }

                    // 安全校验：若补丁声明了 __instance 参数，目标方法的 DeclaringType 必须能安全转换为该参数类型，严防 Hook 到父类导致其他子类转型异常
                    bool instanceTypeValid = true;
                    foreach (var p in method.GetParameters())
                    {
                        if (p.Name == "__instance" && targetMethod.DeclaringType != null && !p.ParameterType.IsAssignableFrom(targetMethod.DeclaringType))
                        {
                            instanceTypeValid = false;
                            Debug.LogWarning($"[AICMod] Resilient patch skip: {patchDesc} requires __instance of type {p.ParameterType.Name}, but resolved target method is declared on {targetMethod.DeclaringType.Name}");
                            break;
                        }
                    }
                    if (!instanceTypeValid) continue;

                    var harmonyMethod = new HarmonyMethod(method);
                    HarmonyMethod? prefix = (prefixAttr != null || method.Name.StartsWith("Prefix")) ? harmonyMethod : null;
                    HarmonyMethod? postfix = (postfixAttr != null || method.Name.StartsWith("Postfix")) ? harmonyMethod : null;

                    _harmony!.Patch(targetMethod, prefix, postfix);
                    ActivePatches++;
                    AppliedPatchNames.Add(patchDesc);
                    Debug.Log($"[AICMod] Successfully applied resilient patch: {patchDesc} -> {targetType.Name}.{targetMethod.Name}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AICMod] Non-fatal patch failure on {patchDesc}: {ex.Message}");
                }
            }
        }

        public static void UnpatchAll()
        {
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
                ActivePatches = 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AICMod] Error unpatching: {ex.Message}");
            }
        }
    }
}
