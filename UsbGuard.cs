using System;
using Microsoft.Win32;

namespace QuanLyHeThong
{
    /// <summary>Khoá / mở cổng USB đối với thiết bị lưu trữ ngoài (USB storage) qua Registry USBSTOR.</summary>
    internal static class UsbGuard
    {
        private const string KeyPath = @"SYSTEM\CurrentControlSet\Services\USBSTOR";
        private const string ValueName = "Start";
        // 3 = bật (cho phép), 4 = tắt (khoá)

        public static bool? IsLocked()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(KeyPath, false))
                {
                    if (key == null) return null;
                    object v = key.GetValue(ValueName);
                    if (v == null) return null;
                    int start = Convert.ToInt32(v);
                    return start == 4;
                }
            }
            catch { return null; }
        }

        public static string Lock()
        {
            return SetStart(4, "KHOÁ");
        }

        public static string Unlock()
        {
            return SetStart(3, "MỞ");
        }

        private static string SetStart(int value, string label)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(KeyPath, true))
                {
                    if (key == null)
                        return "Không mở được khoá USBSTOR trong Registry (máy có thể không có dịch vụ này).";
                    key.SetValue(ValueName, value, RegistryValueKind.DWord);
                    return "Đã " + label + " cổng USB (thiết bị lưu trữ ngoài). " +
                           "Thiết bị đang cắm có thể cần rút ra cắm lại để áp dụng.";
                }
            }
            catch (Exception ex)
            {
                return "Lỗi: " + ex.Message;
            }
        }
    }
}
