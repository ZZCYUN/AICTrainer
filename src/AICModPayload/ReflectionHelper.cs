using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;

namespace AICMod
{
    /// <summary>
    /// 应对游戏更新导致的字段重命名、属性化或签名微调的容错反射工具类
    /// </summary>
    public static class ReflectionHelper
    {
        private const BindingFlags AllFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        // 缓存已解析的字段与属性，避免每帧重复反射带来性能损耗
        private static readonly ConcurrentDictionary<string, FieldInfo?> FieldCache = new ConcurrentDictionary<string, FieldInfo?>();
        private static readonly ConcurrentDictionary<string, PropertyInfo?> PropertyCache = new ConcurrentDictionary<string, PropertyInfo?>();
        private static readonly ConcurrentDictionary<string, MethodInfo?> MethodCache = new ConcurrentDictionary<string, MethodInfo?>();

        /// <summary>
        /// 尝试从多个备选别名中获取字段信息
        /// </summary>
        public static FieldInfo? FindField(Type type, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                string key = $"{type.FullName}::f::{name}";
                if (FieldCache.TryGetValue(key, out var cached))
                {
                    if (cached != null) return cached;
                    continue;
                }

                var f = type.GetField(name, AllFlags);
                if (f != null)
                {
                    FieldCache[key] = f;
                    return f;
                }
            }

            // 若完全匹配未找到，尝试大小写不敏感或包含匹配
            foreach (var f in type.GetFields(AllFlags))
            {
                foreach (var name in candidateNames)
                {
                    if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        string key = $"{type.FullName}::f::{candidateNames[0]}";
                        FieldCache[key] = f;
                        return f;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试从多个备选别名中获取属性信息
        /// </summary>
        public static PropertyInfo? FindProperty(Type type, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                string key = $"{type.FullName}::p::{name}";
                if (PropertyCache.TryGetValue(key, out var cached))
                {
                    if (cached != null) return cached;
                    continue;
                }

                var p = type.GetProperty(name, AllFlags);
                if (p != null)
                {
                    PropertyCache[key] = p;
                    return p;
                }
            }

            foreach (var p in type.GetProperties(AllFlags))
            {
                foreach (var name in candidateNames)
                {
                    if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        string key = $"{type.FullName}::p::{candidateNames[0]}";
                        PropertyCache[key] = p;
                        return p;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 容错获取字段或属性值（支持字段被重构为属性，或命名更改）
        /// </summary>
        public static T GetValue<T>(object target, params string[] candidateNames)
        {
            if (target == null) return default!;
            Type type = target is Type t ? t : target.GetType();
            object? instance = target is Type ? null : target;

            var f = FindField(type, candidateNames);
            if (f != null)
            {
                object? val = f.GetValue(instance);
                if (val is T casted) return casted;
                if (val != null)
                {
                    try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
                }
            }

            var p = FindProperty(type, candidateNames);
            if (p != null)
            {
                object? val = p.GetValue(instance);
                if (val is T casted) return casted;
                if (val != null)
                {
                    try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
                }
            }

            return default!;
        }

        /// <summary>
        /// 容错设置字段或属性值
        /// </summary>
        public static bool SetValue(object target, object? value, params string[] candidateNames)
        {
            if (target == null) return false;
            Type type = target is Type t ? t : target.GetType();
            object? instance = target is Type ? null : target;

            var f = FindField(type, candidateNames);
            if (f != null)
            {
                try
                {
                    object? converted = (value != null && f.FieldType != value.GetType())
                        ? Convert.ChangeType(value, f.FieldType)
                        : value;
                    f.SetValue(instance, converted);
                    return true;
                }
                catch { }
            }

            var p = FindProperty(type, candidateNames);
            if (p != null && p.CanWrite)
            {
                try
                {
                    object? converted = (value != null && p.PropertyType != value.GetType())
                        ? Convert.ChangeType(value, p.PropertyType)
                        : value;
                    p.SetValue(instance, converted);
                    return true;
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// 模糊查找方法（用于补丁定位，哪怕参数有增减或微调）
        /// </summary>
        public static MethodInfo? FindMethodFuzzy(Type type, string methodName, int preferredParamCount = -1)
        {
            string cacheKey = $"{type.FullName}::m::{methodName}::{preferredParamCount}";
            if (MethodCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            MethodInfo? bestMatch = null;
            int bestParamDiff = int.MaxValue;

            // 1. 优先只在目标类自身声明的方法中查找（DeclaredOnly），杜绝意外匹配并 Hook 到父类全局方法（如 M2Attackable）
            var flagsDeclared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var m in type.GetMethods(flagsDeclared))
            {
                if (m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                {
                    var pars = m.GetParameters();
                    if (preferredParamCount < 0)
                    {
                        MethodCache[cacheKey] = m;
                        return m;
                    }

                    int diff = Math.Abs(pars.Length - preferredParamCount);
                    if (diff < bestParamDiff)
                    {
                        bestParamDiff = diff;
                        bestMatch = m;
                    }
                }
            }

            if (bestMatch != null)
            {
                MethodCache[cacheKey] = bestMatch;
                return bestMatch;
            }

            // 2. 若自身未声明，再降级查找继承链
            foreach (var m in type.GetMethods(AllFlags))
            {
                if (m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                {
                    var pars = m.GetParameters();
                    if (preferredParamCount < 0)
                    {
                        MethodCache[cacheKey] = m;
                        return m;
                    }

                    int diff = Math.Abs(pars.Length - preferredParamCount);
                    if (diff < bestParamDiff)
                    {
                        bestParamDiff = diff;
                        bestMatch = m;
                    }
                }
            }

            if (bestMatch != null)
            {
                MethodCache[cacheKey] = bestMatch;
            }

            return bestMatch;
        }
    }
}
