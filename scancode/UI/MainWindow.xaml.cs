using OpenCvSharp;
using scancode.Binding;
using scancode.Models;
using scancode.Services;
using scancode.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
//using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace scancode
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        CameraService camera;
        BarcodeService barcode;
        portService _portService;
        SQLService sql;
        private string lastBarcode = "";

        //dataBinding
        private DataBinding _dataBinding = new DataBinding();

        List<dataHistory> LHistory;

        ProductData prData;

        float kg_scale = 0;

        //phan trang 
        private int pageCurrent = 1;
        private int countPage = 0;
        private int total_item_page = 0;

        private Storyboard blinkStoryBoard;
        public MainWindow()
        {
            sql = new SQLService();
            InitializeComponent();
            DataContext = _dataBinding;
            camera = new CameraService();
            barcode = new BarcodeService();
            _portService = new portService();
            LHistory = new List<dataHistory>();
            _portService.DataReceived += Port_DataReceived;
            bool sttConnect = _portService.connect();

            camera.FrameReceived += Camera_FrameReceived;
            camera.BarcodeReceived += Camera_BarcodeReceived;
            camera.GetCameraList(cbm_camera);

            prData = new ProductData();
            LHistory = sql.GetListHistory();
            dgHistory.ItemsSource = LHistory;
            _dataBinding.totalList = LHistory.Count;
            _dataBinding.IsScaleConnected = sttConnect;

            //khoi tao hieu ung blink 
            blinkStoryBoard = (Storyboard)FindResource("BlinkStoryboard");
            
        }

        private void StartBlink(FrameworkElement element)
        {
            blinkStoryBoard.Begin(element,true);
        }
        
        private void Port_DataReceived(string data)
        {
            string s = "";
            foreach (char c in data)
            {
                try
                    //int.TryParse(c.ToString(), out int a);
                {
                    int a = int.Parse(c.ToString());
                    if (a % 1 == 0)
                    {
                        s += a.ToString();
                        kg_scale = (kg_scale * 10) + a;
                    }
                }
                catch
                {
                    continue;
                }
            }
            Dispatcher.Invoke(() =>
            {
                //Console.WriteLine(s);
                TxtKg.Text = s;
                insertDataHistory();
            });
        }

        public static void messageLog(string s)
        {
            MessageBox.Show(s);
        }

        private void Camera_FrameReceived(Mat frame)
        {
            // Hiển thị camera - luôn chạy
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imgCamera.Source = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(frame);
                frame.Dispose();
            }));
        }

        private void Camera_BarcodeReceived(string code)
        {
            if (code == lastBarcode) return;

            Dispatcher.Invoke(()=>
            OnBarcodeDetected(code,""));
        }

        // Kéo cửa sổ
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) => DragMove();

        // Nút cửa sổ
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        // Gọi hàm này mỗi khi quét được mã
        public void OnBarcodeDetected(string code, string type)
        {
            Console.WriteLine("new dataa");
            try
            {
                if (code.Split(',').Length > 2)
                {
                    string getPO = code.Split(',')[1].Trim().ToString();
                    prData = sql.GetDataForPO(getPO);
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
                        //TxtPOProduct.Text = prData.PHCD;
                    }
                    else
                    {
                        txtBarcode.Text = "Po không hợp lệ !!!";
                        txtBarcode.Foreground = Brushes.Red;
                    }
                }
                else if (code.Length >= 11)
                {
                    prData = sql.GetDataForPO(code);

                    if (prData != null)
                    {
                        txtBarcode.Text = code;
                        txtBarcode.Foreground = Brushes.Green;
                        _dataBinding.Barcode = code;
                        TxtScanTime.Text = DateTime.Now.ToString("HH:mm:ss");
                    }

                }
                else
                {
                    txtBarcode.Text = "Po không hợp lệ !!!";
                    txtBarcode.Foreground = Brushes.Red;
                }

            }
            catch
            {
                txtBarcode.Text = "Po không hợp lệ !!!";
                txtBarcode.Foreground = Brushes.Red;
                TxtScanTime.Text = DateTime.Now.ToString("HH:mm:ss");
            }

        }

        private void btn_restart_Click(object sender, RoutedEventArgs e)
        {
            txtBarcode.Text = "-";
            TxtScanTime.Text = "-";
            TxtKg.Text = "0.00";
        }

        private async void cbm_camera_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadingGrid.Visibility = Visibility.Visible;
            await camera.ChangeCamera(cbm_camera.SelectedIndex);
            LoadingGrid.Visibility = Visibility.Collapsed;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            _ = camera.Stop();
        }

        private void btn_scan_continue_Click(object sender, RoutedEventArgs e)
        {
            _ = camera.Start(cbm_camera.SelectedIndex);
        }

        private void btn_confirm_scale_Click(object sender, RoutedEventArgs e)
        {
            insertDataHistory();
        }

        private void insertDataHistory()
        {
            bool result = sql.insertDataHistory(new dataHistory
            {
                AUFNR = prData.AUFNR,
                GAMNG = prData.GAMNG,
                PHCD = prData.PHCD,
                PHTX = prData.PHTX,
                PSTX = prData.PSTX,
                UPDDT = DateTime.Now,
                kg = kg_scale
            });
            

            if (result)
            {
                NotifyDialog.ShowSuccess("Thêm thành công");
                LHistory = sql.GetListHistory();
                dgHistory.ItemsSource = LHistory;
                prData = new ProductData();
            }
            else
            {
                NotifyDialog.ShowWarning("Thêm thất bại");
            }

        }

        private void btn_restart_dgv_Click(object sender, RoutedEventArgs e)
        {
            update_status_changed();
            changed_value_prev_next();

            //LHistory = sql.GetListHistory(po: TxtSearch.Text, length: total_item_page);
            //dgHistory.ItemsSource = LHistory;
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            LHistory = sql.GetListHistory(TxtSearch.Text);
            dgHistory.ItemsSource = LHistory;
        }
        private void BtnFirstPage_Click(object sender, RoutedEventArgs e) {
            LHistory = sql.GetListHistory(po: TxtSearch.Text, length: total_item_page);//thay dooi gia tri load trang 
            dgHistory.ItemsSource = LHistory;

            _dataBinding.currentPage = 1;
            pageCurrent = _dataBinding.currentPage;
            _dataBinding.Curren_index_item = 1;
            _dataBinding.To_index_item = (_dataBinding.Curren_index_item * total_item_page);

            update_status_changed();
        }
        private void BtnPrevPage_Click(object sender, RoutedEventArgs e) {
            pageCurrent--;
            changed_value_prev_next();
            update_status_changed();
        }
        private void BtnNextPage_Click(object sender, RoutedEventArgs e) 
        {
            pageCurrent++;
            changed_value_prev_next();
            update_status_changed();
        }
        private void BtnLastPage_Click(object sender, RoutedEventArgs e) {
            int stItem = (sql.getCoutListHistory() % total_item_page) != 0 ? ((sql.getCoutListHistory() % total_item_page)) : total_item_page;
            LHistory = sql.GetListHistory(po: TxtSearch.Text, length: total_item_page,stIndex:(sql.getCoutListHistory()- stItem));//thay dooi gia tri load trang 
            dgHistory.ItemsSource = LHistory;

            double kqPage = sql.getCoutListHistory() / (total_item_page * 1.0);
            countPage = (int)Math.Ceiling(kqPage);
            _dataBinding.currentPage = (int)countPage;
            pageCurrent = _dataBinding.currentPage;
            _dataBinding.Curren_index_item = 1+total_item_page*(pageCurrent-1);
            _dataBinding.To_index_item = sql.getCoutListHistory();
            update_status_changed();

        }
        private void BtnPageNum_Click(object sender, RoutedEventArgs e) {
        }
        private void CmbPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e) 
        {
            if(CmbPageSize.SelectedItem is ComboBoxItem item)
            {
                int.TryParse(item.Content.ToString(), out int valuecbm);
                dgHistory.ItemsSource = sql.GetListHistory(po: TxtSearch.Text, length: valuecbm);
                //TxtPageFrom.Text = item.Content.ToString();
               _dataBinding.totalPage =  sql.getCoutListHistory();
                double kqPage = sql.getCoutListHistory() / (valuecbm *1.0);
                countPage = (int)Math.Ceiling(kqPage);
                _dataBinding.sluong_page = countPage;
                total_item_page = valuecbm;
                _dataBinding.currentPage = 1;
                pageCurrent = _dataBinding.currentPage;
                _dataBinding.Curren_index_item = 1;
                _dataBinding.To_index_item = (_dataBinding.Curren_index_item*total_item_page);
                update_status_changed();
            }
            
        }

        private void update_status_changed()
        {
            _ = (pageCurrent >= countPage) ? _dataBinding.SttBtn_next = false : _dataBinding.SttBtn_next = true;
            _ = (pageCurrent > 1) ? (_dataBinding.SttBtn_prev = true) : _dataBinding.SttBtn_prev = false;
        }
        private void changed_value_prev_next()
        {
            _dataBinding.currentPage = pageCurrent;
            LHistory = sql.GetListHistory(po: TxtSearch.Text, length: total_item_page, stIndex: ((pageCurrent - 1) * total_item_page));//thay dooi gia tri load trang 
            dgHistory.ItemsSource = LHistory;

            _dataBinding.Curren_index_item = 1+(total_item_page * (pageCurrent - 1));
            _dataBinding.To_index_item = (total_item_page * pageCurrent);
        }

        private void nav_dasboard_Click(object sender, RoutedEventArgs e)
        {
            MainDasboard.BringIntoView();
            StartBlink(MainDasboard);
        }

        private void nav_maneger_po_Click(object sender, RoutedEventArgs e)
        {
            Main_maneger_po.BringIntoView();
            StartBlink(total_scale);
            StartBlink(total_kg_scale);
            StartBlink(total_kg_success);
            StartBlink(total_kg_fail);

            //StartBlink(Main_maneger_po);
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
    }
}
