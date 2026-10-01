using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace scancode.UI
{
    public enum NotifyType
    {
        Success,
        Warning,
        Danger
    }

    public partial class NotifyDialog : Window
    {
        private const int AutoCloseSeconds = 3;

        public bool Confirmed { get; private set; } = false;

        private readonly DispatcherTimer _autoCloseTimer;

        public NotifyDialog(string message,
                            NotifyType type = NotifyType.Success,
                            bool showCancel = false,
                            string okText = "Xác nhận")
        {
            InitializeComponent();

            // Tự tắt sau một khoảng thời gian nếu người dùng không bấm nút nào
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoCloseSeconds) };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };
            Loaded += (s, e) => _autoCloseTimer.Start();
            Closed += (s, e) => _autoCloseTimer.Stop();

            // Chọn theme theo loại
            string icon, title, colorHex;
            switch (type)
            {
                case NotifyType.Warning:
                    icon = "⚠";
                    title = "Cảnh báo";
                    colorHex = "#F59E0B";
                    break;
                case NotifyType.Danger:
                    icon = "✕";
                    title = "Nguy hiểm";
                    colorHex = "#EF4444";
                    break;
                default:
                    icon = "✓";
                    title = "Thành công";
                    colorHex = "#22C55E";
                    break;
            }

            // Áp màu
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(colorHex));

            TopBar.Background = brush;
            IconCircle.Background = brush;
            BtnOk.Background = brush;

            // Nội dung
            TxtIcon.Text = icon;
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            BtnOk.Content = okText;

            // Nút Bỏ qua
            if (showCancel)
            {
                BtnCancel.Visibility = Visibility.Visible;
            }
            else
            {
                BtnCancel.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        public static bool ShowSuccess(string msg, Window owner = null, bool showCancel = false)
            => ShowInternal(msg, NotifyType.Success, owner, showCancel);

        public static bool ShowWarning(string msg, Window owner = null, bool showCancel = false)
            => ShowInternal(msg, NotifyType.Warning, owner, showCancel);

        public static bool ShowDanger(string msg, Window owner = null, bool showCancel = false)
            => ShowInternal(msg, NotifyType.Danger, owner, showCancel);

        private static bool ShowInternal(string msg, NotifyType type, Window owner, bool showCancel)
        {
            var dlg = new NotifyDialog(msg, type, showCancel) { Owner = owner };
            dlg.ShowDialog();
            return dlg.Confirmed;
        }
    }
}