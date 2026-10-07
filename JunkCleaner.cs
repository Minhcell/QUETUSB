using System;
using System.Collections.Generic;
using System.IO;

namespace QuanLyHeThong
{
    internal class JunkLocation
    {
        public string Label;
        public string Path;
        public bool DefaultChecked;
        public bool IsRecycleBin;
    }

    internal static class JunkCleaner
    {
        public static List<JunkLocation> GetLocations()
        {
            string Env(string v) => Environment.GetEnvironmentVariable(v) ?? "";
            string win = Env("SystemRoot");
            string temp = Path.GetTempPath();
            string local = Env("LOCALAPPDATA");
            string appdata = Env("APPDATA");

            var list = new List<JunkLocation>
            {
                new JunkLocation { Label = "Temp người dùng (%TEMP%)", Path = temp, DefaultChecked = true },
                new JunkLocation { Label = "Temp cục bộ (%LOCALAPPDATA%\\Temp)", Path = Path.Combine(local, "Temp"), DefaultChecked = true },
                new JunkLocation { Label = "Temp hệ thống (Windows\\Temp)", Path = Path.Combine(win, "Temp"), DefaultChecked = true },
                new JunkLocation { Label = "Cache Office (OfficeFileCache)", Path = Path.Combine(local, @"Microsoft\Office\16.0\OfficeFileCache"), DefaultChecked = true },
                new JunkLocation { Label = "Office Unsaved Files", Path = Path.Combine(local, @"Microsoft\Office\UnsavedFiles"), DefaultChecked = false },
                new JunkLocation { Label = "Cache Internet (INetCache)", Path = Path.Combine(local, @"Microsoft\Windows\INetCache"), DefaultChecked = true },
                new JunkLocation { Label = "Báo lỗi treo (CrashDumps)", Path = Path.Combine(local, "CrashDumps"), DefaultChecked = true },
                new JunkLocation { Label = "File gần đây (Recent)", Path = Path.Combine(appdata, @"Microsoft\Windows\Recent"), DefaultChecked = false },
                new JunkLocation { Label = "Prefetch hệ thống", Path = Path.Combine(win, "Prefetch"), DefaultChecked = false },
                new JunkLocation { Label = "Cache Windows Update (Download)", Path = Path.Combine(win, @"SoftwareDistribution\Download"), DefaultChecked = false },
                new JunkLocation { Label = "Báo lỗi Windows (WER ReportQueue)", Path = Path.Combine(Env("ProgramData"), @"Microsoft\Windows\WER\ReportQueue"), DefaultChecked = false },
                new JunkLocation { Label = "Thùng rác (Recycle Bin)", Path = "(toàn bộ ổ đĩa)", DefaultChecked = true, IsRecycleBin = true },
            };
            return list;
        }

        /// <summary>Tính dung lượng của một thư mục (byte). Bỏ qua file/thư mục lỗi.</summary>
        public static long MeasureSize(string path)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(path)) return 0;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        /// <summary>Dọn một thư mục: xoá nội dung bên trong, giữ lại thư mục gốc. Trả về (số byte đã xoá, số mục đã xoá).</summary>
        public static (long bytes, int items) Clean(string path)
        {
            long bytes = 0; int items = 0;
            if (!Directory.Exists(path)) return (0, 0);

            // Xoá file trước
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        long size = 0;
                        try { size = new FileInfo(f).Length; } catch { }
                        try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                        File.Delete(f);
                        bytes += size; items++;
                    }
                    catch { /* file đang bị khoá -> bỏ qua */ }
                }
            }
            catch { }

            // Xoá thư mục con rỗng
            try
            {
                foreach (var d in Directory.EnumerateDirectories(path))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }

            return (bytes, items);
        }

        public static string FormatSize(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:0.##} {u[i]}";
        }
    }
}
