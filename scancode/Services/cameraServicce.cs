using AForge.Video.DirectShow;
using OpenCvSharp;
using scancode.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace scancode.Services
{

    public class CameraService
    {
        private VideoCapture capture;
        private volatile bool running;

        public event Action<Mat> FrameReceived;
        public event Action<string> BarcodeReceived;

        private FilterInfoCollection cameras;
        private BarcodeService barcodeService;

        private readonly object _frameLock = new object();
        private Mat latestFrame;

        private readonly object _rectLock = new object();
        private OpenCvSharp.Rect? lastRect = null;
        private DateTime lastDetectTime = DateTime.MinValue;
        private const int KeepRectMs = 500;

        private readonly SemaphoreSlim _changeLock = new SemaphoreSlim(1, 1);

        private Thread cameraThread;        //  Dùng Thread thay vì Task
        private Task barcodeTask;
        private CancellationTokenSource cts;

        private string barcodeLast;

        public async Task Start(int indexCamera)
        {
            cts = new CancellationTokenSource();
            running = true;

            //  Tạo STA thread riêng cho camera
            var tcs = new TaskCompletionSource<bool>();

            cameraThread = new Thread(() =>
            {
                try
                {
                    // Khởi tạo và dùng capture trên CÙNG 1 STA thread
                    capture = new VideoCapture(indexCamera);
                    barcodeService = barcodeService ?? new BarcodeService();

                    capture.Set(VideoCaptureProperties.FrameWidth, 960);
                    capture.Set(VideoCaptureProperties.FrameHeight, 540);
                    capture.Set(VideoCaptureProperties.AutoFocus, 1);
                    capture.Set(VideoCaptureProperties.Focus, 50);

                    if (!capture.IsOpened())
                    {
                        tcs.SetResult(false);
                        return;
                    }

                    tcs.SetResult(true); //  Báo hiệu Start() đã xong

                    // Camera loop chạy ngay trên thread này
                    CameraLoop(cts.Token);
                }
                catch (Exception ex)
                {
                    if (!tcs.Task.IsCompleted)
                        tcs.SetException(ex);
                }
                finally
                {
                    // Dispose trên cùng thread đã tạo ra nó
                    capture?.Release();
                    capture?.Dispose();
                    capture = null;
                }
            });

            cameraThread.SetApartmentState(ApartmentState.STA); // Quan trọng!
            cameraThread.IsBackground = true;
            cameraThread.Name = "CameraSTAThread";
            cameraThread.Start();

            bool opened = await tcs.Task;
            if (!opened)
                throw new Exception("Không thể mở camera.");

            barcodeTask = BarcodeLoop(cts.Token);
        }

        public async Task Stop()
        {
            running = false;
            cts?.Cancel();

            //  Chờ STA thread kết thúc (không dùng await vì là Thread)
            await Task.Run(() => cameraThread?.Join(TimeSpan.FromSeconds(5)));

            try
            {
                if (barcodeTask != null)
                    await barcodeTask;
            }
            catch (OperationCanceledException) { }

            lock (_frameLock)
            {
                latestFrame?.Dispose();
                latestFrame = null;
            }

            cts?.Dispose();
            cts = null;
            cameraThread = null;
            barcodeTask = null;
        }

        public async Task ChangeCamera(int indexCamera)
        {
            await _changeLock.WaitAsync();
            try
            {
                await Stop();
                await Start(indexCamera);
            }
            finally
            {
                _changeLock.Release();
            }
        }

        //  Đây là method thường (không phải async) — chạy blocking trên STA thread
        private void CameraLoop(CancellationToken token)
        {
             var frame = new Mat();

            while (!token.IsCancellationRequested && running)
            {
                try
                {
                    capture.Read(frame); //  Gọi trên STA thread → không còn COM conflict

                    if (!frame.Empty())
                    {
                        Mat frameToDisplay;

                        lock (_frameLock)
                        {
                            DrawRectangle(frame);
                            latestFrame?.Dispose();
                            latestFrame = frame.Clone();
                            frameToDisplay = frame.Clone();
                        }

                        FrameReceived?.Invoke(frameToDisplay);
                    }

                    // Thay Task.Delay bằng Thread.Sleep vì đây là STA thread
                    Thread.Sleep(30);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        private async Task BarcodeLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Mat frame = null;

                lock (_frameLock)
                {
                    if (latestFrame != null)
                        frame = latestFrame.Clone();
                }

                if (frame != null)
                {
                    try
                    {
                        var result = barcodeService.Decode(frame);

                        if (result != null && result.barcode!=barcodeLast)
                        {
                            lock (_rectLock)
                            {
                                lastRect = result.rect;
                                lastDetectTime = DateTime.Now;
                            }
                            BarcodeReceived?.Invoke(result.barcode);
                            barcodeLast = result.barcode;
                        }
                    }
                    catch (Exception) { }
                    finally
                    {
                        frame.Dispose();
                    }
                }

                try
                {
                    await Task.Delay(30, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void DrawRectangle(Mat frame)
        {
            OpenCvSharp.Rect? rect = null;

            lock (_rectLock)
            {
                if (lastRect.HasValue &&
                    (DateTime.Now - lastDetectTime).TotalMilliseconds < KeepRectMs)
                    rect = lastRect;
            }

            if (!rect.HasValue) return;

            Cv2.Rectangle(frame, rect.Value, Scalar.Lime, 3);
        }

        public void GetCameraList(ComboBox cbm)
        {
            cbm.Items.Clear();
            cameras = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo i in cameras)
                cbm.Items.Add(i.Name);
        }
    }
}
