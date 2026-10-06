using scancode.Helper;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace scancode
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            AppLog.Write("=== App khởi động ===");
            AppLog.LogEnvironment();

            DispatcherUnhandledException += (s, e) =>
            {
                AppLog.Write("DispatcherUnhandledException: " + e.Exception);
                var root = e.Exception.GetBaseException();
                MessageBox.Show(
                    "Ứng dụng gặp lỗi:\n" + root.GetType().Name + ": " + root.Message + "\n\nChi tiết xem file:\n" + AppLog.LogPath,
                    "Scale Scan Pro", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                AppLog.Write("AppDomain.UnhandledException (isTerminating=" + e.IsTerminating + "): " + e.ExceptionObject);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                AppLog.Write("UnobservedTaskException: " + e.Exception);
            };

            Exit += (s, e) => AppLog.Write("=== App thoát (ExitCode " + e.ApplicationExitCode + ") ===");
        }
    }
}
