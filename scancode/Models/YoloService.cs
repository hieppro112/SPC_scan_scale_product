using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace scancode.Models
{
    public class YoloService : IDisposable
    {
        private readonly InferenceSession _session;

        private const int InputWidth = 640;
        private const int InputHeight = 640;

        // Ngưỡng tin cậy (Hạ xuống 0.35f để dễ bắt QR Code hơn nếu ảnh bị mờ nhẹ)
        private const float ConfidenceThreshold = 0.15f;

        // Buffer tái sử dụng mỗi frame thay vì cấp phát mới (giảm áp lực GC trong vòng lặp detect liên tục)
        private readonly Mat _letterboxScratch = new Mat(InputHeight, InputWidth, MatType.CV_8UC3);
        private readonly Mat _resizedScratch = new Mat();
        private readonly Mat _rgbScratch = new Mat();
        private readonly float[] _tensorBuffer = new float[3 * InputWidth * InputHeight];

        public YoloService(string modelPath)
        {
            try
            {
                var options = new SessionOptions();
                // Giới hạn số thread chạy trên CPU để tối ưu hiệu năng ứng dụng WPF
                options.IntraOpNumThreads = 2;
                options.InterOpNumThreads = 1;

                _session = new InferenceSession(modelPath, options);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "ONNX Runtime ERROR");
                throw;
            }
        }

        // =========================================================
        // MODEL INFO
        // =========================================================
        public void PrintModelInfo()
        {
            if (_session == null) return;

            foreach (var input in _session.InputMetadata)
            {
                Console.WriteLine(
                    $"Input: {input.Key} | " +
                    $"Type: {input.Value.ElementType} | " +
                    $"Dimensions: {string.Join(", ", input.Value.Dimensions)}"
                );
            }

            foreach (var output in _session.OutputMetadata)
            {
                Console.WriteLine(
                    $"Output: {output.Key} | " +
                    $"Type: {output.Value.ElementType} | " +
                    $"Dimensions: {string.Join(", ", output.Value.Dimensions)}"
                );
            }
        }

        // =========================================================
        // DETECT (Trả về danh sách tất cả mã tìm được: Barcode + QR)
        // =========================================================
        public List<YoloDetection> DetectAll(Mat frame)
        {
            var resultsList = new List<YoloDetection>();
            if (frame == null || frame.Empty())
                return resultsList;

            // 1. LETTERBOX (Giữ nguyên tỷ lệ ảnh, chèn viền xám 114) — ghi vào buffer tái sử dụng
            Letterbox(frame, out float scale, out int padX, out int padY);

            // 2. MAT -> TENSOR (ghi vào buffer tái sử dụng, không cấp phát mảng mới mỗi frame)
            float[] inputData = MatToTensorData(_letterboxScratch);

            var tensor = new DenseTensor<float>(
                inputData,
                new int[] { 1, 3, InputHeight, InputWidth }
            );

            // 3. INPUT NAME
            string inputName = _session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor)
            };

            // 4. RUN MODEL
            using (var results = _session.Run(inputs))
            {
                var outputTensor = results.First().AsTensor<float>();
                var dimensions = outputTensor.Dimensions;

                // Lấy dữ liệu ra mảng phẳng 1 lần duy nhất — indexer đa chiều outputTensor[a,b,c]
                // cấp phát 1 mảng int[] "indices" MỚI mỗi lần gọi và tính lại stride, rất tốn
                // khi gọi hàng chục nghìn lần/frame (8400 anchor x nhiều field). Dữ liệu ONNX
                // luôn là row-major nên đọc phẳng rồi tự tính offset cho kết quả giống hệt.
                float[] flat = outputTensor.ToArray();

                if (dimensions.Length == 3 && dimensions[1] == 6)
                {
                    int numAnchors = dimensions[2]; // 8400

                    for (int i = 0; i < numAnchors; i++)
                    {
                        // Lấy điểm số của 2 class
                        float scoreClass0 = flat[4 * numAnchors + i]; // Barcode
                        float scoreClass1 = flat[5 * numAnchors + i]; // QR Code

                        // Tìm class có confidence lớn hơn
                        float confidence = scoreClass0;
                        int classId = 0;

                        if (scoreClass1 > scoreClass0)
                        {
                            confidence = scoreClass1;
                            classId = 1;
                        }

                        // Lọc theo ngưỡng tin cậy
                        if (confidence < ConfidenceThreshold)
                            continue;

                        // Đọc tọa độ Center Point
                        float cx = flat[0 * numAnchors + i];
                        float cy = flat[1 * numAnchors + i];
                        float w = flat[2 * numAnchors + i];
                        float h = flat[3 * numAnchors + i];

                        // Chuyển sang (x1, y1, x2, y2)
                        float x1 = cx - (w / 2.0f);
                        float y1 = cy - (h / 2.0f);
                        float x2 = cx + (w / 2.0f);
                        float y2 = cy + (h / 2.0f);

                        ProcessBoundingBox(frame, scale, padX, padY, x1, y1, x2, y2, confidence, classId, resultsList);
                    }
                }
                // Trường hợp B: Định dạng [1, 300, 6] (NMS End-to-End Export)
                else if (dimensions.Length == 3 && dimensions[2] == 6)
                {
                    int numDetections = dimensions[1]; // 300

                    for (int i = 0; i < numDetections; i++)
                    {
                        int baseIdx = i * 6;
                        float confidence = flat[baseIdx + 4];
                        if (confidence < ConfidenceThreshold)
                            continue;

                        int classId = (int)flat[baseIdx + 5];

                        float x1 = flat[baseIdx + 0];
                        float y1 = flat[baseIdx + 1];
                        float x2 = flat[baseIdx + 2];
                        float y2 = flat[baseIdx + 3];

                        ProcessBoundingBox(frame, scale, padX, padY, x1, y1, x2, y2, confidence, classId, resultsList);
                    }
                }

                // Sắp xếp theo Confidence và lọc bỏ các khung trùng lặp bằng NMS
                var filteredList = NonMaximumSuppression(resultsList, 0.45f);
                return filteredList.OrderByDescending(x => x.Confidence).ToList();
            }
        }

        // =========================================================
        // OVERLOAD: Trả về 1 kết quả tốt nhất (Để tương thích code cũ)
        // =========================================================
        public YoloDetection Detect(Mat frame)
        {
            var list = DetectAll(frame);
            return list.FirstOrDefault();
        }

        // =========================================================
        // HELPER PROCESS BOUNDING BOX
        // =========================================================
        private void ProcessBoundingBox(
            Mat frame, float scale, int padX, int padY,
            float x1, float y1, float x2, float y2,
            float confidence, int classId, List<YoloDetection> list)
        {
            // Map từ 640x640 về kích thước Frame gốc
            float originalX1 = (x1 - padX) / scale;
            float originalY1 = (y1 - padY) / scale;
            float originalX2 = (x2 - padX) / scale;
            float originalY2 = (y2 - padY) / scale;

            int rectX = (int)Math.Round(originalX1);
            int rectY = (int)Math.Round(originalY1);
            int rectX2 = (int)Math.Round(originalX2);
            int rectY2 = (int)Math.Round(originalY2);

            // Giới hạn biên (Clamp)
            rectX = Math.Max(0, Math.Min(rectX, frame.Width - 1));
            rectY = Math.Max(0, Math.Min(rectY, frame.Height - 1));
            rectX2 = Math.Max(rectX + 1, Math.Min(rectX2, frame.Width));
            rectY2 = Math.Max(rectY + 1, Math.Min(rectY2, frame.Height));

            int rectWidth = rectX2 - rectX;
            int rectHeight = rectY2 - rectY;

            if (rectWidth <= 0 || rectHeight <= 0)
                return;

            list.Add(new YoloDetection
            {
                Rect = new OpenCvSharp.Rect(rectX, rectY, rectWidth, rectHeight),
                Confidence = confidence,
                ClassId = classId // 0: Barcode, 1: QR Code
            });
        }

        // =========================================================
        // LETTERBOX — ghi kết quả vào _letterboxScratch (tái sử dụng), không new Mat mỗi frame
        // =========================================================
        private void Letterbox(Mat source, out float scale, out int padX, out int padY)
        {
            scale = Math.Min((float)InputWidth / source.Width, (float)InputHeight / source.Height);

            int newWidth = (int)Math.Round(source.Width * scale);
            int newHeight = (int)Math.Round(source.Height * scale);

            padX = (InputWidth - newWidth) / 2;
            padY = (InputHeight - newHeight) / 2;

            // Tô lại nền xám 114 để xóa pixel còn sót của frame trước ở vùng viền
            _letterboxScratch.SetTo(new Scalar(114, 114, 114));

            Cv2.Resize(
                source,
                _resizedScratch,
                new OpenCvSharp.Size(newWidth, newHeight),
                0, 0,
                InterpolationFlags.Area // Tốt khi thu nhỏ ảnh, giữ chi tiết nét hơn Linear
            );

            var roi = new OpenCvSharp.Rect(padX, padY, newWidth, newHeight);
            using (var destination = new Mat(_letterboxScratch, roi))
            {
                _resizedScratch.CopyTo(destination);
            }
        }

        // =========================================================
        // MAT -> TENSOR — ghi vào _tensorBuffer (tái sử dụng), không Cv2.Split (3 Mat/frame)
        // =========================================================
        private float[] MatToTensorData(Mat mat)
        {
            Cv2.CvtColor(mat, _rgbScratch, ColorConversionCodes.BGR2RGB);

            // Đọc từng pixel 3 kênh (R,G,B) một lần duy nhất rồi tự tách thành planar (CHW),
            // cho kết quả giống hệt cách Split + GetArray từng kênh trước đây.
            _rgbScratch.GetArray(out Vec3b[] pixels);

            int channelSize = InputWidth * InputHeight;
            for (int p = 0; p < channelSize; p++)
            {
                Vec3b px = pixels[p];
                _tensorBuffer[p] = px.Item0 / 255.0f;
                _tensorBuffer[channelSize + p] = px.Item1 / 255.0f;
                _tensorBuffer[2 * channelSize + p] = px.Item2 / 255.0f;
            }

            return _tensorBuffer;
        }

        private List<YoloDetection> NonMaximumSuppression(List<YoloDetection> detections, float iouThreshold = 0.45f)
        {
            var sorted = detections.OrderByDescending(x => x.Confidence).ToList();
            int n = sorted.Count;
            var suppressed = new bool[n];
            var result = new List<YoloDetection>(n);

            // Đánh dấu loại bỏ thay vì RemoveAt giữa vòng lặp (RemoveAt dịch chuyển cả mảng,
            // cộng dồn thành chi phí thừa không cần thiết khi có nhiều box ứng viên).
            for (int i = 0; i < n; i++)
            {
                if (suppressed[i])
                    continue;

                var current = sorted[i];
                result.Add(current);

                for (int j = i + 1; j < n; j++)
                {
                    if (suppressed[j])
                        continue;

                    if (CalculateIoU(current.Rect, sorted[j].Rect) > iouThreshold)
                        suppressed[j] = true;
                }
            }

            return result;
        }

        private float CalculateIoU(OpenCvSharp.Rect box1, OpenCvSharp.Rect box2)
        {
            int x1 = Math.Max(box1.X, box2.X);
            int y1 = Math.Max(box1.Y, box2.Y);
            int x2 = Math.Min(box1.X + box1.Width, box2.X + box2.Width);
            int y2 = Math.Min(box1.Y + box1.Height, box2.Y + box2.Height);

            int intersectionWidth = Math.Max(0, x2 - x1);
            int intersectionHeight = Math.Max(0, y2 - y1);

            int intersectionArea = intersectionWidth * intersectionHeight;
            int unionArea = (box1.Width * box1.Height) + (box2.Width * box2.Height) - intersectionArea;

            return unionArea == 0 ? 0 : (float)intersectionArea / unionArea;
        }

        // =========================================================
        // DISPOSE
        // =========================================================
        public void Dispose()
        {
            _session?.Dispose();
            _letterboxScratch?.Dispose();
            _resizedScratch?.Dispose();
            _rgbScratch?.Dispose();
        }
    }
}