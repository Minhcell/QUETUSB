using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace QuanLyHeThong
{
    /// <summary>
    /// Xoá khoá Registry dưới quyền NT AUTHORITY\SYSTEM (như PsExec -s) bằng Task Scheduler —
    /// không cần file ngoài. Dùng cho các khoá Enum\USB bị SYSTEM/TrustedInstaller sở hữu
    /// mà Administrator không xoá được.
    /// </summary>
    internal static class SystemRegCleaner
    {
        public const string SysArg = "--sysdelreg";

        // ===== Phía gọi (chạy bằng Administrator) =====

        /// <summary>
        /// Chạy chính exe này dưới quyền SYSTEM để xoá danh sách khoá con dưới HKLM.
        /// Mỗi phần tử là đường dẫn tương đối dưới HKLM, vd:
        /// SYSTEM\CurrentControlSet\Enum\USB\VID_1234&PID_5678\serial
        /// </summary>
        public static string DeleteKeysAsSystem(List<string> hklmSubPaths, Action<string> log)
        {
            if (hklmSubPaths == null || hklmSubPaths.Count == 0)
                return "Không có khoá nào cần xoá bằng SYSTEM.";

            string tmp = Path.GetTempPath();
            string id = Guid.NewGuid().ToString("N");
            string payload = Path.Combine(tmp, "qlht_payload_" + id + ".txt");
            string result = Path.Combine(tmp, "qlht_result_" + id + ".txt");
            string wrapper = Path.Combine(tmp, "qlht_run_" + id + ".cmd");
            string taskName = "QLHT_SysDel_" + id.Substring(0, 8);
            string exe = Process.GetCurrentProcess().MainModule.FileName;

            try
            {
                File.WriteAllLines(payload, hklmSubPaths);
                if (File.Exists(result)) File.Delete(result);

                // File .cmd bọc ngoài để tránh rắc rối dấu ngoặc của schtasks
                File.WriteAllText(wrapper,
                    "@echo off\r\n\"" + exe + "\" " + SysArg + " \"" + payload + "\" \"" + result + "\"\r\n");

                log("Tạo tác vụ SYSTEM để xoá " + hklmSubPaths.Count + " khoá Registry...");

                // Tạo tác vụ chạy dưới SYSTEM
                string createArgs = "/Create /TN \"" + taskName + "\" /TR \"\\\"" + wrapper +
                                    "\\\"\" /SC ONCE /ST 23:59 /RU SYSTEM /RL HIGHEST /F";
                string cOut;
                int cExit = Run("schtasks.exe", createArgs, out cOut);
                if (cExit != 0)
                    return "Không tạo được tác vụ SYSTEM (schtasks mã " + cExit + "). " + FirstLine(cOut);

                // Chạy ngay
                string dummy;
                Run("schtasks.exe", "/Run /TN \"" + taskName + "\"", out dummy);

                // Chờ file kết quả (tối đa ~30 giây)
                bool done = false;
                for (int i = 0; i < 100; i++)
                {
                    if (File.Exists(result)) { Thread.Sleep(300); done = true; break; }
                    Thread.Sleep(300);
                }

                if (done)
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(result))
                            if (!string.IsNullOrWhiteSpace(line)) log(line);
                    }
                    catch { }
                    return "Đã xoá Registry bằng quyền SYSTEM xong.";
                }
                return "Hết thời gian chờ tác vụ SYSTEM (có thể bị chặn bởi phần mềm bảo mật).";
            }
            catch (Exception ex)
            {
                return "Lỗi khi chạy SYSTEM: " + ex.Message;
            }
            finally
            {
                try { string d2; Run("schtasks.exe", "/Delete /TN \"" + taskName + "\" /F", out d2); } catch { }
                try { if (File.Exists(payload)) File.Delete(payload); } catch { }
                try { if (File.Exists(result)) File.Delete(result); } catch { }
                try { if (File.Exists(wrapper)) File.Delete(wrapper); } catch { }
            }
        }

        private static int Run(string file, string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            foreach (var l in s.Split('\n')) { var t = l.Trim(); if (t.Length > 0) return t; }
            return "";
        }

        // ===== Phía chạy dưới SYSTEM (khi exe được gọi với --sysdelreg) =====

        /// <summary>Đọc payload (danh sách khoá), xoá từng khoá, ghi kết quả ra result. KHÔNG mở giao diện.</summary>
        public static void RunSystemWorker(string payloadPath, string resultPath)
        {
            var lines = new List<string>();
            try
            {
                lines.Add("[SYSTEM] Tiến trình chạy bởi: " + WhoAmI());
                string[] paths = File.ReadAllLines(payloadPath);
                foreach (var raw in paths)
                {
                    string sub = (raw ?? "").Trim();
                    if (sub.Length == 0) continue;
                    int i = sub.LastIndexOf('\\');
                    if (i <= 0) { lines.Add("[SYSTEM] Bỏ qua (sai định dạng): " + sub); continue; }
                    string parent = sub.Substring(0, i);
                    string leaf = sub.Substring(i + 1);

                    // Cách 1: chiếm quyền sở hữu rồi xoá (code)
                    try { RegistryHelper.ForceDeleteSubKey(Registry.LocalMachine, parent, leaf); } catch { }

                    // Cách 2 (dự phòng): dùng reg.exe delete nếu còn
                    if (KeyExists(sub))
                    {
                        string ro;
                        RunProc("reg.exe", "delete \"HKLM\\" + sub + "\" /f", out ro);
                    }

                    bool gone = !KeyExists(sub);
                    lines.Add((gone ? "[SYSTEM] Đã xoá: " : "[SYSTEM] VẪN KHÔNG xoá được: ") + sub);
                }
            }
            catch (Exception ex)
            {
                lines.Add("[SYSTEM] Lỗi: " + ex.Message);
            }
            try { File.WriteAllLines(resultPath, lines); } catch { }
        }

        private static string WhoAmI()
        {
            try { return WindowsIdentity.GetCurrent().Name; } catch { return "(không xác định)"; }
        }

        private static bool KeyExists(string hklmSub)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(hklmSub, false))
                    return k != null;
            }
            catch { return true; } // mở lỗi do quyền => coi như còn
        }

        private static void RunProc(string file, string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                }
            }
            catch (Exception ex) { output = ex.Message; }
        }
    }
}
