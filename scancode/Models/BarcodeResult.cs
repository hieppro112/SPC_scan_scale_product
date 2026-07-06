using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZXing;

namespace scancode.Models
{
    public class BarcodeResult
    {
        public string barcode { get; set; }
        public Rect rect { get; set; }
    }
}
