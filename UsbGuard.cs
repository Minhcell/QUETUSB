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

        // ================================================================
        // CÁCH MẠNH HƠN 1: Group Policy "Removable Storage Access" (Deny_All)
        // Chặn TOÀN BỘ thiết bị lưu trữ di động: USB, điện thoại (WPD/MTP), thẻ SD, CD/DVD...
        // ================================================================
        private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices";

        public static bool? IsDenyAll()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(PolicyPath, false))
                {
                    if (key == null) return false;
                    object v = key.GetValue("Deny_All");
                    if (v == null) return false;
                    return Convert.ToInt32(v) == 1;
                }
            }
            catch { return null; }
        }

        public static string SetDenyAll(bool deny)
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(PolicyPath))
                {
                    if (key == null) return "Không tạo được khoá Group Policy.";
                    key.SetValue("Deny_All", deny ? 1 : 0, RegistryValueKind.DWord);
                }
                return (deny ? "Đã CHẶN toàn bộ thiết bị lưu trữ di động (USB, điện thoại, thẻ nhớ, CD/DVD)."
                             : "Đã BỎ chặn chính sách thiết bị lưu trữ di động.") +
                       " Thiết bị đang cắm cần rút ra cắm lại để áp dụng.";
            }
            catch (Exception ex) { return "Lỗi: " + ex.Message; }
        }

        // ================================================================
        // CÁCH MẠNH HƠN 2: Write-Protect — cho đọc nhưng CẤM GHI/COPY ra USB
        // ================================================================
        private const string StoragePolicyPath = @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies";

        public static bool? IsWriteProtect()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(StoragePolicyPath, false))
                {
                    if (key == null) return false;
                    object v = key.GetValue("WriteProtect");
                    if (v == null) return false;
                    return Convert.ToInt32(v) == 1;
                }
            }
            catch { return null; }
        }

        public static string SetWriteProtect(bool on)
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(StoragePolicyPath))
                {
                    if (key == null) return "Không tạo được khoá StorageDevicePolicies.";
                    key.SetValue("WriteProtect", on ? 1 : 0, RegistryValueKind.DWord);
                }
                return (on ? "Đã bật CHỈ ĐỌC cho USB (cấm ghi/copy ra)."
                           : "Đã tắt chế độ chỉ đọc cho USB.") +
                       " Thiết bị đang cắm cần rút ra cắm lại để áp dụng.";
            }
            catch (Exception ex) { return "Lỗi: " + ex.Message; }
        }
    }
}
