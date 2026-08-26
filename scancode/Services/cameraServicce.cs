using AForge.Video.DirectShow;
using OpenCvSharp;
using scancode.Helper;
using scancode.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace scancode.Services
{
    public class CameraService
    {
        // =========================================================
        // CAMERA
        // =========================================================

        private VideoCapture capture;

        private volatile bool running;

        private Thread cameraThread;


        // =========================================================
        // TOKEN
        // =========================================================

        private CancellationTokenSource cts;


        // =========================================================
        // CAMERA CHANGE LOCK
        // =========================================================

        private readonly SemaphoreSlim _changeLock =
            new SemaphoreSlim(1, 1);


        // =========================================================
        // FRAME BUFFER
        // =========================================================

        private readonly object _frameLock =
            new object();

        private Mat latestFrame;


        // =========================================================
        // SERVICES
        // =========================================================

        private BarcodeService barcodeService;

        private YoloService yoloService;


        // =========================================================
        // TASK
        // =========================================================

        private Task previewTask;

        private Task detectionTask;


        // =========================================================
        // EVENTS
        // =========================================================

        public event Action<Mat> FrameReceived;

        public event Action<string> BarcodeReceived;


        // =========================================================
        // YOLO RECTANGLE
        // =========================================================

        private readonly object _rectLock =
            new object();

        private OpenCvSharp.Rect? lastRect;

        private DateTime lastDetectTime =
            DateTime.MinValue;


        // Rectangle giữ lại trên màn hình
        private const int KeepRectMs = 500;


        // =========================================================
        // BARCODE CACHE
        // =========================================================

        private string barcodeLast;


        // =========================================================
        // CAMERA LIST
        // =========================================================

        private FilterInfoCollection cameras;


        // =========================================================
        // START
        // =========================================================

        public async Task Start(
            int indexCamera)
        {
            if (running)
                return;


            running = true;


            cts =
                new CancellationTokenSource();


            // =====================================================
            // SERVICES
            // =====================================================

            if (barcodeService == null)
            {
                barcodeService =
                    new BarcodeService();
            }


            if (yoloService == null)
            {
                string modelPath =
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Models",
                        "best.onnx"
                    );


                if (!File.Exists(modelPath))
                {
                    running = false;

                    cts.Dispose();
                    cts = null;

                    throw new FileNotFoundException(
                        "Không tìm thấy best.onnx",
                        modelPath
                    );
                }


                yoloService =
                    new YoloService(
                        modelPath
                    );


                yoloService.PrintModelInfo();
            }


            // =====================================================
            // TCS
            // =====================================================

            var tcs =
                new TaskCompletionSource<bool>();


            // =====================================================
            // CAMERA THREAD
            // =====================================================

            cameraThread =
                new Thread(() =>
                {
                    try
                    {
                        // =============================================
                        // OPEN CAMERA
                        // =============================================

                        capture =
                            new VideoCapture(
                                indexCamera
                            );


                        // =============================================
                        // CAMERA RESOLUTION
                        // =============================================

                        capture.Set(
                            VideoCaptureProperties.FrameWidth,
                            960
                        );


                        capture.Set(
                            VideoCaptureProperties.FrameHeight,
                            540
                        );


                        // =============================================
                        // CAMERA FPS
                        // =============================================

                        capture.Set(
                            VideoCaptureProperties.Fps,
                            10
                        );


                        // =============================================
                        // AUTO FOCUS
                        // =============================================

                        capture.Set(
                            VideoCaptureProperties.AutoFocus,
                            1
                        );


                        // =============================================
                        // CHECK
                        // =============================================

                        if (!capture.IsOpened())
                        {
                            if (
                                !tcs.Task.IsCompleted)
                            {
                                tcs.SetResult(
                                    false
                                );
                            }

                            return;
                        }


                        // =============================================
                        // PRINT ACTUAL SETTINGS
                        // =============================================

                        Debug.WriteLine(
                            "Camera Width: " +
                            capture.Get(
                                VideoCaptureProperties.FrameWidth
                            )
                        );


                        Debug.WriteLine(
                            "Camera Height: " +
                            capture.Get(
                                VideoCaptureProperties.FrameHeight
                            )
                        );


                        Debug.WriteLine(
                            "Camera FPS: " +
                            capture.Get(
                                VideoCaptureProperties.Fps
                            )
                        );


                        // =============================================
                        // START OK
                        // =============================================

                        if (
                            !tcs.Task.IsCompleted)
                        {
                            tcs.SetResult(
                                true
                            );
                        }


                        // =============================================
                        // CAMERA LOOP
                        // =============================================

                        CameraLoop(
                            cts.Token
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            ex.ToString()
                        );


                        if (
                            !tcs.Task.IsCompleted)
                        {
                            tcs.SetException(
                                ex
                            );
                        }
                    }
                    finally
                    {
                        // =============================================
                        // RELEASE CAMERA
                        // =============================================

                        try
                        {
                            capture?.Release();
                        }
                        catch
                        {
                        }


                        try
                        {
                            capture?.Dispose();
                        }
                        catch
                        {
                        }


                        capture = null;
                    }
                });


            // =====================================================
            // STA
            // =====================================================

            cameraThread.SetApartmentState(
                ApartmentState.STA
            );


            cameraThread.IsBackground =
                true;


            cameraThread.Name =
                "CameraSTAThread";


            cameraThread.Start();


            // =====================================================
            // WAIT CAMERA
            // =====================================================

            bool opened =
                await tcs.Task;


            if (!opened)
            {
                running = false;

                cts.Cancel();

                cts.Dispose();

                cts = null;

                throw new Exception(
                    "Không thể mở camera."
                );
            }


            // =====================================================
            // START PREVIEW
            // =====================================================

            previewTask =
                PreviewLoop(
                    cts.Token
                );


            // =====================================================
            // START YOLO
            // =====================================================

            detectionTask =
                DetectionLoop(
                    cts.Token
                );
        }


        // =========================================================
        // CAMERA LOOP
        // =========================================================

        private void CameraLoop(
            CancellationToken token)
        {
            using (var frame = new Mat())
            {
                while (
                    running &&
                    !token.IsCancellationRequested)
                {
                    try
                    {
                        if (
                            capture == null)
                        {
                            break;
                        }


                        if (
                            !capture.Read(frame) ||
                            frame.Empty())
                        {
                            continue;
                        }
                        //Cv2.Flip(
                        //    frame,
                        //    frame,
                        //    FlipMode.Y
                        //);

                        // =============================================
                        // CHỈ GIỮ FRAME MỚI NHẤT
                        // =============================================

                        lock (_frameLock)
                        {
                            latestFrame?.Dispose();

                            latestFrame =
                                frame.Clone();
                        }
                    }
                    catch (
                        OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "CameraLoop: " +
                            ex.Message
                        );

                        break;
                    }
                }
            }
        }


        // =========================================================
        // PREVIEW LOOP
        // =========================================================
        //
        // ~10 FPS
        //
        // =========================================================

        private async Task PreviewLoop(
            CancellationToken token)
        {
            const int PreviewDelay =
                100;


            while (
                running &&
                !token.IsCancellationRequested)
            {
                Mat frame = null;


                try
                {
                    // =============================================
                    // COPY LATEST FRAME
                    // =============================================

                    lock (_frameLock)
                    {
                        if (
                            latestFrame != null)
                        {
                            frame =
                                latestFrame.Clone();
                        }
                    }


                    if (frame != null)
                    {
                        // =============================================
                        // DRAW YOLO RECT
                        // =============================================

                        DrawRectangle(
                            frame
                        );


                        // =============================================
                        // RESIZE PREVIEW
                        // =============================================

                        using (
                            var display =
                                new Mat())
                        {
                            Cv2.Resize(
                                frame,
                                display,
                                new OpenCvSharp.Size(
                                    640,
                                    360
                                ),
                                0,
                                0,
                                InterpolationFlags.Area
                            );


                            // =============================================
                            // SEND WPF
                            // =============================================

                            var result =
                                display.Clone();


                            try
                            {
                                FrameReceived?.Invoke(
                                    result
                                );
                            }
                            catch
                            {
                                result.Dispose();
                            }
                        }
                    }


                    await Task.Delay(
                        PreviewDelay,
                        token
                    );
                }
                catch (
                    OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "PreviewLoop: " +
                        ex.Message
                    );
                }
                finally
                {
                    frame?.Dispose();
                }
            }
        }


        // =========================================================
        // YOLO + BARCODE
        // =========================================================
        //
        // ~3 FPS
        //
        // =========================================================

        private async Task DetectionLoop(
            CancellationToken token)
        {
            const int DetectionDelay =
                300;


            while (
                running &&
                !token.IsCancellationRequested)
            {
                Mat frame = null;


                try
                {
                    // =============================================
                    // GET LATEST FRAME
                    // =============================================

                    lock (_frameLock)
                    {
                        if (
                            latestFrame != null)
                        {
                            frame =
                                latestFrame.Clone();
                        }
                    }


                    if (frame != null)
                    {
                        // =============================================
                        // YOLO
                        // =============================================

                        YoloDetection detection =
                            yoloService.Detect(
                                frame
                            );


                        // =============================================
                        // BARCODE FOUND
                        // =============================================

                        if (detection != null)
                        {
                            OpenCvSharp.Rect rect =
                                detection.Rect;


                            // =============================================
                            // SAVE RECTANGLE
                            // =============================================

                            lock (_rectLock)
                            {
                                lastRect =
                                    rect;

                                lastDetectTime =
                                    DateTime.Now;
                            }


                            // =============================================
                            // CROP BARCODE
                            // =============================================

                            using (
                                var roi =
                                    new Mat(
                                        frame,
                                        rect
                                    ))
                            {
                                // =============================================
                                // ZXING
                                // =============================================

                                var result =
                                    barcodeService.Decode(
                                        roi
                                    );


                                if (
                                    result != null &&
                                    !string.IsNullOrWhiteSpace(
                                        result.barcode
                                    ))
                                {
                                    // =============================================
                                    // NEW BARCODE
                                    // =============================================

                                    if (
                                        result.barcode !=
                                        barcodeLast)
                                    {
                                        barcodeLast =
                                            result.barcode;


                                        BarcodeReceived?.Invoke(
                                            result.barcode
                                        );
                                    }
                                }
                            }
                        }
                    }


                    await Task.Delay(
                        DetectionDelay,
                        token
                    );
                }
                catch (
                    OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "DetectionLoop: " +
                        ex.Message
                    );
                }
                finally
                {
                    frame?.Dispose();
                }
            }
        }


        // =========================================================
        // DRAW RECTANGLE
        // =========================================================

        private void DrawRectangle(
            Mat frame)
        {
            OpenCvSharp.Rect? rect =
                null;


            lock (_rectLock)
            {
                if (
                    lastRect.HasValue &&
                    (
                        DateTime.Now -
                        lastDetectTime
                    ).TotalMilliseconds
                    < KeepRectMs)
                {
                    rect =
                        lastRect;
                }
            }


            if (!rect.HasValue)
                return;


            Cv2.Rectangle(
                frame,
                rect.Value,
                Scalar.Lime,
                3
            );
        }


        // =========================================================
        // STOP
        // =========================================================

        public async Task Stop()
        {
            running = false;


            // =============================================
            // CANCEL
            // =============================================

            cts?.Cancel();


            // =============================================
            // WAIT CAMERA THREAD
            // =============================================

            if (cameraThread != null)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        cameraThread.Join(
                            TimeSpan.FromSeconds(5)
                        );
                    }
                    catch
                    {
                    }
                });
            }


            // =============================================
            // WAIT PREVIEW
            // =============================================

            try
            {
                if (
                    previewTask != null)
                {
                    await previewTask;
                }
            }
            catch (
                OperationCanceledException)
            {
            }


            // =============================================
            // WAIT DETECTION
            // =============================================

            try
            {
                if (
                    detectionTask != null)
                {
                    await detectionTask;
                }
            }
            catch (
                OperationCanceledException)
            {
            }


            // =============================================
            // CLEAR FRAME
            // =============================================

            lock (_frameLock)
            {
                latestFrame?.Dispose();

                latestFrame = null;
            }


            // =============================================
            // CLEAR RECT
            // =============================================

            lock (_rectLock)
            {
                lastRect = null;

                lastDetectTime =
                    DateTime.MinValue;
            }


            // =============================================
            // RESET BARCODE
            // =============================================

            barcodeLast = null;


            // =============================================
            // CLEAR CTS
            // =============================================

            cts?.Dispose();

            cts = null;


            cameraThread = null;

            previewTask = null;

            detectionTask = null;
        }


        // =========================================================
        // CHANGE CAMERA
        // =========================================================

        public async Task ChangeCamera(
            int indexCamera)
        {
            await _changeLock.WaitAsync();

            try
            {
                await Stop();

                await Start(
                    indexCamera
                );
            }
            finally
            {
                _changeLock.Release();
            }
        }


        // =========================================================
        // CAMERA LIST
        // =========================================================

        public void GetCameraList(
            ComboBox cbm)
        {
            cbm.Items.Clear();


            cameras =
                new FilterInfoCollection(
                    FilterCategory.VideoInputDevice
                );


            foreach (
                FilterInfo camera
                in cameras)
            {
                cbm.Items.Add(
                    camera.Name
                );
            }
        }


        // =========================================================
        // DISPOSE
        // =========================================================

        public async Task DisposeAsync()
        {
            try
            {
                await Stop();
            }
            catch
            {
            }


            try
            {
                yoloService?.Dispose();
            }
            catch
            {
            }


            yoloService = null;


            try
            {
                _changeLock.Dispose();
            }
            catch
            {
            }
        }
    }
}