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
        private SerialPort _port;

        public bool connect(string namePort = "COM8", int baudRate = 9600)
        {
            try
            {
                if (_port != null && _port.IsOpen) return true;
                _port = new SerialPort(namePort, baudRate, Parity.None, 8, StopBits.One)
                {
                    NewLine = "\r\n",
                    ReadTimeout = 1000,
                };

                _port.DataReceived += (s, e) =>
                {
                    try
                    {
                        string rwData = _port.ReadLine();
                        if (!string.IsNullOrWhiteSpace(rwData))
                        {
                            string cleanWeight = rwData.Trim();
                            DataReceived?.Invoke(cleanWeight);
                        }
                    }
                    catch (Exception ex) {
                        Console.WriteLine("loi khi lay data can: "+ex);
                    }
                };


                _port.Open();
                DataBinding binding = new DataBinding();
                binding.IsScaleConnected = _port.IsOpen;


                return _port.IsOpen;
            }
            catch
            {
                return false;
            }
        }

        //gui lenh cho can 
        public void RequestWeight()
        {
            if (_port != null && _port.IsOpen)
            {
                _port.WriteLine("SP");
            }
        }

        //disconnect
        public void Disconnect()
        {
            if(_port !=null && _port.IsOpen) _port.Close();
        }
    }
}
