using System;
using System.IO;

namespace AICTrainer.Services
{
    /// <summary>
    /// 轻量诊断日志（写入系统临时目录 aic_trainer_diag.log），用于排查客户端与 MOD 的 IPC 数据流问题。
    /// </summary>
    internal static class DiagLog
    {
        public static string LogPath => Path.Combine(Path.GetTempPath(), "aic_trainer_diag.log");

        public static void Write(string msg)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { }
        }
    }
}
