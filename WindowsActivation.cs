using System;
using System.Management;
using System.Text;

namespace QuanLyHeThong
{
    /// <summary>Đọc trạng thái kích hoạt bản quyền Windows qua WMI (chỉ đọc, không thay đổi gì).</summary>
    internal static class WindowsActivation
    {
        // ApplicationID của Windows
        private const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";

        public static string GetStatus()
        {
            var sb = new StringBuilder();
            try
            {
                using (var s = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT Name, Description, LicenseStatus, GracePeriodRemaining, PartialProductKey " +
                    "FROM SoftwareLicensingProduct WHERE ApplicationID = '" + WindowsAppId +
                    "' AND PartialProductKey <> null"))
                {
                    bool any = false;
                    foreach (ManagementObject o in s.Get())
                    {
                        any = true;
                        string name = (o["Name"] as string) ?? "Windows";
                        string desc = (o["Description"] as string) ?? "";
                        int status = o["LicenseStatus"] != null ? Convert.ToInt32(o["LicenseStatus"]) : -1;
                        uint grace = o["GracePeriodRemaining"] != null ? Convert.ToUInt32(o["GracePeriodRemaining"]) : 0;
                        string pkey = (o["PartialProductKey"] as string) ?? "";

                        sb.AppendLine("• Sản phẩm: " + name);
                        if (!string.IsNullOrEmpty(desc)) sb.AppendLine("  Loại giấy phép: " + desc);
                        if (!string.IsNullOrEmpty(pkey)) sb.AppendLine("  Product Key (5 ký tự cuối): " + pkey);
                        sb.AppendLine("  Trạng thái: " + StatusText(status));
                        if (grace > 0)
                            sb.AppendLine("  Hạn còn lại: " + (grace / 1440) + " ngày (" + grace + " phút)");
                        sb.AppendLine();
                    }
                    if (!any)
                        sb.AppendLine("Không đọc được thông tin giấy phép Windows (có thể máy chưa cài key nào).");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Lỗi khi đọc trạng thái: " + ex.Message);
            }
            return sb.ToString();
        }

        /// <summary>true = đã kích hoạt, false = chưa, null = không xác định.</summary>
        public static bool? IsActivated()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID = '" + WindowsAppId +
                    "' AND PartialProductKey <> null"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        int status = o["LicenseStatus"] != null ? Convert.ToInt32(o["LicenseStatus"]) : -1;
                        if (status == 1) return true;
                    }
                }
                return false;
            }
            catch { return null; }
        }

        private static string StatusText(int status)
        {
            switch (status)
            {
                case 0: return "Chưa kích hoạt (Unlicensed)";
                case 1: return "ĐÃ KÍCH HOẠT (Licensed)";
                case 2: return "Đang trong thời gian ân hạn ban đầu (OOB Grace)";
                case 3: return "Đang trong thời gian ân hạn (OOT Grace)";
                case 4: return "Bản quyền không hợp lệ - ân hạn (Non-Genuine Grace)";
                case 5: return "Chế độ nhắc nhở (Notification) - chưa kích hoạt";
                case 6: return "Ân hạn mở rộng (Extended Grace)";
                default: return "Không xác định (mã " + status + ")";
            }
        }
    }
}
