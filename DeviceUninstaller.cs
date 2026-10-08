using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QuanLyHeThong
{
    /// <summary>
    /// Gỡ thiết bị và gói driver bằng Windows SetupAPI — bộ API gốc của Microsoft,
    /// hoạt động từ Windows 7 đến Windows 11 (chính là cơ chế Device Manager / DevCon dùng).
    /// </summary>
    internal static class DeviceUninstaller
    {
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const uint DIF_REMOVE = 0x00000005;
        private const uint SUOI_FORCEDELETE = 0x00000001;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
            StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiCallClassInstaller(uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupUninstallOEMInf(string InfFileName, uint Flags, IntPtr Reserved);

        /// <summary>Gỡ một thiết bị theo Instance ID (vd USB\VID_xxxx&amp;PID_xxxx\seri). Trả về true nếu thành công.</summary>
        public static bool RemoveByInstanceId(string instanceId, out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(instanceId)) { message = "Thiếu Instance ID."; return false; }

            IntPtr devInfo = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1))
            {
                message = "Không mở được danh sách thiết bị (SetupDiGetClassDevs).";
                return false;
            }

            try
            {
                var data = new SP_DEVINFO_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                uint index = 0;

                while (SetupDiEnumDeviceInfo(devInfo, index, ref data))
                {
                    index++;
                    string id = GetInstanceId(devInfo, ref data);
                    if (!string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool ok = SetupDiCallClassInstaller(DIF_REMOVE, devInfo, ref data);
                    if (ok)
                    {
                        message = "Đã gỡ thiết bị khỏi hệ thống.";
                        return true;
                    }
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        message = "Gỡ thất bại (mã lỗi " + err + ").";
                        return false;
                    }
                }

                message = "Không tìm thấy thiết bị (có thể đã được gỡ).";
                return false;
            }
            catch (Exception ex)
            {
                message = "Lỗi: " + ex.Message;
                return false;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }
        }

        /// <summary>Gỡ gói driver OEM (oemXX.inf) khỏi kho driver của Windows. Trả về true nếu thành công.</summary>
        public static bool UninstallOemInf(string infName, out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(infName)) { message = "Thiếu tên INF."; return false; }
            try
            {
                bool ok = SetupUninstallOEMInf(infName, SUOI_FORCEDELETE, IntPtr.Zero);
                if (ok) { message = "Đã gỡ gói driver " + infName + "."; return true; }
                int err = Marshal.GetLastWin32Error();
                message = "Gỡ gói driver " + infName + " thất bại (mã lỗi " + err + ").";
                return false;
            }
            catch (Exception ex)
            {
                message = "Lỗi: " + ex.Message;
                return false;
            }
        }

        /// <summary>Lấy tập Instance ID của mọi thiết bị ĐANG hiện diện (để đánh dấu USB đang cắm vs lịch sử).</summary>
        public static System.Collections.Generic.HashSet<string> GetPresentInstanceIds()
        {
            var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr devInfo = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return set;
            try
            {
                var data = new SP_DEVINFO_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                uint index = 0;
                while (SetupDiEnumDeviceInfo(devInfo, index, ref data))
                {
                    index++;
                    string id = GetInstanceId(devInfo, ref data);
                    if (!string.IsNullOrEmpty(id)) set.Add(id);
                }
            }
            catch { }
            finally { SetupDiDestroyDeviceInfoList(devInfo); }
            return set;
        }

        private static string GetInstanceId(IntPtr devInfo, ref SP_DEVINFO_DATA data)
        {
            var sb = new StringBuilder(512);
            if (SetupDiGetDeviceInstanceId(devInfo, ref data, sb, (uint)sb.Capacity, out uint req))
                return sb.ToString();

            if (Marshal.GetLastWin32Error() == ERROR_INSUFFICIENT_BUFFER && req > 0)
            {
                sb = new StringBuilder((int)req);
                if (SetupDiGetDeviceInstanceId(devInfo, ref data, sb, (uint)sb.Capacity, out _))
                    return sb.ToString();
            }
            return "";
        }
    }
}
