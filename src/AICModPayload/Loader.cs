using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AICMod
{
    public static class Loader
    {
        private static bool _initialized;

        static Loader()
        {
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            }
            catch { }
        }

        public static int Init()
        {
            return Init("");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Init(string arg)
        {
            try
            {
                File.AppendAllText(@"C:\AliceInCradle\payload.log", "[AICMod] Loader.Init entered\n");
                if (_initialized)
                {
                    File.AppendAllText(@"C:\AliceInCradle\payload.log", "[AICMod] Already initialized\n");
                    return 0;
                }

                AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

                // 优先从自身嵌入资源中加载 0Harmony 等依赖程序集
                PreloadDependencies();

                // 隔离调用实际的 Mod 启动逻辑，防止 JIT 在解析 Loader.Init 时提前触发对 0Harmony 的校验
                return StartPayload();
            }
            catch (Exception ex)
            {
                File.AppendAllText(@"C:\AliceInCradle\payload.log", "[AICMod] Failed to initialize: " + ex + "\n");
                return -1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PreloadDependencies()
        {
            try
            {
                var curAsm = typeof(Loader).Assembly;
                foreach (var resName in curAsm.GetManifestResourceNames())
                {
                    if (resName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var s = curAsm.GetManifestResourceStream(resName);
                            if (s != null)
                            {
                                byte[] b = new byte[s.Length];
                                int read = 0;
                                while (read < b.Length)
                                {
                                    int r = s.Read(b, read, b.Length - read);
                                    if (r <= 0) break;
                                    read += r;
                                }
                                var loaded = Assembly.Load(b);
                                File.AppendAllText(@"C:\AliceInCradle\payload.log", $"[AICMod] Preloaded dependency: {loaded.FullName}\n");
                            }
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(@"C:\AliceInCradle\payload.log", $"[AICMod] Preload {resName} failed: {ex.Message}\n");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(@"C:\AliceInCradle\payload.log", "[AICMod] PreloadDependencies outer error: " + ex + "\n");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int StartPayload()
        {
            // 1. 初始化主线程控制器
            File.AppendAllText(@"C:\AliceInCradle\payload.log", "[AICMod] Initializing Controller...\n");
            AICModController.Init();

            // 2. 启动 IPC 服务端
            File.AppendAllText(@"C:\AliceInCradle\payload.log", "[AICMod] Starting IpcServer...\n");
            IpcServer.Instance.Start();

            // 3. 应用补丁
            File.AppendAllText(@"C:\AliceInCradle\payload.log", "[AICMod] Patching...\n");
            ResilientPatcher.PatchAllResilient();

            _initialized = true;
            File.AppendAllText(@"C:\AliceInCradle\payload.log", $"[AICMod] Init success! {ResilientPatcher.ActivePatches}/{ResilientPatcher.TotalPatches} patches active.\n");
            return 0;
        }

        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name ?? "";
                var curAsm = typeof(Loader).Assembly;
                foreach (var resName in curAsm.GetManifestResourceNames())
                {
                    if (resName.EndsWith(name + ".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        using var s = curAsm.GetManifestResourceStream(resName);
                        if (s != null)
                        {
                            byte[] b = new byte[s.Length];
                            int read = 0;
                            while (read < b.Length)
                            {
                                int r = s.Read(b, read, b.Length - read);
                                if (r <= 0) break;
                                read += r;
                            }
                            return Assembly.Load(b);
                        }
                    }
                }

                string asmDir = Path.GetDirectoryName(curAsm.Location) ?? "";
                string targetPath = Path.Combine(asmDir, name + ".dll");
                if (File.Exists(targetPath))
                {
                    return Assembly.LoadFrom(targetPath);
                }
            }
            catch { }
            return null;
        }

        public static int Unload()
        {
            return Unload("");
        }

        public static int Unload(string arg)
        {
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
                IpcServer.Instance.Stop();
                ResilientPatcher.UnpatchAll();
                _initialized = false;

                Debug.Log("[AICMod] Mod Payload unloaded.");
                return 0;
            }
            catch (Exception ex)
            {
                Debug.LogError("[AICMod] Failed to unload: " + ex);
                return -1;
            }
        }
    }
}
