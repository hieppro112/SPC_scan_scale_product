using System;
using System.IO;
using System.Text;

namespace scancode.Helper
{
    public static class AppLog
    {
        private static readonly object _lock = new object();

        public static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "scancode", "logs", "app.log");

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));

                    // Giữ file log gọn: quá 1MB thì làm lại từ đầu
                    var info = new FileInfo(LogPath);
                    if (info.Exists && info.Length > 1024 * 1024)
                        info.Delete();

                    File.AppendAllText(
                        LogPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [pid {System.Diagnostics.Process.GetCurrentProcess().Id}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Không để việc ghi log làm hỏng app
            }
        }

        public static void LogEnvironment()
        {
            Write("OS: " + Environment.OSVersion + " | 64-bit OS: " + Environment.Is64BitOperatingSystem + " | 64-bit process: " + Environment.Is64BitProcess);
            Write(".NET CLR: " + Environment.Version);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Write("BaseDirectory: " + baseDir);

            string[] mustExist =
            {
                Path.Combine("Models", "best.onnx"),
                "onnxruntime.dll",
                "OpenCvSharpExtern.dll",
                "opencv_videoio_ffmpeg455_64.dll"
            };

            foreach (string rel in mustExist)
            {
                bool exists = File.Exists(Path.Combine(baseDir, rel))
                    || File.Exists(Path.Combine(baseDir, "x64", rel))
                    || File.Exists(Path.Combine(baseDir, "dll", "x64", rel))
                    || File.Exists(Path.Combine(baseDir, "runtimes", "win-x64", "native", rel));
                Write("File " + rel + ": " + (exists ? "OK" : "KHÔNG TÌM THẤY"));
            }
        }
    }
}
