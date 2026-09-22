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

            Mat resized = null;

            try
            {
                // 1. LETTERBOX (Giữ nguyên tỷ lệ ảnh, chèn viền xám 114)
                Letterbox(frame, out resized, out float scale, out int padX, out int padY);

                // 2. MAT -> TENSOR
                float[] inputData = MatToTensorData(resized);

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


                    if (dimensions.Length == 3 && dimensions[1] == 6)
                    {
                        int numAnchors = dimensions[2]; // 8400

                        for (int i = 0; i < numAnchors; i++)
                        {
                            // Lấy điểm số của 2 class
                            float scoreClass0 = outputTensor[0, 4, i]; // Barcode
                            float scoreClass1 = outputTensor[0, 5, i]; // QR Code

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
                            float cx = outputTensor[0, 0, i];
                            float cy = outputTensor[0, 1, i];
                            float w = outputTensor[0, 2, i];
                            float h = outputTensor[0, 3, i];

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
                            float confidence = outputTensor[0, i, 4];
                            if (confidence < ConfidenceThreshold)
                                continue;

                            int classId = (int)outputTensor[0, i, 5];

                            float x1 = outputTensor[0, i, 0];
                            float y1 = outputTensor[0, i, 1];
                            float x2 = outputTensor[0, i, 2];
                            float y2 = outputTensor[0, i, 3];

                            ProcessBoundingBox(frame, scale, padX, padY, x1, y1, x2, y2, confidence, classId, resultsList);
                        }
                    }

                    // Sắp xếp theo Confidence và lọc bỏ các khung trùng lặp bằng NMS
                    var filteredList = NonMaximumSuppression(resultsList, 0.45f);
                    return filteredList.OrderByDescending(x => x.Confidence).ToList();
                }
            }
            finally
            {
                resized?.Dispose();
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
        // LETTERBOX
        // =========================================================
        private void Letterbox(Mat source, out Mat result, out float scale, out int padX, out int padY)
        {
            scale = Math.Min((float)InputWidth / source.Width, (float)InputHeight / source.Height);

            int newWidth = (int)Math.Round(source.Width * scale);
            int newHeight = (int)Math.Round(source.Height * scale);

            padX = (InputWidth - newWidth) / 2;
            padY = (InputHeight - newHeight) / 2;

            using (var resized = new Mat())
            {
                //Cv2.Resize(source, resized, new OpenCvSharp.Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);
                // Trong hàm Letterbox() của YoloService.cs
                Cv2.Resize(
                    source,
                    resized,
                    new OpenCvSharp.Size(newWidth, newHeight),
                    0, 0,
                    InterpolationFlags.Area // <-- Đổi từ Linear sang Area (rất tốt khi thu nhỏ ảnh giữ chi tiết nét)
                );

                result = new Mat(InputHeight, InputWidth, MatType.CV_8UC3, new Scalar(114, 114, 114));

                var roi = new OpenCvSharp.Rect(padX, padY, newWidth, newHeight);
                using (var destination = new Mat(result, roi))
                {
                    resized.CopyTo(destination);
                }
            }
        }

        // =========================================================
        // MAT -> TENSOR
        // =========================================================
        private float[] MatToTensorData(Mat mat)
        {
            using (var rgb = new Mat())
            {
                Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);
                Mat[] channels = Cv2.Split(rgb);

                try
                {
                    int channelSize = InputWidth * InputHeight;
                    float[] data = new float[3 * channelSize];

                    for (int c = 0; c < 3; c++)
                    {
                        channels[c].GetArray(out byte[] channelBytes);
                        int offset = c * channelSize;

                        for (int i = 0; i < channelSize; i++)
                        {
                            data[offset + i] = channelBytes[i] / 255.0f;
                        }
                    }

                    return data;
                }
                finally
                {
                    foreach (Mat channel in channels)
                    {
                        channel.Dispose();
                    }
                }
            }
        }

        private List<YoloDetection> NonMaximumSuppression(List<YoloDetection> detections, float iouThreshold = 0.45f)
        {
            var result = new List<YoloDetection>();
            var sortedDetections = detections.OrderByDescending(x => x.Confidence).ToList();

            while (sortedDetections.Count > 0)
            {
                var current = sortedDetections[0];
                result.Add(current);
                sortedDetections.RemoveAt(0);

                for (int i = sortedDetections.Count - 1; i >= 0; i--)
                {
                    var target = sortedDetections[i];
                    if (CalculateIoU(current.Rect, target.Rect) > iouThreshold)
                    {
                        sortedDetections.RemoveAt(i);
                    }
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
        }
    }
}