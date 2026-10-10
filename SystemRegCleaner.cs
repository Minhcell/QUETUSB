using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
            string taskName = "QLHT_SysDel_" + id.Substring(0, 8);
            bool usedTask = false;
            bool pxTemp = false;
            string pxPath = null;

            try
            {
                // ===== BƯỚC 1: XOÁ BẰNG reg.exe (giống hệt xoá tay trong regedit) =====
                // reg.exe là công cụ gốc của Windows — xoá được thì y như regedit, tránh sai lệch của .NET.
                int adminOk = 0;
                var remaining = new List<string>();
                foreach (var p in hklmSubPaths)
                {
                    string o;
                    Run("reg.exe", "delete \"HKLM\\" + p + "\" /f", out o);
                    // còn thì thử thêm cách .NET chiếm quyền sở hữu
                    if (RegExists(p))
                    {
                        int i = p.LastIndexOf('\\');
                        if (i > 0)
                        {
                            try { RegistryHelper.ForceDeleteSubKey(Registry.LocalMachine, p.Substring(0, i), p.Substring(i + 1)); }
                            catch { }
                        }
                    }
                    if (!RegExists(p)) adminOk++;
                    else remaining.Add(p);
                }
                log("Xoá bằng Administrator (reg.exe): " + adminOk + "/" + hklmSubPaths.Count + " khoá.");

                if (remaining.Count == 0)
                    return "✔ Đã xoá sạch toàn bộ " + hklmSubPaths.Count + " khoá (bằng Administrator).";

                log("Còn " + remaining.Count + " khoá cần quyền SYSTEM, đang xử lý bằng PsExec...");

                // ===== BƯỚC 2: PHẦN CÒN SÓT -> dùng SYSTEM (PsExec) =====
                var reg = new StringBuilder();
                reg.AppendLine("Windows Registry Editor Version 5.00");
                reg.AppendLine();
                foreach (var p in remaining)
                    reg.AppendLine("[-HKEY_LOCAL_MACHINE\\" + p + "]");
                File.WriteAllText(regFile, reg.ToString(), Encoding.Unicode); // .reg chuẩn UTF-16

                string psexec = EnsurePsExec(log, out pxTemp);
                pxPath = psexec;
                if (psexec != null)
                {
                    // Ưu tiên PsExec (đã xác nhận chạy được). reg.exe nằm System32 nên không vướng chặn script Temp.
                    log("Dùng PsExec: " + psexec);

                    // 1) Xác nhận chạy đúng quyền SYSTEM
                    string who;
                    int wc = Run(psexec, "-accepteula -nobanner -s whoami", out who);
                    log("  PsExec whoami (mã " + wc + "): " + OneLine(who));

                    // 2) Nhập file .reg để xoá
                    string po;
                    int pc = Run(psexec, "-accepteula -nobanner -s reg import \"" + regFile + "\"", out po);
                    log("  PsExec reg import (mã " + pc + "):");
                    foreach (var line in (po ?? "").Split('\n'))
                        if (!string.IsNullOrWhiteSpace(line)) log("    " + line.Trim());
                    if (string.IsNullOrWhiteSpace(po))
                        log("    (không có output — PsExec có thể bị antivirus chặn)");
                }
                else
                {
                    log("KHÔNG tìm thấy PsExec cạnh app → dùng tác vụ SYSTEM (máy này có thể chặn).");
                    // Không có PsExec: tạo tác vụ SYSTEM gọi THẲNG reg.exe (System32), KHÔNG chạy script từ Temp.
                    usedTask = true;
                    log("Tạo tác vụ SYSTEM gọi reg import để xoá " + remaining.Count + " khoá...");
                    string createArgs = "/Create /TN \"" + taskName + "\" /TR \"reg import \\\"" + regFile +
                                        "\\\"\" /SC ONCE /ST 23:59 /RU SYSTEM /RL HIGHEST /F";
                    string cOut;
                    int cExit = Run("schtasks.exe", createArgs, out cOut);
                    if (cExit != 0)
                        return "Không tạo được tác vụ SYSTEM (schtasks mã " + cExit + "). " + FirstLine(cOut);

                    string dummy;
                    Run("schtasks.exe", "/Run /TN \"" + taskName + "\"", out dummy);

                    // Chờ: hễ khoá đầu tiên biến mất là coi như đang chạy (tối đa ~30 giây)
                    string firstKey = remaining[0];
                    for (int i = 0; i < 60; i++)
                    {
                        Thread.Sleep(500);
                        if (!KeyExists(firstKey)) break;
                    }
                    Thread.Sleep(1500);
                }

                // ===== BƯỚC 3: LƯỢT CUỐI reg.exe + .NET (sau khi PsExec có thể đã chiếm quyền sở hữu) =====
                foreach (var p in remaining)
                {
                    if (!RegExists(p)) continue;
                    string o;
                    Run("reg.exe", "delete \"HKLM\\" + p + "\" /f", out o);
                    if (RegExists(p))
                    {
                        int i = p.LastIndexOf('\\');
                        if (i > 0)
                        {
                            try { RegistryHelper.ForceDeleteSubKey(Registry.LocalMachine, p.Substring(0, i), p.Substring(i + 1)); }
                            catch { }
                        }
                    }
                }

                // Chờ 2 giây để phát hiện khoá bị Windows TẠO LẠI (thiết bị đang hiện diện)
                Thread.Sleep(2000);

                // Tập thiết bị đang hiện diện (để giải thích vì sao khoá quay lại)
                HashSet<string> present2;
                try { present2 = DeviceUninstaller.GetPresentInstanceIds(); }
                catch { present2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

                int remain = 0, recreated = 0;
                var sample = new List<string>();
                foreach (var p in hklmSubPaths)
                {
                    if (!RegExists(p)) continue;
                    remain++;
                    // instanceId = phần sau "...\Enum\"
                    string marker = @"CurrentControlSet\Enum\";
                    string inst = p;
                    int mi = p.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (mi >= 0) inst = p.Substring(mi + marker.Length);
                    bool isPresent = present2.Contains(inst);
                    if (isPresent) recreated++;
                    if (sample.Count < 6) sample.Add((isPresent ? "[ĐANG HOẠT ĐỘNG] " : "[cứng đầu] ") + p);
                }

                if (remain == 0)
                    return "✔ Đã xoá sạch toàn bộ " + hklmSubPaths.Count + " khoá (Administrator + SYSTEM).";

                foreach (var s in sample) log("  → còn: " + s);
                string msg = "Còn " + remain + "/" + hklmSubPaths.Count + " khoá chưa xoá.";
                if (recreated > 0)
                    msg += " Trong đó " + recreated + " khoá là THIẾT BỊ ĐANG HOẠT ĐỘNG/ĐANG CẮM — Windows tự tạo lại ngay, " +
                           "KHÔNG THỂ xoá khi thiết bị còn tồn tại trên máy (webcam, WiFi, modem, hub tích hợp, hoặc USB đang cắm). " +
                           "Muốn xoá USB cắm ngoài thì phải RÚT nó ra trước.";
                int stubborn = remain - recreated;
                if (stubborn > 0)
                    msg += " " + stubborn + " khoá còn lại bị TrustedInstaller khoá sâu" + (psexec == null ? " (thiếu PsExec)." : ".");
                return msg;
            }
            catch (Exception ex)
            {
                return "Lỗi khi chạy SYSTEM: " + ex.Message;
            }
            finally
            {
                if (usedTask) { try { string d2; Run("schtasks.exe", "/Delete /TN \"" + taskName + "\" /F", out d2); } catch { } }
                try { if (File.Exists(regFile)) File.Delete(regFile); } catch { }

                // XOÁ PsExec tạm (nếu do app giải nén) — không để lại file nào trong máy
                if (pxTemp && !string.IsNullOrEmpty(pxPath))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        try { if (File.Exists(pxPath)) File.Delete(pxPath); if (!File.Exists(pxPath)) break; }
                        catch { Thread.Sleep(500); } // PsExec có thể còn giữ file vài giây
                    }
                    try { if (File.Exists(pxPath)) log("  (PsExec tạm sẽ tự xoá lần chạy sau.)"); } catch { }
                }
            }
        }

        /// <summary>Tìm PsExec có sẵn (cùng thư mục app hoặc System32).</summary>
        private static string FindPsExec()
        {
            try
            {
                string dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string[] names = { "PsExec64.exe", "PsExec.exe", "psexec.exe" };
                foreach (var baseDir in new[] { dir, sys })
                {
                    if (string.IsNullOrEmpty(baseDir)) continue;
                    foreach (var n in names)
                    {
                        string full = Path.Combine(baseDir, n);
                        if (File.Exists(full)) return full;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Bảo đảm luôn có PsExec để chạy. Nếu máy đã có sẵn PsExec cạnh app/System32 thì dùng (extracted=false).
        /// Nếu không, GIẢI NÉN bản PsExec nhúng trong exe ra 1 FILE TẠM ngẫu nhiên (extracted=true) — dùng xong app tự xoá,
        /// không để lại file nào trong máy. Windows bắt buộc có file trên ổ mới chạy được tiến trình nên không thể chạy
        /// hoàn toàn trong bộ nhớ; cách này là "ẩn" nhất có thể.
        /// </summary>
        private static string EnsurePsExec(Action<string> log, out bool extracted)
        {
            extracted = false;
            string found = FindPsExec();
            if (found != null) { log("Dùng PsExec có sẵn: " + found); return found; }

            // Giải nén ra file tạm ngẫu nhiên
            string tempExe = Path.Combine(Path.GetTempPath(), "qlht_px_" + Guid.NewGuid().ToString("N") + ".exe");
            if (TryExtractPsExec(tempExe))
            {
                extracted = true;
                log("PsExec (nhúng trong app) chạy tạm từ: " + tempExe + " (sẽ tự xoá sau khi xong).");
                return tempExe;
            }

            // Dự phòng: giải nén cạnh app
            try
            {
                string t2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PsExec.exe");
                if (TryExtractPsExec(t2)) { log("PsExec giải nén cạnh app: " + t2); return t2; }
            }
            catch { }

            log("KHÔNG lấy được PsExec (bản nhúng lỗi?).");
            return null;
        }

        private static bool TryExtractPsExec(string target)
        {
            try
            {
                if (File.Exists(target) && new FileInfo(target).Length > 100000) return true; // đã có sẵn, dùng luôn
                var asm = Assembly.GetExecutingAssembly();
                string resName = null;
                foreach (var n in asm.GetManifestResourceNames())
                    if (n.EndsWith("PsExec.exe", StringComparison.OrdinalIgnoreCase)) { resName = n; break; }
                if (resName == null) return false;
                using (var s = asm.GetManifestResourceStream(resName))
                {
                    if (s == null) return false;
                    using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write))
                        s.CopyTo(fs);
                }
                return File.Exists(target) && new FileInfo(target).Length > 100000;
            }
            catch { return false; }
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
                    p.WaitForExit(90000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "(trống)";
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
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

        /// <summary>Kiểm tra khoá có tồn tại không bằng reg.exe (đúng view như regedit, tránh sai lệch của .NET).</summary>
        private static bool RegExists(string hklmSub)
        {
            try
            {
                string o;
                int code = Run("reg.exe", "query \"HKLM\\" + hklmSub + "\"", out o);
                return code == 0; // 0 = tồn tại, khác 0 = không tồn tại
            }
            catch { return true; }
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
