using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace QuanLyHeThong
{
    /// <summary>
    /// Xoá triệt để lịch sử file đã mở: Recent, Jump List (nguồn của mục "Recommended" trên Start
    /// và "Recent/Quick Access" trong File Explorer) và các khoá Registry lưu tên file gần đây.
    /// Vì Explorer giữ (khoá) các file này khi đang chạy, phải tạm tắt Explorer rồi bật lại.
    /// </summary>
    internal static class RecentHistoryCleaner
    {
        public static string ClearAll(Action<string> log)
        {
            string appdata = Environment.GetEnvironmentVariable("APPDATA") ?? "";
            string recent = Path.Combine(appdata, @"Microsoft\Windows\Recent");
            string autoDest = Path.Combine(recent, "AutomaticDestinations");
            string custDest = Path.Combine(recent, "CustomDestinations");

            bool killed = false;
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); killed = true; } catch { }
                }
                if (killed)
                {
                    Thread.Sleep(900);
                    log("Đã tạm tắt Explorer để xoá triệt để.");
                }
            }
            catch { }

            int files = 0;
            files += DeleteContents(autoDest);
            files += DeleteContents(custDest);
            files += DeleteContents(recent);
            log("Đã xoá " + files + " mục trong Recent / Jump List.");

            // Dọn các khoá Registry lưu tên file/đường dẫn gần đây (HKCU — không cần quyền cao)
            ClearRegKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", log, "RecentDocs (tài liệu gần đây)");
            ClearRegKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU", log, "RunMRU (lịch sử ô Run)");
            ClearRegKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths", log, "TypedPaths (địa chỉ đã gõ trong Explorer)");

            // Bật lại Explorer
            if (killed)
            {
                try
                {
                    string win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                    Process.Start(Path.Combine(win, "explorer.exe"));
                }
                catch { try { Process.Start("explorer.exe"); } catch { } }
                log("Đã mở lại Explorer.");
            }

            return "Hoàn tất xoá lịch sử file đã mở (Recent / Recommended / Quick Access) và dọn Registry liên quan.";
        }

        private static int DeleteContents(string dir)
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        private static void ClearRegKey(string subPath, Action<string> log, string label)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(subPath, true))
                {
                    if (k == null) return;
                    foreach (var vn in k.GetValueNames())
                        try { k.DeleteValue(vn, false); } catch { }
                    foreach (var sn in k.GetSubKeyNames())
                        try { k.DeleteSubKeyTree(sn, false); } catch { }
                }
                log("Đã dọn " + label + ".");
            }
            catch { }
        }
    }
}
