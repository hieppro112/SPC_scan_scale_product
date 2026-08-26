using OpenCvSharp;
using scancode.Binding;
using scancode.Models;
using scancode.Services;
using scancode.UI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace scancode
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        // =========================================================
        // SERVICES
        // =========================================================

        private CameraService camera;
        private portService _portService;
        private SQLService sql;


        // =========================================================
        // DATA
        // =========================================================

        private string lastBarcode = "";

        private DataBinding _dataBinding =
            new DataBinding();

        private List<dataHistory> LHistory;

        private ProductData prData;

        private float kg_scale = 0;


        // =========================================================
        // PAGINATION
        // =========================================================

        private int pageCurrent = 1;
        private int countPage = 0;
        private int total_item_page = 0;


        // =========================================================
        // UI
        // =========================================================

        private Storyboard blinkStoryBoard;


        // =========================================================
        // CAMERA DISPLAY BUFFER
        // =========================================================

        /*
         * RẤT QUAN TRỌNG
         *
         * Không cho Dispatcher xếp hàng hàng trăm frame.
         *
         * Camera có thể tạo:
         *
         * Frame1
         * Frame2
         * Frame3
         * Frame4
         * ...
         *
         * Nhưng WPF chỉ cần frame mới nhất.
         */

        private readonly object _displayLock =
            new object();

        private Mat _pendingDisplayFrame;

        private bool _displayScheduled;


        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public MainWindow()
        {
            Console.WriteLine(
                $"64-bit Process: {Environment.Is64BitProcess}"
            );

            Console.WriteLine(
                $"64-bit OS: {Environment.Is64BitOperatingSystem}"
            );


            // =====================================================
            // SERVICES
            // =====================================================

            sql = new SQLService();

            InitializeComponent();

            DataContext = _dataBinding;

            camera = new CameraService();

            _portService = new portService();


            // =====================================================
            // SCALE
            // =====================================================

            LHistory =
                new List<dataHistory>();

            _portService.DataReceived +=
                Port_DataReceived;

            bool sttConnect =
                _portService.connect();


            // =====================================================
            // CAMERA EVENTS
            // =====================================================

            camera.FrameReceived +=
                Camera_FrameReceived;

            camera.BarcodeReceived +=
                Camera_BarcodeReceived;


            camera.GetCameraList(
                cbm_camera
            );


            // =====================================================
            // PRODUCT
            // =====================================================

            prData =
                new ProductData();


            // =====================================================
            // HISTORY
            // =====================================================

            LHistory =
                sql.GetListHistory();

            dgHistory.ItemsSource =
                LHistory;

            _dataBinding.totalList =
                LHistory.Count;

            _dataBinding.IsScaleConnected =
                sttConnect;


            // =====================================================
            // BLINK
            // =====================================================

            blinkStoryBoard =
                (Storyboard)FindResource(
                    "BlinkStoryboard"
                );
        }


        // =========================================================
        // WINDOW CLOSED
        // =========================================================

        private async void Window_Closed(
            object sender,
            EventArgs e)
        {
            try
            {
                // Không để frame camera tồn tại
                ClearPendingDisplayFrame();


                // Dừng camera
                if (camera != null)
                {
                    await camera.Stop();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Window close error: " +
                    ex.Message
                );
            }
        }


        // =========================================================
        // CLEAR CAMERA BUFFER
        // =========================================================

        private void ClearPendingDisplayFrame()
        {
            lock (_displayLock)
            {
                _pendingDisplayFrame?.Dispose();

                _pendingDisplayFrame =
                    null;

                _displayScheduled =
                    false;
            }
        }


        // =========================================================
        // CAMERA FRAME
        // =========================================================

        private void Camera_FrameReceived(
            Mat frame)
        {
            if (
                frame == null ||
                frame.Empty())
            {
                frame?.Dispose();
                return;
            }


            lock (_displayLock)
            {
                /*
                 * Nếu WPF chưa kịp hiển thị frame trước
                 * thì bỏ frame đó.
                 *
                 * Chỉ giữ frame mới nhất.
                 */

                _pendingDisplayFrame?.Dispose();

                _pendingDisplayFrame =
                    frame;


                /*
                 * Đã có callback UI đang chờ.
                 *
                 * Không tạo thêm Dispatcher callback.
                 */

                if (_displayScheduled)
                    return;


                _displayScheduled =
                    true;
            }


            Dispatcher.BeginInvoke(
                new Action(
                    DisplayLatestFrame
                ),
                System.Windows.Threading
                    .DispatcherPriority.Render
            );
        }


        // =========================================================
        // DISPLAY LATEST CAMERA FRAME
        // =========================================================

        private void DisplayLatestFrame()
        {
            Mat frame = null;


            lock (_displayLock)
            {
                frame =
                    _pendingDisplayFrame;

                _pendingDisplayFrame =
                    null;

                _displayScheduled =
                    false;
            }


            if (frame == null)
                return;


            try
            {
                /*
                 * MAT → BitmapSource
                 */

                var bitmap =
                    OpenCvSharp
                        .WpfExtensions
                        .BitmapSourceConverter
                        .ToBitmapSource(
                            frame
                        );


                /*
                 * Cho WPF sử dụng bitmap độc lập
                 * với thread camera.
                 */

                bitmap.Freeze();


                imgCamera.Source =
                    bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Display camera error: " +
                    ex.Message
                );
            }
            finally
            {
                frame.Dispose();
            }


            /*
             * Trong lúc UI xử lý frame này,
             * camera có thể đã gửi frame mới.
             *
             * Nếu có → schedule lại.
             */

            lock (_displayLock)
            {
                if (
                    _pendingDisplayFrame != null &&
                    !_displayScheduled)
                {
                    _displayScheduled =
                        true;

                    Dispatcher.BeginInvoke(
                        new Action(
                            DisplayLatestFrame
                        ),
                        System.Windows.Threading
                            .DispatcherPriority.Render
                    );
                }
            }
        }


        // =========================================================
        // BARCODE RECEIVED
        // =========================================================

        private void Camera_BarcodeReceived(
            string code)
        {
            if (
                string.IsNullOrWhiteSpace(
                    code))
            {
                return;
            }


            /*
             * Không xử lý cùng một barcode
             * liên tục.
             */

            if (code == lastBarcode)
                return;


            lastBarcode =
                code;


            /*
             * Không dùng Invoke vì nó block
             * thread DetectionLoop.
             */

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    OnBarcodeDetected(
                        code,
                        ""
                    );
                })
            );
        }


        // =========================================================
        // SCALE DATA
        // =========================================================

        private void Port_DataReceived(
            string data)
        {
            string s = "";


            foreach (char c in data)
            {
                try
                {
                    int a =
                        int.Parse(
                            c.ToString()
                        );


                    s +=
                        a.ToString();


                    kg_scale =
                        (kg_scale * 10) + a;
                }
                catch
                {
                    continue;
                }
            }


            /*
             * Chỉ update UI ở Dispatcher.
             */

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    TxtKg.Text =s;
                    insertDataHistory();
                })
            );
        }


        // =========================================================
        // MESSAGE
        // =========================================================

        public static void messageLog(
            string s)
        {
            MessageBox.Show(s);
        }


        // =========================================================
        // BLINK
        // =========================================================

        private void StartBlink(
            FrameworkElement element)
        {
            blinkStoryBoard.Begin(
                element,
                true
            );
        }


        // =========================================================
        // BARCODE PROCESS
        // =========================================================

        public void OnBarcodeDetected(
            string code,
            string type)
        {
            Console.WriteLine(
                "Barcode: " + code
            );


            try
            {
                if (
                    string.IsNullOrWhiteSpace(
                        code))
                {
                    txtBarcode.Text =
                        "Po không hợp lệ !!!";

                    txtBarcode.Foreground =
                        Brushes.Red;

                    return;
                }


                // =================================================
                // FORMAT 1
                // =================================================

                if (
                    code.Split(',').Length >
                    2)
                {
                    string getPO =
                        code
                        .Split(',')[1]
                        .Trim();


                    prData =
                        sql.GetDataForPO(
                            getPO
                        );


                    if (prData != null)
                    {
                        txtBarcode.Text =
                            getPO;

                        txtBarcode.Foreground =
                            Brushes.Green;

                        _dataBinding.Barcode =
                            getPO;

                        TxtScanTime.Text =
                            DateTime.Now.ToString(
                                "HH:mm:ss"
                            );


                        txtPHCD.Text =
                            prData.PHCD;

                        TxtPHTX.Text =
                            prData.PHTX;

                        TxtPSTX.Text =
                            prData.PSTX;

                        TxtQty.Text =
                            prData.GAMNG.ToString();
                    }
                    else
                    {
                        txtBarcode.Text =
                            "Po không hợp lệ !!!";

                        txtBarcode.Foreground =
                            Brushes.Red;
                    }


                    return;
                }


                // =================================================
                // FORMAT 2
                // =================================================

                if (code.Length >= 11)
                {
                    prData =
                        sql.GetDataForPO(
                            code
                        );


                    if (prData != null)
                    {
                        txtBarcode.Text =
                            code;

                        txtBarcode.Foreground =
                            Brushes.Green;

                        _dataBinding.Barcode =
                            code;

                        TxtScanTime.Text =
                            DateTime.Now.ToString(
                                "HH:mm:ss"
                            );
                    }
                    else
                    {
                        txtBarcode.Text =
                            "Po không hợp lệ !!!";

                        txtBarcode.Foreground =
                            Brushes.Red;
                    }


                    return;
                }


                // =================================================
                // INVALID
                // =================================================

                txtBarcode.Text =
                    "Po không hợp lệ !!!";

                txtBarcode.Foreground =
                    Brushes.Red;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Barcode process error: " +
                    ex.Message
                );


                txtBarcode.Text =
                    "Po không hợp lệ !!!";

                txtBarcode.Foreground =
                    Brushes.Red;

                TxtScanTime.Text =
                    DateTime.Now.ToString(
                        "HH:mm:ss"
                    );
            }
        }


        // =========================================================
        // RESET SCAN
        // =========================================================

        private void btn_restart_Click(
            object sender,
            RoutedEventArgs e)
        {
            /*
             * Cho phép quét lại barcode cũ.
             */

            lastBarcode = "";


            txtBarcode.Text =
                "-";

            TxtScanTime.Text =
                "-";

            TxtKg.Text =
                "0.00";

            kg_scale =
                0;

            prData =
                new ProductData();
        }


        // =========================================================
        // CAMERA SELECTION
        // =========================================================

        private async void cbm_camera_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (
                cbm_camera.SelectedIndex <
                0)
            {
                return;
            }


            LoadingGrid.Visibility =
                Visibility.Visible;


            try
            {
                await camera.ChangeCamera(
                    cbm_camera.SelectedIndex
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể đổi camera:\n" +
                    ex.Message
                );
            }
            finally
            {
                LoadingGrid.Visibility =
                    Visibility.Collapsed;
            }
        }


        // =========================================================
        // STOP CAMERA
        // =========================================================

        private async void Button_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                await camera.Stop();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể dừng camera:\n" +
                    ex.Message
                );
            }
        }


        // =========================================================
        // START CAMERA
        // =========================================================

        private async void btn_scan_continue_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                await camera.Start(
                    cbm_camera.SelectedIndex
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể bật camera:\n" +
                    ex.Message
                );
            }
        }


        // =========================================================
        // CONFIRM SCALE
        // =========================================================

        private void btn_confirm_scale_Click(
            object sender,
            RoutedEventArgs e)
        {
            insertDataHistory();
        }


        // =========================================================
        // INSERT HISTORY
        // =========================================================

        private void insertDataHistory()
        {
            /*
             * Tránh NullReferenceException
             * nếu chưa quét PO hợp lệ.
             */

            if (prData == null)
            {
                NotifyDialog.ShowWarning(
                    "Chưa có PO hợp lệ."
                );

                return;
            }


            bool result =
                sql.insertDataHistory(
                    new dataHistory
                    {
                        AUFNR =
                            prData.AUFNR,

                        GAMNG =
                            prData.GAMNG,

                        PHCD =
                            prData.PHCD,

                        PHTX =
                            prData.PHTX,

                        PSTX =
                            prData.PSTX,

                        UPDDT =
                            DateTime.Now,

                        kg =
                            kg_scale
                    }
                );


            if (result)
            {
                NotifyDialog.ShowSuccess(
                    "Thêm thành công"
                );


                LHistory =
                    sql.GetListHistory();


                dgHistory.ItemsSource =
                    LHistory;


                _dataBinding.totalList =
                    LHistory.Count;


                prData =
                    new ProductData();


                kg_scale =
                    0;
            }
            else
            {
                NotifyDialog.ShowWarning(
                    "Thêm thất bại"
                );
            }
        }


        // =========================================================
        // HISTORY RESET
        // =========================================================

        private void btn_restart_dgv_Click(
            object sender,
            RoutedEventArgs e)
        {
            update_status_changed();

            changed_value_prev_next();
        }


        // =========================================================
        // SEARCH
        // =========================================================

        private void TxtSearch_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;


            LHistory =
                sql.GetListHistory(
                    TxtSearch.Text
                );


            dgHistory.ItemsSource =
                LHistory;
        }


        // =========================================================
        // FIRST PAGE
        // =========================================================

        private void BtnFirstPage_Click(
            object sender,
            RoutedEventArgs e)
        {
            LHistory =
                sql.GetListHistory(
                    po: TxtSearch.Text,
                    length: total_item_page
                );


            dgHistory.ItemsSource =
                LHistory;


            _dataBinding.currentPage =
                1;

            pageCurrent =
                1;


            _dataBinding.Curren_index_item =
                1;

            _dataBinding.To_index_item =
                total_item_page;


            update_status_changed();
        }


        // =========================================================
        // PREVIOUS PAGE
        // =========================================================

        private void BtnPrevPage_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (pageCurrent <= 1)
                return;


            pageCurrent--;

            changed_value_prev_next();

            update_status_changed();
        }


        // =========================================================
        // NEXT PAGE
        // =========================================================

        private void BtnNextPage_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (pageCurrent >= countPage)
                return;


            pageCurrent++;

            changed_value_prev_next();

            update_status_changed();
        }


        // =========================================================
        // LAST PAGE
        // =========================================================

        private void BtnLastPage_Click(
            object sender,
            RoutedEventArgs e)
        {
            int total =
                sql.getCoutListHistory();


            if (total == 0)
                return;


            int stItem =
                total %
                total_item_page != 0
                    ? total %
                      total_item_page
                    : total_item_page;


            LHistory =
                sql.GetListHistory(
                    po: TxtSearch.Text,
                    length: total_item_page,
                    stIndex:
                        total - stItem
                );


            dgHistory.ItemsSource =
                LHistory;


            double kqPage =
                total /
                (total_item_page * 1.0);


            countPage =
                (int)Math.Ceiling(
                    kqPage
                );


            _dataBinding.currentPage =
                countPage;

            pageCurrent =
                countPage;


            _dataBinding.Curren_index_item =
                1 +
                total_item_page *
                (pageCurrent - 1);


            _dataBinding.To_index_item =
                total;


            update_status_changed();
        }


        // =========================================================
        // PAGE NUMBER
        // =========================================================

        private void BtnPageNum_Click(
            object sender,
            RoutedEventArgs e)
        {
        }


        // =========================================================
        // PAGE SIZE
        // =========================================================

        private void CmbPageSize_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (
                CmbPageSize.SelectedItem
                is ComboBoxItem item)
            {
                int.TryParse(
                    item.Content.ToString(),
                    out int valuecbm
                );


                if (valuecbm <= 0)
                    return;


                dgHistory.ItemsSource =
                    sql.GetListHistory(
                        po: TxtSearch.Text,
                        length: valuecbm
                    );


                int total =
                    sql.getCoutListHistory();


                _dataBinding.totalPage =
                    total;


                double kqPage =
                    total /
                    (valuecbm * 1.0);


                countPage =
                    (int)Math.Ceiling(
                        kqPage
                    );


                _dataBinding.sluong_page =
                    countPage;


                total_item_page =
                    valuecbm;


                _dataBinding.currentPage =
                    1;

                pageCurrent =
                    1;


                _dataBinding.Curren_index_item =
                    1;

                _dataBinding.To_index_item =
                    valuecbm;


                update_status_changed();
            }
        }


        // =========================================================
        // UPDATE PAGE BUTTON STATUS
        // =========================================================

        private void update_status_changed()
        {
            _dataBinding.SttBtn_next =
                pageCurrent <
                countPage;


            _dataBinding.SttBtn_prev =
                pageCurrent > 1;
        }


        // =========================================================
        // CHANGE PAGE
        // =========================================================

        private void changed_value_prev_next()
        {
            if (pageCurrent < 1)
                pageCurrent = 1;


            if (
                countPage > 0 &&
                pageCurrent > countPage)
            {
                pageCurrent =
                    countPage;
            }


            _dataBinding.currentPage =
                pageCurrent;


            LHistory =
                sql.GetListHistory(
                    po: TxtSearch.Text,
                    length: total_item_page,
                    stIndex:
                        (
                            pageCurrent - 1
                        ) *
                        total_item_page
                );


            dgHistory.ItemsSource =
                LHistory;


            _dataBinding.Curren_index_item =
                1 +
                (
                    total_item_page *
                    (pageCurrent - 1)
                );


            _dataBinding.To_index_item =
                total_item_page *
                pageCurrent;


            update_status_changed();
        }


        // =========================================================
        // NAVIGATION
        // =========================================================

        private void nav_dasboard_Click(
            object sender,
            RoutedEventArgs e)
        {
            MainDasboard.BringIntoView();

            StartBlink(
                MainDasboard
            );
        }


        private void nav_maneger_po_Click(
            object sender,
            RoutedEventArgs e)
        {
            Main_maneger_po.BringIntoView();

            StartBlink(total_scale);
            StartBlink(total_kg_scale);
            StartBlink(total_kg_success);
            StartBlink(total_kg_fail);
        }


        private void nav_history_scale_Click(
            object sender,
            RoutedEventArgs e)
        {
            Main_list_history.BringIntoView();

            StartBlink(
                Main_list_history
            );
        }


        private void nav_export_csv_Click(
            object sender,
            RoutedEventArgs e)
        {
            btn_export_csv.BringIntoView();

            StartBlink(
                btn_export_csv
            );
        }


        // =========================================================
        // WINDOW DRAG
        // =========================================================

        private void TitleBar_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            DragMove();
        }


        // =========================================================
        // WINDOW BUTTONS
        // =========================================================

        private void BtnClose_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }


        private void BtnMinimize_Click(
            object sender,
            RoutedEventArgs e)
        {
            WindowState =
                WindowState.Minimized;
        }


        private void BtnMaximize_Click(
            object sender,
            RoutedEventArgs e)
        {
            WindowState =
                WindowState ==
                WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
        }
    }
}