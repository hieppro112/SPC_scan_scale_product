using OpenCvSharp;
using scancode.Binding;
using scancode.Helper;
using scancode.Models;
using scancode.Services;
using scancode.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        // ==================== SERVICES ====================
        private CameraService camera;
        private portService _portService;
        private SQLService sql;
        private string jsonPath = "";

        // ==================== DATA ====================
        private string lastBarcode = "";
        private DataBinding _dataBinding = new DataBinding();
        private List<dataHistory> LHistory;
        private ProductData prData;
        private float kg_scale = 0;

        // ==================== PAGINATION ====================
        private int pageCurrent = 1;
        private int countPage = 0;
        private int total_item_page = 0;

        // ==================== UI ====================
        private Storyboard blinkStoryBoard;
        private MiniWindow miniWindow;
        private bool isCompactMode;

        // ==================== CAMERA DISPLAY BUFFER ====================
        /*
         * RẤT QUAN TRỌNG: Không cho Dispatcher xếp hàng hàng trăm frame.
         * Camera có thể tạo Frame1, Frame2, Frame3... liên tục,
         * nhưng WPF chỉ cần frame mới nhất.
         */
        private readonly object _displayLock = new object();
        private Mat _pendingDisplayFrame;
        private bool _displayScheduled;


        

        // ==================== CONSTRUCTOR ====================
        public MainWindow()
        {
            Console.WriteLine($"64-bit Process: {Environment.Is64BitProcess}");
            Console.WriteLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");

            // SERVICES
            sql = new SQLService();
            InitializeComponent();
            DataContext = _dataBinding;
            camera = new CameraService();
            _portService = new portService();

            // SCALE
            LHistory = new List<dataHistory>();
            _portService.DataReceived += Port_DataReceived;
            bool sttConnect = _portService.connect();

            // CAMERA EVENTS
            camera.FrameReceived += Camera_FrameReceived;
            camera.BarcodeReceived += Camera_BarcodeReceived;
            camera.GetCameraList(cbm_camera);

            // PRODUCT
            prData = new ProductData();

            // HISTORY
            //LHistory = sql.GetListHistory();
            //dgHistory.ItemsSource = LHistory;
            _dataBinding.totalList = 0; //LHistory.Count;
            _dataBinding.IsScaleConnected = sttConnect;

            // BLINK
            blinkStoryBoard = (Storyboard)FindResource("BlinkStoryboard");

            jsonPath = ReadJsonPath();
        }

        private async void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            // HISTORY
            LHistory = await sql.GetListHistory();
            dgHistory.ItemsSource = LHistory;

        }

        // ==================== WINDOW CLOSED ====================
        private async void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                // Không để frame camera tồn tại
                ClearPendingDisplayFrame();

                miniWindow?.Close();

                // Dừng camera
                if (camera != null)
                    await camera.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Window close error: " + ex.Message);
            }
        }

        // ==================== CHẾ ĐỘ THU NHỎ (GÓC MÀN HÌNH) ====================
        private void EnterCompactMode()
        {
            if (isCompactMode)
                return;

            isCompactMode = true;

            if (miniWindow == null)
            {
                miniWindow = new MiniWindow();
                miniWindow.RestoreRequested += ExitCompactMode;
            }

            miniWindow.SnapToBottomRight();
            miniWindow.Show();
            Hide();
        }

        private void ExitCompactMode()
        {
            if (!isCompactMode)
                return;

            isCompactMode = false;

            miniWindow?.Hide();
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // Bắt cả trường hợp Windows tự thu nhỏ (Win+Down, taskbar...) thay vì chỉ nút "─"
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && !isCompactMode)
            {
                WindowState = WindowState.Normal;
                EnterCompactMode();
            }
        }

        // ==================== CLEAR CAMERA BUFFER ====================
        private void ClearPendingDisplayFrame()
        {
            lock (_displayLock)
            {
                _pendingDisplayFrame?.Dispose();
                _pendingDisplayFrame = null;
                _displayScheduled = false;
            }
        }

        // ==================== CAMERA FRAME ====================
        private void Camera_FrameReceived(Mat frame)
        {
            if (frame == null || frame.Empty())
            {
                frame?.Dispose();
                return;
            }

            lock (_displayLock)
            {
                // Nếu WPF chưa kịp hiển thị frame trước thì bỏ frame đó, chỉ giữ frame mới nhất.
                _pendingDisplayFrame?.Dispose();
                _pendingDisplayFrame = frame;

                // Đã có callback UI đang chờ, không tạo thêm Dispatcher callback.
                if (_displayScheduled)
                    return;

                _displayScheduled = true;
            }

            Dispatcher.BeginInvoke(
                new Action(DisplayLatestFrame),
                System.Windows.Threading.DispatcherPriority.Render
            );
        }

        // ==================== DISPLAY LATEST CAMERA FRAME ====================
        private void DisplayLatestFrame()
        {
            Mat frame = null;

            lock (_displayLock)
            {
                frame = _pendingDisplayFrame;
                _pendingDisplayFrame = null;
                _displayScheduled = false;
            }

            if (frame == null)
                return;

            try
            {
                // MAT → BitmapSource
                var bitmap = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(frame);

                // Cho WPF sử dụng bitmap độc lập với thread camera.
                bitmap.Freeze();

                imgCamera.Source = bitmap;

                if (isCompactMode)
                    miniWindow?.UpdateFrame(bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Display camera error: " + ex.Message);
            }
            finally
            {
                frame.Dispose();
            }

            // Trong lúc UI xử lý frame này, camera có thể đã gửi frame mới. Nếu có → schedule lại.
            lock (_displayLock)
            {
                if (_pendingDisplayFrame != null && !_displayScheduled)
                {
                    _displayScheduled = true;
                    Dispatcher.BeginInvoke(
                        new Action(DisplayLatestFrame),
                        System.Windows.Threading.DispatcherPriority.Render
                    );
                }
            }
        }

        // ==================== BARCODE RECEIVED ====================
        private void Camera_BarcodeReceived(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;

            // Không xử lý cùng một barcode liên tục.
            if (code == lastBarcode)
                return;

            lastBarcode = code;

            // Không dùng Invoke vì nó block thread DetectionLoop.
            Dispatcher.BeginInvoke(new Action(() => OnBarcodeDetected(code, "")));
        }

        // ==================== SCALE DATA ====================
        private void Port_DataReceived(string data)
        {
            // Lọc lấy số (hỗ trợ cả số nguyên và số thập phân)
            string match = Regex.Match(data, @"\d+(\.\d+)?").Value;

            if (!string.IsNullOrEmpty(match))
            {
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    TxtKg.Text = match;

                    if (float.TryParse(match, out float weight))
                    {
                        kg_scale = weight;

                        // Chỉ ghi nhận khi có mã PO hợp lệ đang chờ
                        if (prData != null && !string.IsNullOrEmpty(prData.AUFNR))
                        {
                            await insertDataHistory();
                        }
                    }
                }));
            }
        }
        // ==================== MESSAGE ====================
        public static void messageLog(string s)
        {
            MessageBox.Show(s);
        }

        // ==================== BLINK ====================
        private void StartBlink(FrameworkElement element)
        {
            blinkStoryBoard.Begin(element, true);
        }

        // ==================== BARCODE PROCESS ====================
        public async Task OnBarcodeDetected(string code, string type)
        {
            Console.WriteLine("Barcode: " + code);

            try
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    txtBarcode.Text = "Po không hợp lệ !!!";
                    txtBarcode.Foreground = Brushes.Red;
                    return;
                }

                // ---- FORMAT 1 ----
                if (code.Split(',').Length > 2)
                {
                    string getPO = code.Split(',')[1].Trim();
                    prData = await sql.GetDataForPO(getPO);

                    if (prData != null)
                    {
                        txtBarcode.Text = getPO;
                        txtBarcode.Foreground = Brushes.Green;
                        _dataBinding.Barcode = getPO;
                        TxtScanTime.Text = DateTime.Now.ToString("HH:mm:ss");

                        txtPHCD.Text = prData.PHCD;
                        TxtPHTX.Text = prData.PHTX;
                        TxtPSTX.Text = prData.PSTX;
                        TxtQty.Text = prData.GAMNG.ToString();

                        _portService.RequestWeight();
                    }
                    else
                    {
                        txtBarcode.Text = "Po không hợp lệ !!!";
                        txtBarcode.Foreground = Brushes.Red;
                    }

                    return;
                }

                // ---- FORMAT 2 ----
                if (code.Length >= 11)
                {
                    prData = await sql.GetDataForPO(code);

                    if (prData != null)
                    {
                        txtBarcode.Text = code;
                        txtBarcode.Foreground = Brushes.Green;
                        _dataBinding.Barcode = code;
                        TxtScanTime.Text = DateTime.Now.ToString("HH:mm:ss");

                        _portService.RequestWeight();
                        //insertDataHistory();
                    }
                    else
                    {
                        txtBarcode.Text = "Po không hợp lệ !!!";
                        txtBarcode.Foreground = Brushes.Red;
                    }

                    return;
                }

                // ---- INVALID ----
                txtBarcode.Text = "Po không hợp lệ !!!";
                txtBarcode.Foreground = Brushes.Red;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Barcode process error: " + ex.Message);

                txtBarcode.Text = "Po không hợp lệ !!!";
                txtBarcode.Foreground = Brushes.Red;
                TxtScanTime.Text = DateTime.Now.ToString("HH:mm:ss");
            }
        }

        // ==================== RESET SCAN ====================
        private void btn_restart_Click(object sender, RoutedEventArgs e)
        {
            // Cho phép quét lại barcode cũ.
            lastBarcode = "";

            txtBarcode.Text = "-";
            TxtScanTime.Text = "-";
            TxtKg.Text = "0.00";
            kg_scale = 0;
            prData = new ProductData();
        }

        // ==================== CAMERA SELECTION ====================
        private async void cbm_camera_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbm_camera.SelectedIndex < 0)
                return;

            LoadingGrid.Visibility = Visibility.Visible;

            try
            {
                await camera.ChangeCamera(cbm_camera.SelectedIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể đổi camera:\n" + ex.Message);
            }
            finally
            {
                LoadingGrid.Visibility = Visibility.Collapsed;
            }
        }

        // ==================== STOP CAMERA ====================
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await camera.Stop();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể dừng camera:\n" + ex.Message);
            }
        }

        // ==================== START CAMERA ====================
        private async void btn_scan_continue_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await camera.Start(cbm_camera.SelectedIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể bật camera:\n" + ex.Message);
            }
        }

        // ==================== CONFIRM SCALE ====================
        private void btn_confirm_scale_Click(object sender, RoutedEventArgs e)
        {
            _portService.RequestWeight();
            insertDataHistory();
        }

        // ==================== INSERT HISTORY ====================
        private async Task insertDataHistory()
        {
            // Tránh NullReferenceException nếu chưa quét PO hợp lệ
            if (prData == null)
            {
                NotifyDialog.ShowWarning("Chưa có PO hợp lệ.");
                return;
            }

            var DataHistory = new dataHistory
            {
                AUFNR = prData.AUFNR,
                GAMNG = prData.GAMNG,
                PHCD = prData.PHCD,
                PHTX = prData.PHTX,
                PSTX = prData.PSTX,
                UPDDT = DateTime.Now,
                kg = kg_scale,
                NumWeight = prData.NumWeight,
                Qty = prData.Qty,
            };


            bool result = await sql.insertDataHistory(DataHistory);
            if (result)
            {
                //kiem tra can nang cua PO
                Check_Weight(kg_scale,DataHistory);

                if (isCompactMode)
                    miniWindow?.ShowSuccessToast($"Đã cân {kg_scale:0.###} kg");
                else
                    NotifyDialog.ShowSuccess("Thêm thành công");

                LHistory = await sql.GetListHistory();
                dgHistory.ItemsSource = LHistory;
                _dataBinding.totalList = LHistory.Count;

                prData = new ProductData();
                kg_scale = 0;
            }
            else
            {
                NotifyDialog.ShowWarning("Thêm thất bại");
            }
        }

        private void Check_Weight(double weight,dataHistory dathis)
        {
            double tb_kg = prData.NumWeight / prData.Qty;
            double scale = tb_kg * dathis.GAMNG;
            //so sanh so kg trong du lieu so voi khi cân
            if (scale != weight)
            {
                NotifyDialog.ShowWarning($"Số cân hiện tại:{dathis.kg}  bị lệch với master: {scale}");
                JsonCreate.SavaJsonErr(dathis,jsonPath);
            }
        }

        // ==================== HISTORY RESET ====================
        private void btn_restart_dgv_Click(object sender, RoutedEventArgs e)
        {
            update_status_changed();
            changed_value_prev_next();
        }

        // ==================== SEARCH ====================
        private async void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            LHistory = await sql.GetListHistory(TxtSearch.Text);
            dgHistory.ItemsSource = LHistory;
        }

        // ==================== FIRST PAGE ====================
        private async void BtnFirstPage_Click(object sender, RoutedEventArgs e)
        {
            LHistory = await sql.GetListHistory(po: TxtSearch.Text, length: total_item_page);
            dgHistory.ItemsSource = LHistory;

            _dataBinding.currentPage = 1;
            pageCurrent = 1;

            _dataBinding.Curren_index_item = 1;
            _dataBinding.To_index_item = total_item_page;

            update_status_changed();
        }

        // ==================== PREVIOUS PAGE ====================
        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (pageCurrent <= 1)
                return;

            pageCurrent--;
            changed_value_prev_next();
            update_status_changed();
        }

        // ==================== NEXT PAGE ====================
        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (pageCurrent >= countPage)
                return;

            pageCurrent++;
            changed_value_prev_next();
            update_status_changed();
        }

        // ==================== LAST PAGE ====================
        private async void BtnLastPage_Click(object sender, RoutedEventArgs e)
        {
            int total = sql.getCoutListHistory();
            if (total == 0)
                return;

            int stItem = total % total_item_page != 0
                ? total % total_item_page
                : total_item_page;

            LHistory = await sql.GetListHistory(
                po: TxtSearch.Text,
                length: total_item_page,
                stIndex: total - stItem
            );
            dgHistory.ItemsSource = LHistory;

            double kqPage = total / (total_item_page * 1.0);
            countPage = (int)Math.Ceiling(kqPage);

            _dataBinding.currentPage = countPage;
            pageCurrent = countPage;

            _dataBinding.Curren_index_item = 1 + total_item_page * (pageCurrent - 1);
            _dataBinding.To_index_item = total;

            update_status_changed();
        }

        // ==================== PAGE NUMBER ====================
        private void BtnPageNum_Click(object sender, RoutedEventArgs e)
        {
        }

        // ==================== PAGE SIZE ====================
        private async void CmbPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbPageSize.SelectedItem is ComboBoxItem item)
            {
                int.TryParse(item.Content.ToString(), out int valuecbm);
                if (valuecbm <= 0)
                    return;

                dgHistory.ItemsSource = await sql.GetListHistory(po: TxtSearch.Text, length: valuecbm);

                int total = sql.getCoutListHistory();
                _dataBinding.totalPage = total;

                double kqPage = total / (valuecbm * 1.0);
                countPage = (int)Math.Ceiling(kqPage);
                _dataBinding.sluong_page = countPage;

                total_item_page = valuecbm;

                _dataBinding.currentPage = 1;
                pageCurrent = 1;

                _dataBinding.Curren_index_item = 1;
                _dataBinding.To_index_item = valuecbm;

                update_status_changed();
            }
        }

        // ==================== UPDATE PAGE BUTTON STATUS ====================
        private void update_status_changed()
        {
            _dataBinding.SttBtn_next = pageCurrent < countPage;
            _dataBinding.SttBtn_prev = pageCurrent > 1;
        }

        // ==================== CHANGE PAGE ====================
        private async Task changed_value_prev_next()
        {
            if (pageCurrent < 1)
                pageCurrent = 1;

            if (countPage > 0 && pageCurrent > countPage)
                pageCurrent = countPage;

            _dataBinding.currentPage = pageCurrent;

            LHistory = await sql.GetListHistory(
                po: TxtSearch.Text,
                length: total_item_page,
                stIndex: (pageCurrent - 1) * total_item_page
            );
            dgHistory.ItemsSource = LHistory;

            _dataBinding.Curren_index_item = 1 + (total_item_page * (pageCurrent - 1));
            _dataBinding.To_index_item = total_item_page * pageCurrent;

            update_status_changed();
        }

        // ==================== NAVIGATION ====================
        private void nav_dasboard_Click(object sender, RoutedEventArgs e)
        {
            MainDasboard.BringIntoView();
            StartBlink(MainDasboard);
        }

        private void nav_history_scale_Click(object sender, RoutedEventArgs e)
        {
            Main_list_history.BringIntoView();
            StartBlink(Main_list_history);
        }

        private void nav_export_csv_Click(object sender, RoutedEventArgs e)
        {
            btn_export_csv.BringIntoView();
            StartBlink(btn_export_csv);
        }

        // ==================== WINDOW DRAG ====================
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        // ==================== WINDOW BUTTONS ====================
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            EnterCompactMode();
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void btn_config_Click(object sender, RoutedEventArgs e)
        {
            // Khởi tạo FolderBrowserDialog của WinForms
            using (var dialog_select_folder = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog_select_folder.Description = "Chọn thư mục lưu file JSON";
                dialog_select_folder.ShowNewFolderButton = true; // Cho phép tạo thư mục mới nếu muốn

                // Mở hộp thoại chọn thư mục
                System.Windows.Forms.DialogResult result = dialog_select_folder.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog_select_folder.SelectedPath))
                {
                    string selectedFolder = dialog_select_folder.SelectedPath;
                    string jsonPath = selectedFolder;

                    try
                    {
                        // 1.đường dẫn thư muc
                        string configFolderPath = @"C:\Config_packing";
                        string configFilePath = Path.Combine(configFolderPath, "config.txt");

                        // 2. Ki?m tra n?u thu m?c C:\Config_packing chua t?n t?i thì t?o m?i
                        if (!Directory.Exists(configFolderPath))
                        {
                            Directory.CreateDirectory(configFolderPath);
                        }

                        // 3. Ghi dè (WriteAllText) du?ng d?n jsonPath vào file config.txt v?i chu?n mã hóa UTF-8
                        File.WriteAllText(configFilePath, jsonPath, Encoding.UTF8);

                        MessageBox.Show($"Ðã lưu Config:\n{configFilePath}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Lỗi khi ghi file cấu hình: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        public string ReadJsonPath()
        {
            string configFilePath = @"C:\Config_packing\config.txt";

            if (File.Exists(configFilePath))
            {
                return File.ReadAllText(configFilePath, Encoding.UTF8).Trim();
            }

            return string.Empty; 
        }

        
    }
}
