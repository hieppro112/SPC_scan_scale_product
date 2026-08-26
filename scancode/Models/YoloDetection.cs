using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace scancode.Models
{
    public class YoloDetection
    {
        public OpenCvSharp.Rect Rect { get; set; }

        public float Confidence { get; set; }

        public int ClassId { get; set; }
    }

}
