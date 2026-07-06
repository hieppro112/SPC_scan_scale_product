using scancode.Binding;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace scancode.Services
{
    public class portService
    {
        // Event gửi dữ liệu ra ngoài
        public event Action<string> DataReceived;

        SerialPort port = new SerialPort(
            "COM8",
            9600,
            Parity.Even,
            7,
            StopBits.One
            );

        public bool connect()
        {
            try
            {
                port.DataReceived += (s, e) =>
                {
                    string data = port.ReadLine();
                    DataReceived?.Invoke(data);
                };
                port.Open();
                DataBinding binding = new DataBinding();
                binding.IsScaleConnected = port.IsOpen;


                return port.IsOpen;
            }
            catch
            {
                return false;
            }
        }
    }
}
