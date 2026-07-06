using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp.Extensions;
using ZXing;
using System.Linq;
using scancode.Models;

namespace scancode.Services
{
    public class BarcodeService
    {
        private string barcodeLast = "";
        private readonly BarcodeReader _reader;
        public BarcodeService()
        {
            _reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = false
                }
            };

        }

        public BarcodeResult Decode(Mat frame)
        {
            using (var bitmap = BitmapConverter.ToBitmap(frame))
            {
                var result = _reader.Decode(bitmap);

                if (result == null)
                {
                    return null;
                }

                var points = result.ResultPoints
                   .Select(p => new Point((int)p.X, (int)p.Y))
                   .ToArray();

                Rect rect = Cv2.BoundingRect(points);
                return new BarcodeResult
                {
                    barcode = result.Text,
                    rect = rect
                };
            }
        }

    }
}
