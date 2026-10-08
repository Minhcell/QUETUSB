using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace QuanLyHeThong
{
    /// <summary>Một thiết bị USB tìm thấy trong Registry (đang cắm hoặc đã từng cắm).</summary>
    internal class UsbRecord
    {
        public string Description;   // Tên mô tả
        public string VidPid;        // VID_xxxx&PID_xxxx
        public string Serial;        // Số seri / instance
        public string Type;          // USB hoặc USBSTOR
        public bool Present;         // Đang cắm?
        public string InstanceId;    // vd USB\VID_xxxx&PID_xxxx\seri
        public string EnumRoot;      // "USB" hoặc "USBSTOR"
        public string ParentRel;     // vd VID_xxxx&PID_xxxx  (khoá cha dưới Enum\<root>)
    }

    /// <summary>
    /// Quét toàn bộ USB ngoại vi kiểu USBDeview: đọc HKLM\...\Enum\USB và Enum\USBSTOR,
    /// liệt kê cả thiết bị đang cắm lẫn đã từng cắm (lịch sử). Chạy offline, không cần mạng.
    /// </summary>
    internal static class UsbHistoryScanner
    {
        public static List<UsbRecord> ScanAll()
        {
            var list = new List<UsbRecord>();
            HashSet<string> present;
            try { present = DeviceUninstaller.GetPresentInstanceIds(); }
            catch { present = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

            ReadEnumBranch("USB", present, list, onlyVidPid: true);
            ReadEnumBranch("USBSTOR", present, list, onlyVidPid: false);
            return list;
        }

        private static void ReadEnumBranch(string root, HashSet<string> present, List<UsbRecord> list, bool onlyVidPid)
        {
            try
            {
                using (var branch = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + root, false))
                {
                    if (branch == null) return;
                    foreach (var parentName in branch.GetSubKeyNames())
                    {
                        // Với nhánh USB: chỉ lấy thiết bị ngoại vi thật (VID_/PID_), bỏ hub/host controller
                        if (onlyVidPid && parentName.IndexOf("VID_", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        using (var parent = branch.OpenSubKey(parentName, false))
                        {
                            if (parent == null) continue;
                            foreach (var serial in parent.GetSubKeyNames())
                            {
                                using (var dev = parent.OpenSubKey(serial, false))
                                {
                                    if (dev == null) continue;
                                    string instanceId = root + "\\" + parentName + "\\" + serial;
                                    var rec = new UsbRecord
                                    {
                                        Type = root,
                                        VidPid = ExtractVidPid(parentName),
                                        Serial = serial,
                                        InstanceId = instanceId,
                                        EnumRoot = root,
                                        ParentRel = parentName,
                                        Present = present.Contains(instanceId),
                                        Description = BuildDesc(dev, parentName)
                                    };
                                    list.Add(rec);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static string BuildDesc(RegistryKey dev, string fallback)
        {
            string friendly = dev.GetValue("FriendlyName") as string;
            if (!string.IsNullOrEmpty(friendly)) return Clean(friendly);
            string desc = dev.GetValue("DeviceDesc") as string;
            if (!string.IsNullOrEmpty(desc)) return Clean(desc);
            string mfg = dev.GetValue("Mfg") as string;
            return string.IsNullOrEmpty(mfg) ? fallback : Clean(mfg);
        }

        // DeviceDesc/Mfg thường dạng "@file.inf,%key%;Nội dung" -> lấy phần sau dấu ';'
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.LastIndexOf(';');
            if (i >= 0 && i < s.Length - 1) return s.Substring(i + 1).Trim();
            return s.Trim();
        }

        private static string ExtractVidPid(string parentName)
        {
            var m = Regex.Match(parentName, @"VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}");
            return m.Success ? m.Value : parentName;
        }

        /// <summary>Xoá một thiết bị: nếu đang cắm thì gỡ qua SetupAPI, rồi xoá dấu vết Registry.</summary>
        public static string Remove(UsbRecord rec)
        {
            string msg;
            if (rec.Present)
            {
                DeviceUninstaller.RemoveByInstanceId(rec.InstanceId, out msg);
            }
            else
            {
                msg = "Thiết bị lịch sử (không cắm).";
            }

            string parentPath = @"SYSTEM\CurrentControlSet\Enum\" + rec.EnumRoot + "\\" + rec.ParentRel;
            bool ok = RegistryHelper.ForceDeleteSubKey(Registry.LocalMachine, parentPath, rec.Serial);

            // Nếu khoá cha hết thiết bị con thì xoá luôn cho sạch
            try
            {
                using (var parent = Registry.LocalMachine.OpenSubKey(parentPath, false))
                {
                    if (parent != null && parent.SubKeyCount == 0)
                    {
                        parent.Close();
                        RegistryHelper.ForceDeleteSubKey(Registry.LocalMachine,
                            @"SYSTEM\CurrentControlSet\Enum\" + rec.EnumRoot, rec.ParentRel);
                    }
                }
            }
            catch { }

            return "[" + rec.Type + "] " + rec.Description + " (" + rec.Serial + "): " + msg +
                   (ok ? "  | Đã xoá Registry." : "  | Registry: không xoá được / đã sạch.");
        }
    }
}
