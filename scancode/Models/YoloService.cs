using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace scancode.Models
{

    public class YoloService : IDisposable
    {
        private readonly InferenceSession _session;

        private const int InputWidth = 640;
        private const int InputHeight = 640;

        private const float ConfidenceThreshold = 0.50f;


        public YoloService(string modelPath)
        {
            var options = new SessionOptions();

            // Giới hạn CPU cho ONNX Runtime
            options.IntraOpNumThreads = 2;
            options.InterOpNumThreads = 1;

            _session = new InferenceSession(
                modelPath,
                options
            );
        }


        // =========================================================
        // MODEL INFO
        // =========================================================

        public void PrintModelInfo()
        {
            foreach (var input in _session.InputMetadata)
            {
                Console.WriteLine(
                    $"Input: {input.Key} | " +
                    $"Type: {input.Value.ElementType}"
                );

                Console.WriteLine(
                    $"Dimensions: " +
                    $"{string.Join(", ", input.Value.Dimensions)}"
                );
            }


            foreach (var output in _session.OutputMetadata)
            {
                Console.WriteLine(
                    $"Output: {output.Key} | " +
                    $"Type: {output.Value.ElementType}"
                );

                Console.WriteLine(
                    $"Dimensions: " +
                    $"{string.Join(", ", output.Value.Dimensions)}"
                );
            }
        }


        // =========================================================
        // DETECT
        // =========================================================

        public YoloDetection Detect(Mat frame)
        {
            if (frame == null || frame.Empty())
                return null;


            Mat resized = null;

            try
            {
                float scale;
                int padX;
                int padY;


                // =================================================
                // 1. LETTERBOX
                // =================================================

                Letterbox(
                    frame,
                    out resized,
                    out scale,
                    out padX,
                    out padY
                );


                // =================================================
                // 2. MAT -> TENSOR
                // =================================================

                float[] inputData =
                    MatToTensorData(resized);


                var tensor =
                    new DenseTensor<float>(
                        inputData,
                        new int[]
                        {
                            1,
                            3,
                            InputHeight,
                            InputWidth
                        }
                    );


                // =================================================
                // 3. INPUT NAME
                // =================================================

                string inputName =
                    _session.InputMetadata
                            .Keys
                            .First();


                var inputs =
                    new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor(
                            inputName,
                            tensor
                        )
                    };


                // =================================================
                // 4. RUN MODEL
                // =================================================

                using (
                    IDisposableReadOnlyCollection<
                        DisposableNamedOnnxValue
                    > results =
                        _session.Run(inputs))
                {
                    var output =
                        results
                            .First()
                            .AsTensor<float>();


                    // =================================================
                    // EXPECT:
                    //
                    // [1, 300, 6]
                    //
                    // x1
                    // y1
                    // x2
                    // y2
                    // confidence
                    // class
                    // =================================================

                    int detectionCount =
                        output.Dimensions[1];


                    YoloDetection best =
                        null;


                    float bestConfidence = 0;


                    for (
                        int i = 0;
                        i < detectionCount;
                        i++)
                    {
                        float x1 =
                            output[0, i, 0];

                        float y1 =
                            output[0, i, 1];

                        float x2 =
                            output[0, i, 2];

                        float y2 =
                            output[0, i, 3];

                        float confidence =
                            output[0, i, 4];

                        float classValue =
                            output[0, i, 5];


                        int classId =
                            (int)classValue;


                        // =================================================
                        // CONFIDENCE
                        // =================================================

                        if (
                            confidence <
                            ConfidenceThreshold)
                        {
                            continue;
                        }


                        // =================================================
                        // BARCODE CLASS
                        //
                        // Nếu dataset của bạn chỉ có:
                        //
                        // 0 = barcode
                        //
                        // thì giữ nguyên.
                        // =================================================

                        if (classId != 0)
                        {
                            continue;
                        }


                        if (
                            confidence <=
                            bestConfidence)
                        {
                            continue;
                        }


                        // =================================================
                        // 5. MAP 640x640
                        //    -> FRAME GỐC
                        // =================================================

                        float originalX1 =
                            (x1 - padX) /
                            scale;

                        float originalY1 =
                            (y1 - padY) /
                            scale;

                        float originalX2 =
                            (x2 - padX) /
                            scale;

                        float originalY2 =
                            (y2 - padY) /
                            scale;


                        int rectX =
                            (int)Math.Round(
                                originalX1
                            );

                        int rectY =
                            (int)Math.Round(
                                originalY1
                            );

                        int rectX2 =
                            (int)Math.Round(
                                originalX2
                            );

                        int rectY2 =
                            (int)Math.Round(
                                originalY2
                            );


                        // =================================================
                        // CLAMP
                        // =================================================

                        rectX =
                            Math.Max(
                                0,
                                Math.Min(
                                    rectX,
                                    frame.Width - 1
                                )
                            );


                        rectY =
                            Math.Max(
                                0,
                                Math.Min(
                                    rectY,
                                    frame.Height - 1
                                )
                            );


                        rectX2 =
                            Math.Max(
                                rectX + 1,
                                Math.Min(
                                    rectX2,
                                    frame.Width
                                )
                            );


                        rectY2 =
                            Math.Max(
                                rectY + 1,
                                Math.Min(
                                    rectY2,
                                    frame.Height
                                )
                            );


                        int width =
                            rectX2 - rectX;

                        int height =
                            rectY2 - rectY;


                        if (
                            width <= 0 ||
                            height <= 0)
                        {
                            continue;
                        }


                        // =================================================
                        // BEST DETECTION
                        // =================================================

                        bestConfidence =
                            confidence;


                        best =
                            new YoloDetection
                            {
                                Rect =
                                    new OpenCvSharp.Rect(
                                        rectX,
                                        rectY,
                                        width,
                                        height
                                    ),

                                Confidence =
                                    confidence,

                                ClassId =
                                    classId
                            };
                    }


                    return best;
                }
            }
            finally
            {
                resized?.Dispose();
            }
        }


        // =========================================================
        // LETTERBOX
        // =========================================================

        private void Letterbox(
            Mat source,
            out Mat result,
            out float scale,
            out int padX,
            out int padY)
        {
            scale =
                Math.Min(
                    (float)InputWidth /
                    source.Width,

                    (float)InputHeight /
                    source.Height
                );


            int newWidth =
                (int)Math.Round(
                    source.Width * scale
                );


            int newHeight =
                (int)Math.Round(
                    source.Height * scale
                );


            padX =
                (InputWidth - newWidth) / 2;


            padY =
                (InputHeight - newHeight) / 2;


            using (var resized = new Mat())
            {
                Cv2.Resize(
                    source,
                    resized,
                    new OpenCvSharp.Size(
                        newWidth,
                        newHeight
                    ),
                    0,
                    0,
                    InterpolationFlags.Linear
                );


                result =
                    new Mat(
                        InputHeight,
                        InputWidth,
                        MatType.CV_8UC3,
                        new Scalar(
                            114,
                            114,
                            114
                        )
                    );


                var roi =
                    new OpenCvSharp.Rect(
                        padX,
                        padY,
                        newWidth,
                        newHeight
                    );


                using (
                    var destination =
                        new Mat(
                            result,
                            roi
                        ))
                {
                    resized.CopyTo(
                        destination
                    );
                }
            }
        }


        // =========================================================
        // MAT -> TENSOR
        // =========================================================

        private float[] MatToTensorData(
            Mat mat)
        {
            using (var rgb = new Mat())
            {
                // BGR -> RGB
                Cv2.CvtColor(
                    mat,
                    rgb,
                    ColorConversionCodes.BGR2RGB
                );


                Mat[] channels =
                    Cv2.Split(rgb);


                try
                {
                    int channelSize =
                        InputWidth *
                        InputHeight;


                    float[] data =
                        new float[
                            3 *
                            channelSize
                        ];


                    for (
                        int c = 0;
                        c < 3;
                        c++)
                    {
                        byte[] channelBytes;

                        channels[c].GetArray(
                            out channelBytes
                        );


                        int offset =
                            c *
                            channelSize;


                        for (
                            int i = 0;
                            i < channelSize;
                            i++)
                        {
                            data[
                                offset + i
                            ] =
                                channelBytes[i]
                                / 255.0f;
                        }
                    }


                    return data;
                }
                finally
                {
                    foreach (
                        Mat channel
                        in channels)
                    {
                        channel.Dispose();
                    }
                }
            }
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