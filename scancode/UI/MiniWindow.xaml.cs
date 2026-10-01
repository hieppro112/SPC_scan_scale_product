using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace scancode.UI
{
    public partial class MiniWindow : Window
    {
        public event Action RestoreRequested;

        private readonly DispatcherTimer _toastTimer;

        public MiniWindow()
        {
            InitializeComponent();

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                ToastPanel.Visibility = Visibility.Collapsed;
            };
        }

        public void UpdateFrame(BitmapSource frame)
        {
            MiniCamera.Source = frame;
        }

        public void ShowSuccessToast(string message)
        {
            TxtToast.Text = message;
            ToastPanel.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        public void SnapToBottomRight()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            RestoreRequested?.Invoke();
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                RestoreRequested?.Invoke();
                return;
            }

            DragMove();
        }
    }
}
