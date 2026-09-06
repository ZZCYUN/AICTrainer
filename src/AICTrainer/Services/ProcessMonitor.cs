using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AICTrainer.Services
{
    public class ProcessMonitor
    {
        public const string ProcessName = "AliceInCradle";

        private CancellationTokenSource? _cts;
        private Process? _currentProcess;

        public bool IsGameRunning => _currentProcess != null && !_currentProcess.HasExited;
        public Process? TargetProcess => _currentProcess;

        public string GameDirectory { get; private set; } = string.Empty;
        public string GameExecutablePath { get; private set; } = string.Empty;
        public string GameFileVersion { get; private set; } = string.Empty;
        public string ManagedDirectory { get; private set; } = string.Empty;

        public event Action<Process>? OnGameDetected;
        public event Action? OnGameExited;

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => MonitorLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var proc = Process.GetProcessesByName(ProcessName).FirstOrDefault();

                    if (proc != null && !proc.HasExited)
                    {
                        if (_currentProcess == null || _currentProcess.Id != proc.Id)
                        {
                            _currentProcess = proc;
                            try
                            {
                                string exePath = proc.MainModule?.FileName ?? string.Empty;
                                GameExecutablePath = exePath;
                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    GameDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty;
                                    ManagedDirectory = System.IO.Path.Combine(GameDirectory, "AliceInCradle_Data", "Managed");
                                    var vi = FileVersionInfo.GetVersionInfo(exePath);
                                    GameFileVersion = vi.ProductVersion ?? vi.FileVersion ?? "未知版本";
                                }
                            }
                            catch { }

                            OnGameDetected?.Invoke(proc);
                        }
                    }
                    else
                    {
                        if (_currentProcess != null)
                        {
                            _currentProcess = null;
                            GameDirectory = string.Empty;
                            GameExecutablePath = string.Empty;
                            GameFileVersion = string.Empty;
                            ManagedDirectory = string.Empty;
                            OnGameExited?.Invoke();
                        }
                    }
                }
                catch { }

                await Task.Delay(1000, token);
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public static async Task<Process?> WaitForGameWindowAsync(int timeoutSeconds = 60, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSeconds && !ct.IsCancellationRequested)
            {
                var proc = Process.GetProcessesByName(ProcessName).FirstOrDefault();
                if (proc != null && !proc.HasExited)
                {
                    try
                    {
                        proc.Refresh();
                        IntPtr hWnd = proc.MainWindowHandle;
                        if (hWnd != IntPtr.Zero && (IsWindowVisible(hWnd) || !string.IsNullOrEmpty(proc.MainWindowTitle)))
                        {
                            return proc;
                        }
                    }
                    catch { }
                }
                await Task.Delay(250, ct);
            }
            return null;
        }

        public static bool LaunchGame(out string err)
        {
            err = string.Empty;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exePath = System.IO.Path.Combine(baseDir, "AliceInCradle.exe");
                if (!System.IO.File.Exists(exePath))
                {
                    err = $"未在当前目录找到 AliceInCradle.exe: {exePath}";
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = baseDir,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        public static byte[]? GetEmbeddedPayloadBytes()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string fullResName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("AICModPayload.dll", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

                if (!string.IsNullOrEmpty(fullResName))
                {
                    using var stream = asm.GetManifestResourceStream(fullResName);
                    if (stream != null)
                    {
                        byte[] buffer = new byte[stream.Length];
                        int totalRead = 0;
                        while (totalRead < buffer.Length)
                        {
                            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }
                        return buffer;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ProcessMonitor] GetEmbeddedPayloadBytes failed: " + ex.Message);
            }
            return null;
        }
    }
}
