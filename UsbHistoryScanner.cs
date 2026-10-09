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
        public string Service;       // dịch vụ driver (usbhub, hidusb, usbvideo...)
        public bool Protected;       // true = thiết bị hệ thống (hub/chuột/phím/camera...) — nên GIỮ, không xoá
        public string FullKeyPath;   // đường dẫn khoá đầy đủ dưới HKLM (để xoá đúng mọi nhánh)
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
            ReadStorageVolume(present, list);
            ReadScsiUsb(present, list);

            // Gán FullKeyPath cho các record dưới Enum
            foreach (var rec in list)
                if (string.IsNullOrEmpty(rec.FullKeyPath))
                    rec.FullKeyPath = @"SYSTEM\CurrentControlSet\Enum\" + rec.InstanceId;

            // Quét sâu các nhánh khác trong HKLM theo dấu vết serial USB (DeviceClasses...)
            DeepScanReferences(present, list);
            return list;
        }

        /// <summary>
        /// Quét sâu HKLM tìm dấu vết USB ở các nhánh ngoài Enum (Control\DeviceClasses,
        /// Services\USBSTOR\Enum...). Bắt theo từ khoá USBSTOR / USB#VID_ trong tên khoá.
        /// KHÔNG đụng DriverStore / gói driver.
        /// </summary>
        private static void DeepScanReferences(HashSet<string> present, List<UsbRecord> list)
        {
            // Tập serial của thiết bị ĐANG cắm (để đánh dấu present cho các tham chiếu)
            var presentSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in list)
                if (rec.Present && !string.IsNullOrEmpty(rec.Serial))
                    presentSerials.Add(rec.Serial);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in list) seen.Add(rec.FullKeyPath);

            // DeviceClasses: mỗi {InterfaceGUID}\##?#USBSTOR#... hoặc ##?#USB#VID_... là một tham chiếu thiết bị
            try
            {
                string dcBase = @"SYSTEM\CurrentControlSet\Control\DeviceClasses";
                using (var dc = Registry.LocalMachine.OpenSubKey(dcBase, false))
                {
                    if (dc != null)
                    {
                        foreach (var guid in dc.GetSubKeyNames())
                        {
                            using (var g = dc.OpenSubKey(guid, false))
                            {
                                if (g == null) continue;
                                foreach (var symlink in g.GetSubKeyNames())
                                {
                                    string up = symlink.ToUpperInvariant();
                                    bool isUsb = up.Contains("USBSTOR#") || up.Contains("#USB#VID_") || up.StartsWith("##?#USB#VID_");
                                    if (!isUsb) continue;
                                    string full = dcBase + "\\" + guid + "\\" + symlink;
                                    if (!seen.Add(full)) continue;
                                    bool pres = false;
                                    foreach (var ser in presentSerials)
                                        if (up.IndexOf(ser.ToUpperInvariant(), StringComparison.Ordinal) >= 0) { pres = true; break; }
                                    list.Add(new UsbRecord
                                    {
                                        Type = "DeviceClass",
                                        VidPid = "",
                                        Serial = symlink,
                                        InstanceId = "",
                                        EnumRoot = "",
                                        ParentRel = "",
                                        Present = pres,
                                        Description = "Tham chiếu DeviceClass: " + CleanSymlink(symlink),
                                        Service = "",
                                        Protected = false,
                                        FullKeyPath = full
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static string CleanSymlink(string s)
        {
            string t = s.Replace("##?#", "").Replace("_??_", "");
            int cut = t.IndexOf('#');
            if (cut > 0)
            {
                int c2 = t.IndexOf('#', cut + 1);
                if (c2 > cut) t = t.Substring(0, c2);
            }
            return t.Replace("Disk&", "").Replace("&", " ").Replace("_", " ").Trim();
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
                                    string service = (dev.GetValue("Service") as string) ?? "";
                                    string classGuid = (dev.GetValue("ClassGUID") as string) ?? "";
                                    var rec = new UsbRecord
                                    {
                                        Type = root,
                                        VidPid = ExtractVidPid(parentName),
                                        Serial = serial,
                                        InstanceId = instanceId,
                                        EnumRoot = root,
                                        ParentRel = parentName,
                                        Present = present.Contains(instanceId),
                                        Description = BuildDesc(dev, parentName),
                                        Service = service,
                                        Protected = IsSystemDevice(service, parentName) || IsNetClass(classGuid)
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

        /// <summary>Quét Enum\STORAGE\Volume — chỉ lấy volume của USB (tên chứa USBSTOR), bỏ ổ trong máy ({GUID}#...).</summary>
        private static void ReadStorageVolume(HashSet<string> present, List<UsbRecord> list)
        {
            try
            {
                using (var branch = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\STORAGE\Volume", false))
                {
                    if (branch == null) return;
                    foreach (var name in branch.GetSubKeyNames())
                    {
                        // Chỉ volume của USB. Bỏ {GUID}#... (volume ổ cứng trong máy) để an toàn.
                        if (name.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string instanceId = @"STORAGE\Volume\" + name;
                        list.Add(new UsbRecord
                        {
                            Type = "VOLUME",
                            VidPid = "",
                            Serial = name,
                            InstanceId = instanceId,
                            EnumRoot = "STORAGE",
                            ParentRel = "Volume",
                            Present = present.Contains(instanceId),
                            Description = DescribeVolume(name),
                            Service = "",
                            Protected = false
                        });
                    }
                }
            }
            catch { }
        }

        /// <summary>Quét Enum\SCSI — một số USB/ổ ngoài hiện ở đây. Chỉ lấy mục của USB.</summary>
        private static void ReadScsiUsb(HashSet<string> present, List<UsbRecord> list)
        {
            try
            {
                using (var branch = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\SCSI", false))
                {
                    if (branch == null) return;
                    foreach (var parentName in branch.GetSubKeyNames())
                    {
                        using (var parent = branch.OpenSubKey(parentName, false))
                        {
                            if (parent == null) continue;
                            foreach (var serial in parent.GetSubKeyNames())
                            {
                                using (var dev = parent.OpenSubKey(serial, false))
                                {
                                    if (dev == null) continue;
                                    // Chỉ lấy mục có dấu hiệu USB (thiết bị ngoài), bỏ ổ SATA/NVMe trong máy
                                    string service = (dev.GetValue("Service") as string) ?? "";
                                    string desc = (dev.GetValue("DeviceDesc") as string) ?? "";
                                    string blob = (parentName + " " + desc).ToLowerInvariant();
                                    bool looksUsb = blob.Contains("usb") || service.ToLowerInvariant() == "usbstor";
                                    if (!looksUsb) continue;
                                    string instanceId = @"SCSI\" + parentName + "\\" + serial;
                                    list.Add(new UsbRecord
                                    {
                                        Type = "SCSI",
                                        VidPid = "",
                                        Serial = serial,
                                        InstanceId = instanceId,
                                        EnumRoot = "SCSI",
                                        ParentRel = parentName,
                                        Present = present.Contains(instanceId),
                                        Description = Clean(string.IsNullOrEmpty(desc) ? parentName : desc),
                                        Service = service,
                                        Protected = false
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static string DescribeVolume(string name)
        {
            // Tên dạng: _??_USBSTOR#Disk&Ven_USB&Prod__SanDisk_3.2Gen1&Rev_1.00#serial#{guid}
            string s = name.Replace("_??_", "");
            int firstHash = s.IndexOf('#');
            if (firstHash >= 0)
            {
                int cut = s.IndexOf('#', firstHash + 1); // cắt trước phần serial/GUID
                if (cut > firstHash) s = s.Substring(0, cut);
            }
            s = s.Replace("USBSTOR", "").Replace("Disk&", "").Replace("&", " ").Replace("_", " ").Trim();
            return "USB Volume: " + s;
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

        /// <summary>Nhận diện thiết bị HỆ THỐNG cần giữ (hub, chuột, phím, camera, bluetooth, loa) theo dịch vụ driver.</summary>
        private static bool IsSystemDevice(string service, string parentName)
        {
            if (parentName != null && parentName.StartsWith("ROOT_HUB", StringComparison.OrdinalIgnoreCase))
                return true;
            string s = (service ?? "").ToLowerInvariant();
            if (s.Length == 0) return false;
            // Hub USB
            if (s.Contains("usbhub") || s.Contains("hub3") || s.Contains("usbxhci") || s.Contains("usbehci"))
                return true;
            // Chuột / bàn phím / HID
            if (s == "hidusb" || s == "kbdhid" || s == "mouhid" || s == "kbdclass" || s == "mouclass" || s.Contains("hidclass"))
                return true;
            // Camera / webcam
            if (s == "usbvideo" || s.Contains("ksthunk") || s.Contains("stream"))
                return true;
            // Loa / mic USB
            if (s == "usbaudio" || s.Contains("usbaudio2"))
                return true;
            // Bluetooth
            if (s.Contains("bthusb") || s.Contains("bth"))
                return true;
            return false;
        }

        // Net class = card mạng / USB WiFi-LAN -> bảo vệ (không xoá, giữ WiFi)
        private static bool IsNetClass(string classGuid)
        {
            return !string.IsNullOrEmpty(classGuid) &&
                   classGuid.IndexOf("4D36E972-E325-11CE-BFC1-08002BE10318", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractVidPid(string parentName)
        {
            var m = Regex.Match(parentName, @"VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}");
            return m.Success ? m.Value : parentName;
        }

        /// <summary>
        /// Gom MỌI đường dẫn lịch sử USB cắm ngoài trong HKLM (trừ thiết bị hệ thống &amp; card mạng).
        /// KHÔNG đụng tới DriverStore / gói driver (oem*.inf) nên không xoá driver USB hay WiFi.
        /// </summary>
        public static List<string> CollectExternalHistoryHklm()
        {
            var paths = new List<string>();

            // Lấy TẤT CẢ dấu vết USB cắm ngoài ĐÃ RÚT (lịch sử), KHÔNG được bảo vệ — gồm cả
            // DeviceClass, STORAGE\Volume, SCSI... (quét sâu). Bỏ thiết bị đang cắm, hub/chuột/phím/camera/WiFi.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in ScanAll())
                if (!rec.Protected && !rec.Present && !string.IsNullOrEmpty(rec.FullKeyPath))
                    if (seen.Add(rec.FullKeyPath)) paths.Add(rec.FullKeyPath);

            // Các nơi chỉ chứa thiết bị cắm ngoài (điện thoại/MTP) -> gom toàn bộ khoá con
            AddAllSubKeys(paths, seen, @"SYSTEM\CurrentControlSet\Enum\WpdBusEnumRoot");
            AddAllSubKeys(paths, seen, @"SYSTEM\CurrentControlSet\Enum\SWD\WPDBUSENUM");
            AddAllSubKeys(paths, seen, @"SOFTWARE\Microsoft\Windows Portable Devices\Devices");

            return paths;
        }

        private static void AddAllSubKeys(List<string> paths, HashSet<string> seen, string basePath)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(basePath, false))
                {
                    if (k == null) return;
                    foreach (var name in k.GetSubKeyNames())
                    {
                        string full = basePath + "\\" + name;
                        if (seen.Add(full)) paths.Add(full);
                    }
                }
            }
            catch { }
        }

        /// <summary>Xoá một thiết bị/dấu vết: nếu là thiết bị đang cắm thì gỡ qua SetupAPI, rồi xoá khoá Registry theo FullKeyPath.</summary>
        public static string Remove(UsbRecord rec)
        {
            string msg;
            if (rec.Present && !string.IsNullOrEmpty(rec.InstanceId))
                DeviceUninstaller.RemoveByInstanceId(rec.InstanceId, out msg);
            else
                msg = rec.Present ? "Đang cắm." : "Lịch sử (không cắm).";

            bool ok = false;
            string full = rec.FullKeyPath;
            if (!string.IsNullOrEmpty(full))
            {
                int i = full.LastIndexOf('\\');
                if (i > 0)
                    ok = RegistryHelper.ForceDeleteSubKey(Registry.LocalMachine, full.Substring(0, i), full.Substring(i + 1));
            }

            return "[" + rec.Type + "] " + rec.Description + " (" + rec.Serial + "): " + msg +
                   (ok ? "  | Đã xoá Registry." : "  | Registry: chưa xoá được (sẽ xử lý bằng SYSTEM).");
        }
    }
}
