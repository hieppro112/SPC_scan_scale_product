using OpenCvSharp;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace scancode.Helper
{
    public class ImageHelper
    {
        public static BitmapSource ToBitmapSource(Mat mat)
        {
            // Xác định PixelFormat dựa trên số kênh
            PixelFormat pixelFormat;
            switch (mat.Channels())
            {
                case 1:
                    pixelFormat = PixelFormats.Gray8;
                    break;
                case 3:
                    pixelFormat = PixelFormats.Bgr24;
                    break;
                case 4:
                    pixelFormat = PixelFormats.Bgra32;
                    break;
                default:
                    throw new NotSupportedException($"Không hỗ trợ {mat.Channels()} channels");
            }

            var bitmap = new WriteableBitmap(
                mat.Width,
                mat.Height,
                96, 96,
                pixelFormat,
                null);

            bitmap.WritePixels(
                new Int32Rect(0, 0, mat.Width, mat.Height),
                mat.Data,
                (int)(mat.Step() * mat.Height),
                (int)mat.Step());

            return bitmap;
        }
    }
}