using System;
using System.Management;
using System.Text;

namespace QuanLyHeThong
{
    /// <summary>Đọc trạng thái kích hoạt Microsoft Office qua WMI (chỉ đọc, không thay đổi gì).</summary>
    internal static class OfficeActivation
    {
        public static string GetStatus()
        {
            var sb = new StringBuilder();
            int found = 0;

            // Office 2013 trở lên: nằm trong SoftwareLicensingProduct (tên bắt đầu bằng "Office")
            found += Query(sb, "SoftwareLicensingProduct");
            // Office 2010: nằm trong OfficeSoftwareProtectionProduct
            found += Query(sb, "OfficeSoftwareProtectionProduct");

            if (found == 0)
                sb.AppendLine("Không tìm thấy Office có giấy phép trên máy (có thể chưa cài, hoặc dùng bản Microsoft 365 đăng nhập bằng tài khoản).");
            return sb.ToString();
        }

        private static int Query(StringBuilder sb, string wmiClass)
        {
            int n = 0;
            try
            {
                using (var s = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT Name, Description, LicenseStatus, PartialProductKey, GracePeriodRemaining, " +
                    "KeyManagementServiceMachine, DiscoveredKeyManagementServiceMachineName " +
                    "FROM " + wmiClass + " WHERE Name LIKE 'Office%' AND PartialProductKey <> null"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        n++;
                        string name = (o["Name"] as string) ?? "Office";
                        string desc = (o["Description"] as string) ?? "";
                        int status = o["LicenseStatus"] != null ? Convert.ToInt32(o["LicenseStatus"]) : -1;
                        string pkey = (o["PartialProductKey"] as string) ?? "";
                        uint grace = o["GracePeriodRemaining"] != null ? Convert.ToUInt32(o["GracePeriodRemaining"]) : 0;
                        string kms = GetStr(o, "KeyManagementServiceMachine");
                        if (string.IsNullOrEmpty(kms)) kms = GetStr(o, "DiscoveredKeyManagementServiceMachineName");

                        sb.AppendLine("• " + name);
                        if (!string.IsNullOrEmpty(desc)) sb.AppendLine("  Kênh giấy phép: " + desc);
                        if (!string.IsNullOrEmpty(pkey)) sb.AppendLine("  Product Key (5 ký tự cuối): " + pkey);
                        sb.AppendLine("  Trạng thái: " + StatusText(status));
                        if (grace > 0)
                            sb.AppendLine("  Hạn còn lại: " + (grace / 1440) + " ngày (" + grace + " phút)");
                        if (!string.IsNullOrEmpty(kms))
                            sb.AppendLine("  Máy chủ KMS: " + kms);
                        sb.AppendLine("  => " + Interpret(desc, kms));
                        sb.AppendLine();
                    }
                }
            }
            catch { /* lớp WMI có thể không tồn tại trên phiên bản Office tương ứng */ }
            return n;
        }

        private static string Interpret(string desc, string kms)
        {
            string d = (desc ?? "").ToUpperInvariant();
            if (d.Contains("RETAIL"))
                return "Kênh RETAIL — giấy phép bán lẻ (bản quyền hợp lệ).";
            if (d.Contains("OEM"))
                return "Kênh OEM — bản quyền theo máy (hợp lệ).";
            if (d.Contains("MAK"))
                return "Kênh MAK — khóa số lượng lớn (bản quyền hợp lệ của tổ chức).";
            if (d.Contains("KMS") || d.Contains("VOLUME"))
            {
                string extra = string.IsNullOrEmpty(kms) ? "" : " (KMS host: " + kms + ")";
                return "Kênh KMS/VOLUME" + extra + " — kích hoạt kiểu doanh nghiệp. " +
                       "Nếu cơ quan CÓ máy chủ KMS chính thống thì hợp lệ; nếu KHÔNG, nhiều khả năng là kích hoạt lậu/crack.";
            }
            return "Không xác định được kênh giấy phép từ mô tả.";
        }

        private static string GetStr(ManagementObject o, string prop)
        {
            try { return (o[prop] as string) ?? ""; } catch { return ""; }
        }

        private static string StatusText(int status)
        {
            switch (status)
            {
                case 0: return "Chưa kích hoạt (Unlicensed)";
                case 1: return "ĐÃ KÍCH HOẠT (Licensed)";
                case 2: return "Ân hạn ban đầu (OOB Grace)";
                case 3: return "Ân hạn (OOT Grace)";
                case 4: return "Bản quyền không hợp lệ - ân hạn (Non-Genuine Grace)";
                case 5: return "Chế độ nhắc nhở (Notification) - chưa kích hoạt";
                case 6: return "Ân hạn mở rộng (Extended Grace)";
                default: return "Không xác định (mã " + status + ")";
            }
        }
    }
}
