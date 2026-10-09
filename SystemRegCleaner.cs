using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
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
            string regFile = Path.Combine(tmp, "qlht_del_" + id + ".reg");
            string result = Path.Combine(tmp, "qlht_result_" + id + ".txt");
            string wrapper = Path.Combine(tmp, "qlht_run_" + id + ".cmd");
            string taskName = "QLHT_SysDel_" + id.Substring(0, 8);

            try
            {
                if (File.Exists(result)) File.Delete(result);

                // 1) File .reg xoá TẤT CẢ khoá trong 1 lần (dòng [-HKLM\...] = xoá khoá đó + con)
                var reg = new StringBuilder();
                reg.AppendLine("Windows Registry Editor Version 5.00");
                reg.AppendLine();
                foreach (var p in hklmSubPaths)
                    reg.AppendLine("[-HKEY_LOCAL_MACHINE\\" + p + "]");
                File.WriteAllText(regFile, reg.ToString(), Encoding.Unicode); // .reg chuẩn UTF-16

                // 2) File .cmd chạy dưới SYSTEM: whoami + reg import (chỉ 1 tiến trình reg.exe)
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("echo ===QLHT_WHOAMI=== > \"" + result + "\"");
                sb.AppendLine("whoami >> \"" + result + "\" 2>&1");
                sb.AppendLine("echo ===QLHT_IMPORT=== >> \"" + result + "\"");
                sb.AppendLine("reg import \"" + regFile + "\" >> \"" + result + "\" 2>&1");
                sb.AppendLine("echo ===QLHT_DONE=== >> \"" + result + "\"");
                File.WriteAllText(wrapper, sb.ToString(), Encoding.Default);

                log("Tạo tác vụ SYSTEM để xoá " + hklmSubPaths.Count + " khoá Registry (gộp 1 lần)...");

                string createArgs = "/Create /TN \"" + taskName + "\" /TR \"\\\"" + wrapper +
                                    "\\\"\" /SC ONCE /ST 23:59 /RU SYSTEM /RL HIGHEST /F";
                string cOut;
                int cExit = Run("schtasks.exe", createArgs, out cOut);
                if (cExit != 0)
                    return "Không tạo được tác vụ SYSTEM (schtasks mã " + cExit + "). " + FirstLine(cOut);

                string dummy;
                Run("schtasks.exe", "/Run /TN \"" + taskName + "\"", out dummy);

                // Chờ dấu kết thúc (tối đa ~60 giây)
                bool done = false;
                for (int i = 0; i < 200; i++)
                {
                    Thread.Sleep(300);
                    if (File.Exists(result) && SafeRead(result).Contains("===QLHT_DONE===")) { done = true; break; }
                }

                // LUÔN hiện nội dung file kết quả (kể cả khi hết giờ) để chẩn đoán
                string txt = SafeRead(result);
                if (string.IsNullOrWhiteSpace(txt))
                    log("  (Tác vụ SYSTEM KHÔNG tạo ra kết quả — nhiều khả năng bị phần mềm bảo mật chặn.)");
                else
                    foreach (var line in txt.Split('\n'))
                        if (!string.IsNullOrWhiteSpace(line)) log("  " + line.Trim());

                // Tự kiểm tra lại: còn khoá nào chưa xoá?
                int remain = 0;
                var sample = new List<string>();
                foreach (var p in hklmSubPaths)
                    if (KeyExists(p)) { remain++; if (sample.Count < 5) sample.Add(p); }

                if (remain == 0)
                    return "✔ Đã xoá sạch toàn bộ " + hklmSubPaths.Count + " khoá bằng quyền SYSTEM.";

                string msg = "Còn " + remain + "/" + hklmSubPaths.Count + " khoá CHƯA xoá được.";
                if (!done) msg += " (Tác vụ SYSTEM hết giờ hoặc bị chặn.)";
                foreach (var s in sample) log("  → còn: " + s);
                return msg;
            }
            catch (Exception ex)
            {
                return "Lỗi khi chạy SYSTEM: " + ex.Message;
            }
            finally
            {
                try { string d2; Run("schtasks.exe", "/Delete /TN \"" + taskName + "\" /F", out d2); } catch { }
                try { if (File.Exists(regFile)) File.Delete(regFile); } catch { }
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

        private static string SafeRead(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
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
