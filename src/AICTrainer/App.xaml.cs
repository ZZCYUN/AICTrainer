using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using AICTrainer.Injector;
using AICTrainer.Services;

namespace AICTrainer
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private static System.Threading.Mutex? _appMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try { File.WriteAllText(@"C:\AliceInCradle\crash.log", args.ExceptionObject?.ToString()); } catch { }
            };
            DispatcherUnhandledException += (s, args) =>
            {
                try { File.WriteAllText(@"C:\AliceInCradle\crash.log", args.Exception?.ToString()); } catch { }
            };

            if (e.Args.Length > 0 && (e.Args[0] == "--test" || e.Args[0] == "--cli"))
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                RunCliTestAsync().GetAwaiter().GetResult();
                Environment.Exit(0);
                return;
            }

            try
            {
                _appMutex = new System.Threading.Mutex(true, "AICTrainer_SingleInstance_Mutex", out bool isNewInstance);
                if (!isNewInstance)
                {
                    var currentProc = Process.GetCurrentProcess();
                    var existing = Process.GetProcessesByName(currentProc.ProcessName)
                        .FirstOrDefault(p => p.Id != currentProc.Id && p.MainWindowHandle != IntPtr.Zero);
                    if (existing != null)
                    {
                        ShowWindowAsync(existing.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(existing.MainWindowHandle);
                    }
                    Shutdown();
                    return;
                }

                base.OnStartup(e);
                var win = new Views.MainWindow();
                MainWindow = win;
                win.Show();

                try { if (File.Exists(@"C:\AliceInCradle\crash.log")) File.Delete(@"C:\AliceInCradle\crash.log"); } catch { }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(@"C:\AliceInCradle\crash.log", ex.ToString()); } catch { }
                MessageBox.Show($"修改器启动出现异常：\n{ex.Message}\n\n详细崩溃日志已记录至 C:\\AliceInCradle\\crash.log", "启动异常", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private void Log(string msg)
        {
            Console.WriteLine(msg);
            try
            {
                File.AppendAllText(@"C:\AliceInCradle\test.log", msg + Environment.NewLine);
            }
            catch { }
        }

        private async Task RunCliTestAsync()
        {
            try { File.Delete(@"C:\AliceInCradle\test.log"); } catch { }
            Log("[AICTrainer CLI] Starting automated test mode...");
            var proc = Process.GetProcessesByName("AliceInCradle").FirstOrDefault();
            if (proc == null)
            {
                Log("[AICTrainer CLI] Error: AliceInCradle process not found!");
                return;
            }

            byte[]? payloadBytes = ProcessMonitor.GetEmbeddedPayloadBytes();
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                Log("[AICTrainer CLI] Error: Embedded payload bytes not found!");
                return;
            }

            Log($"[AICTrainer CLI] In-memory injecting payload ({payloadBytes.Length} bytes) into PID {proc.Id} (0 disk drop)...");

            bool ok = MonoInjector.InjectFromMemory(proc.Id, payloadBytes, "AICModPayload", "AICMod", "Loader", "Init", out string err);
            if (!ok)
            {
                Log($"[AICTrainer CLI] Injection failed: {err}");
                return;
            }

            Log("[AICTrainer CLI] Injection successful! Connecting to IPC server...");
            var client = new TrainerClient();
            bool connected = false;
            for (int i = 0; i < 10; i++)
            {
                Log($"[AICTrainer CLI] Connection attempt {i + 1}/10...");
                if (await client.ConnectAsync(1000))
                {
                    connected = true;
                    Log("[AICTrainer CLI] Connected successfully!");
                    break;
                }
                await Task.Delay(300);
            }

            if (!connected)
            {
                Log("[AICTrainer CLI] Error: Could not connect to IPC server!");
                return;
            }

            Log("[AICTrainer CLI] Connected to IPC server! Waiting for GameState sync...");
            bool stateReceived = false;
            client.OnStateReceived += state =>
            {
                stateReceived = true;
                Log($"[AICTrainer CLI] Received GameState: Ready={state.IsGameReady}, Map={state.CurrentMap}, Version={state.GameVersion}, Engine={state.EngineVersion}, Patches={state.ActivePatchCount}/{state.TotalPatchCount}, HP={state.Hp}/{state.MaxHp}, Grip={state.Grip:F3}");
            };

            var testConfig = new AICShared.ModConfigDto
            {
                DisableMosaic = true,
                OneHitKill = true,
                InfiniteJump = true
            };
            client.SendConfig(testConfig);
            Log("[AICTrainer CLI] Sent test config (DisableMosaic=true, OneHitKill=true, InfiniteJump=true)");

            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(500);
                if (stateReceived) break;
            }

            Log($"[AICTrainer CLI] Test completed successfully. State received: {stateReceived}");
            client.Disconnect();
        }
    }
}
