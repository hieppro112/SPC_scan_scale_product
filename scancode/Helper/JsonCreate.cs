using scancode.Models;
using scancode.UI;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using System.CodeDom;

namespace scancode.Helper
{
    public class JsonCreate
    {
        public static void SavaJsonErr(dataHistory data, string path)
        {
            try
            {
                // 1. Kiểm tra và tự động tạo thư mục 'path' nếu chưa tồn tại
                if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                // 2. Tạo đường dẫn đầy đủ tới file .json (Bao gồm tên file có mốc thời gian)
                string fileName = $"log_packing_{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.json";
                string fullPath = Path.Combine(path, fileName);

                // 3. Cấu hình định dạng JSON thụt lề cho đẹp mắt
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                // 4. Chuyển đổi dữ liệu sang chuỗi JSON
                string jsonString = JsonSerializer.Serialize(data, options);

                // 5. Ghi file JSON vào đúng đường dẫn 'fullPath' (Thay vì 'path')
                File.WriteAllText(fullPath, jsonString);
            }
            catch (Exception ex)
            {
                NotifyDialog.ShowWarning("Lỗi khi lưu Json: " + ex.Message);
            }
        }
    }
}
