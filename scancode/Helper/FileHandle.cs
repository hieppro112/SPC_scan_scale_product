using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace scancode.Helper
{
    public class FileHandle
    {
        //lay gia tri config
        public static int getconfig(String s_search)
        {
            string configPath = @"C:\camera_config\camera_config.txt";

            try
            {
                // Kiểm tra file có tồn tại không
                if (!File.Exists(configPath))
                {
                    MessageBox.Show($"Không tìm thấy file config tại: {configPath}");
                    return -1;
                }

                // Đọc tất cả các dòng trong file
                string[] lines = File.ReadAllLines(configPath);

                // Tìm dòng bắt đầu bằng "Name_Servo:"
                foreach (string line in lines)
                {
                    if (line.StartsWith($"{s_search}:"))
                    {

                        string port = line.Split(':')[1];

                        int.TryParse(port.ToString(), out int r);
                        return r;
                    }
                }
                // Không tìm thấy dòng Name_Servo
                MessageBox.Show($"Không tìm thấy cấu hình {s_search} trong file config");
                return -1;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi đọc file config: {ex.Message}");
                return -1;
            }
        }

    }
}
