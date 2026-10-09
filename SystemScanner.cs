using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace QuanLyHeThong
{
    internal enum ItemKind { Usb, Driver, Nic }

    /// <summary>Một mục thiết bị / driver / card mạng tìm thấy khi quét.</summary>
    internal class SysItem
    {
        public ItemKind Kind;
        public string Name;         // Tên hiển thị
        public string Detail;       // Chi tiết (nhà sản xuất, MAC, phiên bản...)
        public string Status;       // Trạng thái
        public string InstanceId;   // PnP Device Instance ID (để gỡ thiết bị)
        public string InfName;      // oemXX.inf (để gỡ gói driver)

        public string KindText
        {
            get { return Kind == ItemKind.Usb ? "USB" : Kind == ItemKind.Driver ? "Driver" : "Card mạng"; }
        }
    }

    /// <summary>Quét và gỡ thiết bị USB, gói driver bên thứ ba, card mạng (có cả Wi-Fi).</summary>
    internal static class SystemScanner
    {
        // ====== QUÉT (chỉ liệt kê, KHÔNG xoá gì) ======

        public static List<SysItem> ScanUsb()
        {
            var list = new List<SysItem>();
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, DeviceID, Status, PNPClass, Manufacturer FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%'"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string id = (o["DeviceID"] as string) ?? "";
                        string name = (o["Name"] as string) ?? "(không tên)";
                        string cls = (o["PNPClass"] as string) ?? "";
                        string mfg = (o["Manufacturer"] as string) ?? "";
                        list.Add(new SysItem
                        {
                            Kind = ItemKind.Usb,
                            Name = name,
                            Detail = string.IsNullOrEmpty(cls) ? mfg : (cls + "  |  " + mfg),
                            Status = (o["Status"] as string) ?? "",
                            InstanceId = id
                        });
                    }
                }
            }
            catch (Exception ex) { list.Add(ErrItem(ItemKind.Usb, ex)); }
            return list;
        }

        public static List<SysItem> ScanDrivers()
        {
            var list = new List<SysItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT DeviceName, DriverProviderName, DriverVersion, InfName, DeviceClass FROM Win32_PnPSignedDriver"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string inf = (o["InfName"] as string) ?? "";
                        // Chỉ lấy gói driver bên thứ ba (oemXX.inf) để không đụng driver gốc của Windows
                        if (!inf.StartsWith("oem", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!seen.Add(inf)) continue; // mỗi gói chỉ hiện 1 dòng

                        string dev = (o["DeviceName"] as string) ?? "";
                        string prov = (o["DriverProviderName"] as string) ?? "";
                        string ver = (o["DriverVersion"] as string) ?? "";
                        string cls = (o["DeviceClass"] as string) ?? "";
                        // Chỉ lấy driver Wi-Fi (không dây), bỏ qua driver khác
                        if (!IsWifi(dev + " " + prov + " " + cls)) continue;
                        list.Add(new SysItem
                        {
                            Kind = ItemKind.Driver,
                            Name = "(Wi-Fi) " + inf + "  —  " + (string.IsNullOrEmpty(dev) ? cls : dev),
                            Detail = (string.IsNullOrEmpty(prov) ? "" : "NSX: " + prov) +
                                     (string.IsNullOrEmpty(ver) ? "" : "  |  v" + ver),
                            Status = "",
                            InfName = inf
                        });
                    }
                }
            }
            catch (Exception ex) { list.Add(ErrItem(ItemKind.Driver, ex)); }
            return list;
        }

        public static List<SysItem> ScanNics()
        {
            var list = new List<SysItem>();
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID, NetConnectionID, MACAddress, NetEnabled FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string id = (o["PNPDeviceID"] as string) ?? "";
                        if (string.IsNullOrEmpty(id)) continue;
                        string name = (o["Name"] as string) ?? "(không tên)";
                        string conn = (o["NetConnectionID"] as string) ?? "";
                        string mac = (o["MACAddress"] as string) ?? "";
                        bool enabled = o["NetEnabled"] is bool && (bool)o["NetEnabled"];
                        // Chỉ lấy card Wi-Fi (không dây), bỏ qua card mạng thường (LAN)
                        if (!IsWifi(name + " " + conn)) continue;
                        list.Add(new SysItem
                        {
                            Kind = ItemKind.Nic,
                            Name = "(Wi-Fi) " + name,
                            Detail = (string.IsNullOrEmpty(conn) ? "" : "Kết nối: " + conn + "  |  ") + "MAC: " + mac,
                            Status = enabled ? "Đang bật" : "Đang tắt",
                            InstanceId = id
                        });
                    }
                }
            }
            catch (Exception ex) { list.Add(ErrItem(ItemKind.Nic, ex)); }
            return list;
        }

        private static SysItem ErrItem(ItemKind k, Exception ex)
        {
            return new SysItem { Kind = k, Name = "(Lỗi khi quét)", Detail = ex.Message, Status = "" };
        }

        /// <summary>Nhận diện thiết bị/driver không dây (Wi-Fi) theo từ khoá trong tên.</summary>
        private static bool IsWifi(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.ToLowerInvariant();
            return t.Contains("wi-fi") || t.Contains("wifi") || t.Contains("wireless")
                || t.Contains("802.11") || t.Contains("wlan") || t.Contains("dual band");
        }

        // ====== XOÁ (chỉ chạy khi người dùng bấm nút, với các mục đã tích chọn) ======

        /// <summary>Gỡ một mục. cleanRegistry = true thì xoá luôn dấu vết trong Registry (Enum).</summary>
        public static string Remove(SysItem item, bool cleanRegistry)
        {
            if (item.Kind == ItemKind.Driver)
            {
                string m;
                DeviceUninstaller.UninstallOemInf(item.InfName, out m);
                return m;
            }

            // USB hoặc Card mạng: gỡ thiết bị bằng SetupAPI
            string msg;
            DeviceUninstaller.RemoveByInstanceId(item.InstanceId, out msg);
            msg = "\"" + item.Name + "\": " + msg;

            if (cleanRegistry && !string.IsNullOrEmpty(item.InstanceId))
            {
                bool ok = CleanEnumRegistry(item.InstanceId);
                msg += ok ? "  | Đã dọn dấu vết Registry." : "  | Registry: không còn dấu vết hoặc bị bảo vệ.";
            }
            return msg;
        }

        /// <summary>Xoá khoá dưới HKLM\SYSTEM\CurrentControlSet\Enum\&lt;InstanceId&gt; (dấu vết còn sót).</summary>
        private static bool CleanEnumRegistry(string instanceId)
        {
            try
            {
                int i = instanceId.LastIndexOf('\\');
                if (i <= 0) return false;
                string parentRel = instanceId.Substring(0, i);
                string leaf = instanceId.Substring(i + 1);
                string parentPath = @"SYSTEM\CurrentControlSet\Enum\" + parentRel;
                return RegistryHelper.ForceDeleteSubKey(Registry.LocalMachine, parentPath, leaf);
            }
            catch { return false; }
        }

        /// <summary>Liệt kê đường dẫn các khoá con dưới Enum\USBSTOR (để xoá bằng quyền SYSTEM).</summary>
        public static System.Collections.Generic.List<string> GetUsbStorSubPaths()
        {
            var list = new System.Collections.Generic.List<string>();
            try
            {
                using (var usbstor = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBSTOR", false))
                {
                    if (usbstor == null) return list;
                    foreach (var name in usbstor.GetSubKeyNames())
                        list.Add(@"SYSTEM\CurrentControlSet\Enum\USBSTOR\" + name);
                }
            }
            catch { }
            return list;
        }

        /// <summary>Xoá toàn bộ lịch sử USB từng cắm (ghost) trong USBSTOR.</summary>
        public static string ClearUsbStorHistory()
        {
            int ok = 0, fail = 0;
            try
            {
                using (var usbstor = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBSTOR", false))
                {
                    if (usbstor == null) return "Không có mục USBSTOR nào.";
                    foreach (var name in usbstor.GetSubKeyNames())
                    {
                        if (RegistryHelper.ForceDeleteSubKey(Registry.LocalMachine,
                            @"SYSTEM\CurrentControlSet\Enum\USBSTOR", name)) ok++;
                        else fail++;
                    }
                }
            }
            catch (Exception ex) { return "Lỗi: " + ex.Message; }
            return "Đã xoá " + ok + " mục lịch sử USBSTOR, " + fail + " mục không xoá được.";
        }
    }
}
