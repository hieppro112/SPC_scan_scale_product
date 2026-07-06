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
            camera.Start(cbm_camera.SelectedIndex);
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
            LHistory = sql.GetListHistory();
            dgHistory.ItemsSource = LHistory ;
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            LHistory = sql.GetListHistory(TxtSearch.Text);
            dgHistory.ItemsSource = LHistory;
        }
        private void BtnFirstPage_Click(object sender, RoutedEventArgs e) {
            _ = (pageCurrent >= countPage) ? _dataBinding.SttBtn_next = false : _dataBinding.SttBtn_next = true;
            _ = (pageCurrent > 1) ? (_dataBinding.SttBtn_prev = true) : _dataBinding.SttBtn_prev = false;
            LHistory = sql.GetListHistory(po: TxtSearch.Text, length: total_item_page);//thay dooi gia tri load trang 
            dgHistory.ItemsSource = LHistory;
        }
        private void BtnPrevPage_Click(object sender, RoutedEventArgs e) {
            pageCurrent--;
            _dataBinding.currentPage = pageCurrent;
            //if (pageCurrent > 1)
            //{
            //    _dataBinding.SttBtn_prev = true;
            //}
            //else
            //{
            //    _dataBinding.SttBtn_prev = false;
            //}

            _ = (pageCurrent >= countPage) ? _dataBinding.SttBtn_next = false : _dataBinding.SttBtn_next = true;
            _ = (pageCurrent > 1) ? (_dataBinding.SttBtn_prev = true) : _dataBinding.SttBtn_prev = false;
            LHistory = sql.GetListHistory(po: TxtSearch.Text, length: total_item_page, stIndex: ((pageCurrent-1) * total_item_page ));//thay dooi gia tri load trang 
            dgHistory.ItemsSource = LHistory;
        }
        private void BtnNextPage_Click(object sender, RoutedEventArgs e) 
        {
            pageCurrent++;
            _dataBinding.currentPage = pageCurrent;
            //if (pageCurrent == countPage)
            //{
            //    _dataBinding.SttBtn_next = false;
            //}
            //else
            //{
            //    _dataBinding.SttBtn_next = true;

            //}

            //if (pageCurrent > 1)
            //{
            //    _dataBinding.SttBtn_prev = true;
            //    Console.WriteLine(_dataBinding.SttBtn_prev);
            //}
            //else
            //{
            //    _dataBinding.SttBtn_prev = false;
            //}
            _ = (pageCurrent>=countPage)? _dataBinding.SttBtn_next = false : _dataBinding.SttBtn_next = true;
            _ = (pageCurrent > 1) ? (_dataBinding.SttBtn_prev = true) : _dataBinding.SttBtn_prev = false;
            LHistory = sql.GetListHistory(po: TxtSearch.Text, length: total_item_page, stIndex: ((pageCurrent - 1) * total_item_page));//thay dooi gia tri load trang 
            dgHistory.ItemsSource = LHistory;

            TxtPageFrom.Text = (total_item_page * (pageCurrent - 1)).ToString();
            TxtPageTo.Text = (total_item_page * pageCurrent).ToString();
        }
        private void BtnLastPage_Click(object sender, RoutedEventArgs e) {
            _ = (pageCurrent >= countPage) ? _dataBinding.SttBtn_next = false : _dataBinding.SttBtn_next = true;
            _ = (pageCurrent > 1) ? (_dataBinding.SttBtn_prev = true) : _dataBinding.SttBtn_prev = false;
            LHistory = sql.GetListHistory(po: TxtSearch.Text, length: total_item_page,stIndex:(sql.getCoutListHistory()-total_item_page));//thay dooi gia tri load trang 
            dgHistory.ItemsSource = LHistory;
        }
        private void BtnPageNum_Click(object sender, RoutedEventArgs e) {
        }
        private void CmbPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e) 
        {
            if(CmbPageSize.SelectedItem is ComboBoxItem item)
            {
                
                Console.WriteLine("cbm: " + cbm_camera.SelectedItem);
                int.TryParse(item.Content.ToString(), out int valuecbm);
                dgHistory.ItemsSource = sql.GetListHistory(po: TxtSearch.Text, length: valuecbm);
                TxtPageFrom.Text = item.Content.ToString();
               _dataBinding.totalPage =  sql.getCoutListHistory();
                double kqPage = sql.getCoutListHistory() / (valuecbm *1.0);
                Console.WriteLine((int)Math.Ceiling(kqPage));
                countPage = (int)Math.Ceiling(kqPage);
                _dataBinding.sluong_page = countPage;
                total_item_page = valuecbm;
                _ = (pageCurrent >= countPage) ? _dataBinding.SttBtn_next = false : _dataBinding.SttBtn_next = true;
                _ = (pageCurrent > 1) ? (_dataBinding.SttBtn_prev = true) : _dataBinding.SttBtn_prev = false;
               
            }
            
        }
    }
}
