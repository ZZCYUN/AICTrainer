using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AICTrainer.Injector
{
    public static class MonoInjector
    {
        #region Win32 Native APIs
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpBaseAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibraryExA(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint DONT_RESOLVE_DLL_REFERENCES = 0x00000001;
        private const uint INFINITE = 0xFFFFFFFF;
        #endregion

        public static bool Inject(int processId, string assemblyPath, string namespaceName, string className, string methodName, out string errorMessage)
        {
            errorMessage = string.Empty;
            IntPtr hProcess = IntPtr.Zero;
            IntPtr remoteAlloc = IntPtr.Zero;
            uint allocSize = 0;

            try
            {
                if (!File.Exists(assemblyPath))
                {
                    errorMessage = $"载荷程序集不存在: {assemblyPath}";
                    return false;
                }

                var proc = Process.GetProcessById(processId);
                IntPtr remoteMonoBase = IntPtr.Zero;
                string? monoDllPath = null;

                foreach (ProcessModule mod in proc.Modules)
                {
                    if (mod.ModuleName.Equals("mono-2.0-bdwgc.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        remoteMonoBase = mod.BaseAddress;
                        monoDllPath = mod.FileName;
                        break;
                    }
                }

                if (remoteMonoBase == IntPtr.Zero || string.IsNullOrEmpty(monoDllPath))
                {
                    errorMessage = "目标进程未加载 mono-2.0-bdwgc.dll (可能游戏尚未完全就绪或非 Mono 运行时)";
                    return false;
                }

                // 2. 本地加载目标 Mono DLL 解析导出函数相对地址 (RVA)
                IntPtr localMono = LoadLibraryExA(monoDllPath, IntPtr.Zero, DONT_RESOLVE_DLL_REFERENCES);
                if (localMono == IntPtr.Zero)
                {
                    errorMessage = $"无法加载本地 Mono 库: {monoDllPath}";
                    return false;
                }

                IntPtr GetRemoteFunc(string name)
                {
                    IntPtr localAddr = GetProcAddress(localMono, name);
                    if (localAddr == IntPtr.Zero)
                    {
                        throw new Exception($"未找到 Mono 导出函数: {name}");
                    }
                    long rva = localAddr.ToInt64() - localMono.ToInt64();
                    return new IntPtr(remoteMonoBase.ToInt64() + rva);
                }

                IntPtr fn_mono_get_root_domain = GetRemoteFunc("mono_get_root_domain");
                IntPtr fn_mono_thread_attach = GetRemoteFunc("mono_thread_attach");
                IntPtr fn_mono_domain_assembly_open = GetRemoteFunc("mono_domain_assembly_open");
                IntPtr fn_mono_assembly_get_image = GetRemoteFunc("mono_assembly_get_image");
                IntPtr fn_mono_class_from_name = GetRemoteFunc("mono_class_from_name");
                IntPtr fn_mono_class_get_method_from_name = GetRemoteFunc("mono_class_get_method_from_name");
                IntPtr fn_mono_runtime_invoke = GetRemoteFunc("mono_runtime_invoke");

                FreeLibrary(localMono);

                // 3. 构造数据区与 Shellcode
                // 内存布局:
                // [0x00~0x37]: 函数指针 (7 * 8 = 56 bytes)
                // [0x38...]: 字符串数据 (UTF-8 / ANSI)
                // [...]: Shellcode 执行代码
                var memoryStream = new MemoryStream();
                using (var writer = new BinaryWriter(memoryStream))
                {
                    // 写入函数指针
                    writer.Write(fn_mono_get_root_domain.ToInt64());
                    writer.Write(fn_mono_thread_attach.ToInt64());
                    writer.Write(fn_mono_domain_assembly_open.ToInt64());
                    writer.Write(fn_mono_assembly_get_image.ToInt64());
                    writer.Write(fn_mono_class_from_name.ToInt64());
                    writer.Write(fn_mono_class_get_method_from_name.ToInt64());
                    writer.Write(fn_mono_runtime_invoke.ToInt64());

                    // 写入字符串并记录偏移
                    int offsetPath = (int)writer.BaseStream.Position;
                    writer.Write(Encoding.UTF8.GetBytes(assemblyPath));
                    writer.Write((byte)0);

                    int offsetNs = (int)writer.BaseStream.Position;
                    writer.Write(Encoding.UTF8.GetBytes(namespaceName));
                    writer.Write((byte)0);

                    int offsetCls = (int)writer.BaseStream.Position;
                    writer.Write(Encoding.UTF8.GetBytes(className));
                    writer.Write((byte)0);

                    int offsetMethod = (int)writer.BaseStream.Position;
                    writer.Write(Encoding.UTF8.GetBytes(methodName));
                    writer.Write((byte)0);

                    // 8字节对齐
                    while (writer.BaseStream.Position % 8 != 0)
                    {
                        writer.Write((byte)0x90);
                    }

                    int shellcodeOffset = (int)writer.BaseStream.Position;

                    // 4. 组装 x64 Shellcode
                    var sc = new List<byte>
                    {
                        0x53,                                     // push rbx
                        0x41, 0x54,                               // push r12
                        0x41, 0x55,                               // push r13
                        0x41, 0x56,                               // push r14
                        0x48, 0x83, 0xEC, 0x28,                   // sub rsp, 28h

                        0x48, 0x89, 0xCB                          // mov rbx, rcx (rbx = base of args)
                    };

                    var jzOffsets = new List<int>();
                    void EmitJzExitErr()
                    {
                        sc.Add(0x74); // jz rel8
                        jzOffsets.Add(sc.Count);
                        sc.Add(0x00); // placeholder
                    }

                    // 1. domain = mono_get_root_domain()
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x00 }); // call [rbx + 0x00]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 }); // test rax, rax
                    EmitJzExitErr();
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC4 }); // mov r12, rax (r12 = domain)

                    // 2. mono_thread_attach(domain)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xE1 }); // mov rcx, r12
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x08 }); // call [rbx + 0x08]

                    // 3. assembly = mono_domain_assembly_open(domain, path)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xE1 }); // mov rcx, r12
                    sc.AddRange(new byte[] { 0x48, 0x8D, 0x93 }); // lea rdx, [rbx + offsetPath]
                    sc.AddRange(BitConverter.GetBytes(offsetPath));
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x10 }); // call [rbx + 0x10]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 }); // test rax, rax
                    EmitJzExitErr();
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC5 }); // mov r13, rax (r13 = assembly)

                    // 4. image = mono_assembly_get_image(assembly)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xE9 }); // mov rcx, r13
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x18 }); // call [rbx + 0x18]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 }); // test rax, rax
                    EmitJzExitErr();
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC6 }); // mov r14, rax (r14 = image)

                    // 5. klass = mono_class_from_name(image, namespace, class)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xF1 }); // mov rcx, r14
                    sc.AddRange(new byte[] { 0x48, 0x8D, 0x93 }); // lea rdx, [rbx + offsetNs]
                    sc.AddRange(BitConverter.GetBytes(offsetNs));
                    sc.AddRange(new byte[] { 0x4C, 0x8D, 0x83 }); // lea r8, [rbx + offsetCls]
                    sc.AddRange(BitConverter.GetBytes(offsetCls));
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x20 }); // call [rbx + 0x20]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 }); // test rax, rax
                    EmitJzExitErr();
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC6 }); // mov r14, rax (r14 = klass)

                    // 6. method = mono_class_get_method_from_name(klass, methodName, 0)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xF1 }); // mov rcx, r14
                    sc.AddRange(new byte[] { 0x48, 0x8D, 0x93 }); // lea rdx, [rbx + offsetMethod]
                    sc.AddRange(BitConverter.GetBytes(offsetMethod));
                    sc.AddRange(new byte[] { 0x45, 0x31, 0xC0 }); // xor r8d, r8d (param_count = 0)
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x28 }); // call [rbx + 0x28]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 }); // test rax, rax
                    EmitJzExitErr();
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC6 }); // mov r14, rax (r14 = method)

                    // 7. mono_runtime_invoke(method, NULL, NULL, NULL)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xF1 }); // mov rcx, r14
                    sc.AddRange(new byte[] { 0x31, 0xD2 });       // xor edx, edx
                    sc.AddRange(new byte[] { 0x45, 0x31, 0xC0 }); // xor r8d, r8d
                    sc.AddRange(new byte[] { 0x45, 0x31, 0xC9 }); // xor r9d, r9d
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x30 }); // call [rbx + 0x30]

                    // success
                    sc.AddRange(new byte[] { 0x31, 0xC0 });       // xor eax, eax
                    sc.Add(0xEB);                                 // jmp rel8
                    int jmpSuccessOffset = sc.Count;
                    sc.Add(0x00);                                 // placeholder

                    // exit_err:
                    int exitErrOffset = sc.Count;
                    sc.AddRange(new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00 }); // mov eax, 1

                    // finish:
                    int finishOffset = sc.Count;
                    sc.AddRange(new byte[]
                    {
                        0x48, 0x83, 0xC4, 0x28,                   // add rsp, 28h
                        0x41, 0x5E,                               // pop r14
                        0x41, 0x5D,                               // pop r13
                        0x41, 0x5C,                               // pop r12
                        0x5B,                                     // pop rbx
                        0xC3                                      // ret
                    });

                    // 回填跳转偏移
                    foreach (int pos in jzOffsets)
                    {
                        sc[pos] = (byte)(exitErrOffset - (pos + 1));
                    }
                    sc[jmpSuccessOffset] = (byte)(finishOffset - (jmpSuccessOffset + 1));

                    writer.Write(sc.ToArray());
                }

                byte[] buffer = memoryStream.ToArray();
                allocSize = (uint)buffer.Length;

                // 5. 写入远程目标进程并启动远程线程
                hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
                if (hProcess == IntPtr.Zero)
                {
                    errorMessage = $"打开游戏进程失败 (PID: {processId}, 错误码: {Marshal.GetLastWin32Error()})";
                    return false;
                }

                remoteAlloc = VirtualAllocEx(hProcess, IntPtr.Zero, allocSize, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                if (remoteAlloc == IntPtr.Zero)
                {
                    errorMessage = $"远程分配内存失败 (错误码: {Marshal.GetLastWin32Error()})";
                    return false;
                }

                if (!WriteProcessMemory(hProcess, remoteAlloc, buffer, allocSize, out _))
                {
                    errorMessage = $"向远程内存写入注入数据失败 (错误码: {Marshal.GetLastWin32Error()})";
                    return false;
                }

                // Shellcode 位于数据区之后
                // 查找 shellcodeOffset: 指针(56) + 字符串长度 + 对齐
                int offsetPathCheck = 56;
                int offsetNsCheck = offsetPathCheck + Encoding.UTF8.GetByteCount(assemblyPath) + 1;
                int offsetClsCheck = offsetNsCheck + Encoding.UTF8.GetByteCount(namespaceName) + 1;
                int offsetMethodCheck = offsetClsCheck + Encoding.UTF8.GetByteCount(className) + 1;
                int endStr = offsetMethodCheck + Encoding.UTF8.GetByteCount(methodName) + 1;
                while (endStr % 8 != 0) endStr++;
                int actualShellcodeOffset = endStr;

                IntPtr startAddress = new IntPtr(remoteAlloc.ToInt64() + actualShellcodeOffset);

                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, startAddress, remoteAlloc, 0, out _);
                if (hThread == IntPtr.Zero)
                {
                    errorMessage = $"创建远程执行线程失败 (错误码: {Marshal.GetLastWin32Error()})";
                    return false;
                }

                WaitForSingleObject(hThread, 10000); // 10秒超时
                GetExitCodeThread(hThread, out uint exitCode);
                CloseHandle(hThread);

                if (exitCode != 0)
                {
                    errorMessage = $"远程线程执行返回失败代码: {exitCode}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "注入异常: " + ex.Message;
                return false;
            }
            finally
            {
                if (remoteAlloc != IntPtr.Zero && hProcess != IntPtr.Zero)
                {
                    VirtualFreeEx(hProcess, remoteAlloc, 0, MEM_RELEASE);
                }
                if (hProcess != IntPtr.Zero)
                {
                    CloseHandle(hProcess);
                }
            }
        }

        public static bool InjectFromMemory(
            int processId,
            byte[] rawAssemblyBytes,
            string assemblyName,
            string namespaceName,
            string className,
            string methodName,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            IntPtr hProcess = IntPtr.Zero;
            IntPtr remoteAlloc = IntPtr.Zero;
            uint allocSize = 0;

            try
            {
                if (rawAssemblyBytes == null || rawAssemblyBytes.Length == 0)
                {
                    errorMessage = "载荷程序集数据为空";
                    return false;
                }

                var proc = Process.GetProcessById(processId);
                IntPtr remoteMonoBase = IntPtr.Zero;
                string? monoDllPath = null;

                foreach (ProcessModule mod in proc.Modules)
                {
                    if (mod.ModuleName.Equals("mono-2.0-bdwgc.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        remoteMonoBase = mod.BaseAddress;
                        monoDllPath = mod.FileName;
                        break;
                    }
                }

                if (remoteMonoBase == IntPtr.Zero || string.IsNullOrEmpty(monoDllPath))
                {
                    errorMessage = "目标进程未加载 mono-2.0-bdwgc.dll (可能游戏尚未完全就绪或非 Mono 运行时)";
                    return false;
                }

                IntPtr localMono = LoadLibraryExA(monoDllPath, IntPtr.Zero, DONT_RESOLVE_DLL_REFERENCES);
                if (localMono == IntPtr.Zero)
                {
                    errorMessage = $"无法加载本地 Mono 库: {monoDllPath}";
                    return false;
                }

                IntPtr GetRemoteFunc(string name)
                {
                    IntPtr localAddr = GetProcAddress(localMono, name);
                    if (localAddr == IntPtr.Zero)
                    {
                        throw new Exception($"未找到 Mono 导出函数: {name}");
                    }
                    long rva = localAddr.ToInt64() - localMono.ToInt64();
                    return new IntPtr(remoteMonoBase.ToInt64() + rva);
                }

                IntPtr fn_mono_get_root_domain = GetRemoteFunc("mono_get_root_domain");
                IntPtr fn_mono_thread_attach = GetRemoteFunc("mono_thread_attach");
                IntPtr fn_mono_image_open_from_data = GetRemoteFunc("mono_image_open_from_data");
                IntPtr fn_mono_assembly_load_from_full = GetRemoteFunc("mono_assembly_load_from_full");
                IntPtr fn_mono_assembly_get_image = GetRemoteFunc("mono_assembly_get_image");
                IntPtr fn_mono_class_from_name = GetRemoteFunc("mono_class_from_name");
                IntPtr fn_mono_class_get_method_from_name = GetRemoteFunc("mono_class_get_method_from_name");
                IntPtr fn_mono_runtime_invoke = GetRemoteFunc("mono_runtime_invoke");

                FreeLibrary(localMono);

                var memoryStream = new MemoryStream();
                int shellcodeOffset = 0;
                using (var writer = new BinaryWriter(memoryStream))
                {
                    writer.Write(fn_mono_get_root_domain.ToInt64());            // +0x00
                    writer.Write(fn_mono_thread_attach.ToInt64());              // +0x08
                    writer.Write(fn_mono_image_open_from_data.ToInt64());       // +0x10
                    writer.Write(fn_mono_assembly_load_from_full.ToInt64());    // +0x18
                    writer.Write(fn_mono_assembly_get_image.ToInt64());         // +0x20
                    writer.Write(fn_mono_class_from_name.ToInt64());            // +0x28
                    writer.Write(fn_mono_class_get_method_from_name.ToInt64()); // +0x30
                    writer.Write(fn_mono_runtime_invoke.ToInt64());             // +0x38

                    writer.Write((long)rawAssemblyBytes.Length);                // +0x40
                    long payloadOffsetPlaceholderPos = writer.BaseStream.Position;
                    writer.Write((long)0);                                      // +0x48 (placeholder)
                    writer.Write((int)0);                                       // +0x50 status
                    writer.Write((int)0);                                       // +0x54 padding

                    int offsetAsm = (int)writer.BaseStream.Position;
                    writer.Write(Encoding.UTF8.GetBytes(assemblyName));
                    writer.Write((byte)0);

                    int offsetNs = (int)writer.BaseStream.Position;
                    writer.Write(Encoding.UTF8.GetBytes(namespaceName));
                    writer.Write((byte)0);

                    int offsetCls = (int)writer.BaseStream.Position;
                    writer.Write(Encoding.UTF8.GetBytes(className));
                    writer.Write((byte)0);

                    int offsetMethod = (int)writer.BaseStream.Position;
                    writer.Write(Encoding.UTF8.GetBytes(methodName));
                    writer.Write((byte)0);

                    while (writer.BaseStream.Position % 16 != 0)
                    {
                        writer.Write((byte)0x90);
                    }

                    shellcodeOffset = (int)writer.BaseStream.Position;

                    var sc = new List<byte>
                    {
                        0x53,                                     // push rbx
                        0x41, 0x54,                               // push r12
                        0x41, 0x55,                               // push r13
                        0x41, 0x56,                               // push r14
                        0x41, 0x57,                               // push r15
                        0x48, 0x83, 0xEC, 0x30,                   // sub rsp, 30h

                        0x48, 0x89, 0xCB                          // mov rbx, rcx
                    };

                    // 1. domain = mono_get_root_domain()
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x00 }); // call [rbx + 0x00]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 }); // test rax, rax
                    sc.Add(0x0F); sc.Add(0x84);                   // jz rel32 err1
                    int jzErr1Pos = sc.Count;
                    sc.AddRange(new byte[] { 0, 0, 0, 0 });
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC4 }); // mov r12, rax

                    // 2. mono_thread_attach(domain)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xE1 }); // mov rcx, r12
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x08 }); // call [rbx + 0x08]

                    // 3. image = mono_image_open_from_data(data, data_len, need_copy=1, &status)
                    sc.AddRange(new byte[] { 0x48, 0x8B, 0x43, 0x48 }); // mov rax, [rbx + 0x48]
                    sc.AddRange(new byte[] { 0x48, 0x89, 0xD9 });       // mov rcx, rbx
                    sc.AddRange(new byte[] { 0x48, 0x01, 0xC1 });       // add rcx, rax
                    sc.AddRange(new byte[] { 0x8B, 0x53, 0x40 });       // mov edx, dword ptr [rbx + 0x40]
                    sc.AddRange(new byte[] { 0x41, 0xB8, 0x01, 0x00, 0x00, 0x00 }); // mov r8d, 1
                    sc.AddRange(new byte[] { 0x4C, 0x8D, 0x4B, 0x50 }); // lea r9, [rbx + 0x50]
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x10 });       // call [rbx + 0x10]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 });       // test rax, rax
                    sc.Add(0x0F); sc.Add(0x84);                         // jz rel32 err3
                    int jzErr3Pos = sc.Count;
                    sc.AddRange(new byte[] { 0, 0, 0, 0 });
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC5 });       // mov r13, rax

                    // 4. assembly = mono_assembly_load_from_full(image, fname, &status, refonly=0)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xE9 });       // mov rcx, r13
                    sc.AddRange(new byte[] { 0x48, 0x8D, 0x93 });       // lea rdx, [rbx + offsetAsm]
                    sc.AddRange(BitConverter.GetBytes(offsetAsm));
                    sc.AddRange(new byte[] { 0x4C, 0x8D, 0x43, 0x50 }); // lea r8, [rbx + 0x50]
                    sc.AddRange(new byte[] { 0x45, 0x31, 0xC9 });       // xor r9d, r9d
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x18 });       // call [rbx + 0x18]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 });       // test rax, rax
                    sc.Add(0x0F); sc.Add(0x84);                         // jz rel32 err4
                    int jzErr4Pos = sc.Count;
                    sc.AddRange(new byte[] { 0, 0, 0, 0 });
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC5 });       // mov r13, rax

                    // 5. image = mono_assembly_get_image(assembly)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xE9 });       // mov rcx, r13
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x20 });       // call [rbx + 0x20]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 });       // test rax, rax
                    sc.Add(0x0F); sc.Add(0x84);                         // jz rel32 err5
                    int jzErr5Pos = sc.Count;
                    sc.AddRange(new byte[] { 0, 0, 0, 0 });
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC6 });       // mov r14, rax

                    // 6. klass = mono_class_from_name(image, ns, cls)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xF1 });       // mov rcx, r14
                    sc.AddRange(new byte[] { 0x48, 0x8D, 0x93 });       // lea rdx, [rbx + offsetNs]
                    sc.AddRange(BitConverter.GetBytes(offsetNs));
                    sc.AddRange(new byte[] { 0x4C, 0x8D, 0x83 });       // lea r8, [rbx + offsetCls]
                    sc.AddRange(BitConverter.GetBytes(offsetCls));
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x28 });       // call [rbx + 0x28]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 });       // test rax, rax
                    sc.Add(0x0F); sc.Add(0x84);                         // jz rel32 err6
                    int jzErr6Pos = sc.Count;
                    sc.AddRange(new byte[] { 0, 0, 0, 0 });
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC6 });       // mov r14, rax

                    // 7. method = mono_class_get_method_from_name(klass, methodName, 0)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xF1 });       // mov rcx, r14
                    sc.AddRange(new byte[] { 0x48, 0x8D, 0x93 });       // lea rdx, [rbx + offsetMethod]
                    sc.AddRange(BitConverter.GetBytes(offsetMethod));
                    sc.AddRange(new byte[] { 0x45, 0x31, 0xC0 });       // xor r8d, r8d
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x30 });       // call [rbx + 0x30]
                    sc.AddRange(new byte[] { 0x48, 0x85, 0xC0 });       // test rax, rax
                    sc.Add(0x0F); sc.Add(0x84);                         // jz rel32 err7
                    int jzErr7Pos = sc.Count;
                    sc.AddRange(new byte[] { 0, 0, 0, 0 });
                    sc.AddRange(new byte[] { 0x49, 0x89, 0xC6 });       // mov r14, rax

                    // 8. mono_runtime_invoke(method, NULL, NULL, NULL)
                    sc.AddRange(new byte[] { 0x4C, 0x89, 0xF1 });       // mov rcx, r14
                    sc.AddRange(new byte[] { 0x31, 0xD2 });             // xor edx, edx
                    sc.AddRange(new byte[] { 0x45, 0x31, 0xC0 });       // xor r8d, r8d
                    sc.AddRange(new byte[] { 0x45, 0x31, 0xC9 });       // xor r9d, r9d
                    sc.AddRange(new byte[] { 0xFF, 0x53, 0x38 });       // call [rbx + 0x38]

                    // 成功退出: eax = 0
                    sc.AddRange(new byte[] { 0x31, 0xC0 });             // xor eax, eax
                    sc.Add(0xE9);                                       // jmp rel32 finish
                    int jmpFinishPos = sc.Count;
                    sc.AddRange(new byte[] { 0, 0, 0, 0 });

                    // err1: eax = 101
                    int err1Offset = sc.Count;
                    sc.AddRange(new byte[] { 0xB8, 0x65, 0x00, 0x00, 0x00 }); // mov eax, 101
                    sc.Add(0xEB); int jmpFinishFrom1 = sc.Count; sc.Add(0);

                    // err3: eax = 103
                    int err3Offset = sc.Count;
                    sc.AddRange(new byte[] { 0xB8, 0x67, 0x00, 0x00, 0x00 }); // mov eax, 103
                    sc.Add(0xEB); int jmpFinishFrom3 = sc.Count; sc.Add(0);

                    // err4: eax = 104
                    int err4Offset = sc.Count;
                    sc.AddRange(new byte[] { 0xB8, 0x68, 0x00, 0x00, 0x00 }); // mov eax, 104
                    sc.Add(0xEB); int jmpFinishFrom4 = sc.Count; sc.Add(0);

                    // err5: eax = 105
                    int err5Offset = sc.Count;
                    sc.AddRange(new byte[] { 0xB8, 0x69, 0x00, 0x00, 0x00 }); // mov eax, 105
                    sc.Add(0xEB); int jmpFinishFrom5 = sc.Count; sc.Add(0);

                    // err6: eax = 106
                    int err6Offset = sc.Count;
                    sc.AddRange(new byte[] { 0xB8, 0x6A, 0x00, 0x00, 0x00 }); // mov eax, 106
                    sc.Add(0xEB); int jmpFinishFrom6 = sc.Count; sc.Add(0);

                    // err7: eax = 107
                    int err7Offset = sc.Count;
                    sc.AddRange(new byte[] { 0xB8, 0x6B, 0x00, 0x00, 0x00 }); // mov eax, 107

                    // finish:
                    int finishOffset = sc.Count;
                    sc.AddRange(new byte[]
                    {
                        0x48, 0x83, 0xC4, 0x30,                   // add rsp, 30h
                        0x41, 0x5F,                               // pop r15
                        0x41, 0x5E,                               // pop r14
                        0x41, 0x5D,                               // pop r13
                        0x41, 0x5C,                               // pop r12
                        0x5B,                                     // pop rbx
                        0xC3                                      // ret
                    });

                    void PatchRel32(int pos, int target)
                    {
                        int rel = target - (pos + 4);
                        byte[] b = BitConverter.GetBytes(rel);
                        for (int i = 0; i < 4; i++) sc[pos + i] = b[i];
                    }

                    PatchRel32(jzErr1Pos, err1Offset);
                    PatchRel32(jzErr3Pos, err3Offset);
                    PatchRel32(jzErr4Pos, err4Offset);
                    PatchRel32(jzErr5Pos, err5Offset);
                    PatchRel32(jzErr6Pos, err6Offset);
                    PatchRel32(jzErr7Pos, err7Offset);
                    PatchRel32(jmpFinishPos, finishOffset);

                    sc[jmpFinishFrom1] = (byte)(finishOffset - (jmpFinishFrom1 + 1));
                    sc[jmpFinishFrom3] = (byte)(finishOffset - (jmpFinishFrom3 + 1));
                    sc[jmpFinishFrom4] = (byte)(finishOffset - (jmpFinishFrom4 + 1));
                    sc[jmpFinishFrom5] = (byte)(finishOffset - (jmpFinishFrom5 + 1));
                    sc[jmpFinishFrom6] = (byte)(finishOffset - (jmpFinishFrom6 + 1));

                    writer.Write(sc.ToArray());

                    while (writer.BaseStream.Position % 16 != 0)
                    {
                        writer.Write((byte)0x90);
                    }

                    int payloadOffset = (int)writer.BaseStream.Position;
                    writer.Write(rawAssemblyBytes);

                    writer.BaseStream.Seek(payloadOffsetPlaceholderPos, SeekOrigin.Begin);
                    writer.Write((long)payloadOffset);
                }

                byte[] buffer = memoryStream.ToArray();
                allocSize = (uint)buffer.Length;

                hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
                if (hProcess == IntPtr.Zero)
                {
                    errorMessage = $"打开游戏进程失败 (PID: {processId}, 错误码: {Marshal.GetLastWin32Error()})";
                    return false;
                }

                remoteAlloc = VirtualAllocEx(hProcess, IntPtr.Zero, allocSize, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                if (remoteAlloc == IntPtr.Zero)
                {
                    errorMessage = $"远程分配内存失败 (大小: {allocSize}, 错误码: {Marshal.GetLastWin32Error()})";
                    return false;
                }

                if (!WriteProcessMemory(hProcess, remoteAlloc, buffer, allocSize, out _))
                {
                    errorMessage = $"向远程内存写入注入数据失败 (错误码: {Marshal.GetLastWin32Error()})";
                    return false;
                }

                IntPtr startAddress = new IntPtr(remoteAlloc.ToInt64() + shellcodeOffset);

                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, startAddress, remoteAlloc, 0, out _);
                if (hThread == IntPtr.Zero)
                {
                    errorMessage = $"创建远程执行线程失败 (错误码: {Marshal.GetLastWin32Error()})";
                    return false;
                }

                WaitForSingleObject(hThread, 10000);
                GetExitCodeThread(hThread, out uint exitCode);
                CloseHandle(hThread);

                if (exitCode != 0)
                {
                    errorMessage = exitCode switch
                    {
                        101 => "mono_get_root_domain() 返回空，Mono环境未就绪",
                        103 => "mono_image_open_from_data() 失败，无法从内存打开程序集映像",
                        104 => "mono_assembly_load_from_full() 失败，无法在Mono域中加载映像",
                        105 => "mono_assembly_get_image() 失败",
                        106 => $"mono_class_from_name() 失败，未找到类 {namespaceName}.{className}",
                        107 => $"mono_class_get_method_from_name() 失败，未找到入口方法 {methodName}",
                        _ => $"远程线程执行返回失败代码: {exitCode}"
                    };
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "内存载荷注入异常: " + ex.Message;
                return false;
            }
            finally
            {
                if (remoteAlloc != IntPtr.Zero && hProcess != IntPtr.Zero)
                {
                    VirtualFreeEx(hProcess, remoteAlloc, 0, MEM_RELEASE);
                }
                if (hProcess != IntPtr.Zero)
                {
                    CloseHandle(hProcess);
                }
            }
        }
    }
}
