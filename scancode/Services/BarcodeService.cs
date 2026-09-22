using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using ZXing;
using ZXing.Common;

namespace scancode.Helper
{
    public class BarcodeResult
    {
        public string barcode { get; set; }
        public OpenCvSharp.Rect rect { get; set; }
    }

    public class BarcodeService
    {
        private readonly BarcodeReader _reader;

        public BarcodeService()
        {
            _reader = new BarcodeReader();
            _reader.AutoRotate = true;

            _reader.Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true, // Thử đảo màu để nhận diện QR Code trên nền tối
                PossibleFormats = new List<BarcodeFormat>
                {
                    BarcodeFormat.QR_CODE,
                    BarcodeFormat.CODE_128,
                    BarcodeFormat.EAN_13,
                    BarcodeFormat.EAN_8,
                    BarcodeFormat.CODE_39,
                    BarcodeFormat.DATA_MATRIX
                }
            };
        }

        public BarcodeResult Decode(Mat frame)
        {
            if (frame == null || frame.Empty())
                return null;

            Bitmap bitmap = null;

            try
            {
                bitmap = BitmapConverter.ToBitmap(frame);
                var result = _reader.Decode(bitmap);

                if (result == null || string.IsNullOrWhiteSpace(result.Text))
                    return null;

                return new BarcodeResult
                {
                    barcode = result.Text,
                    rect = new OpenCvSharp.Rect(0, 0, frame.Width, frame.Height)
                };
            }
            catch
            {
                return null;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
    }
}