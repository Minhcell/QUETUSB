using System;
using System.Windows.Forms;

namespace QuanLyHeThong
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Chế độ chạy ngầm dưới quyền SYSTEM (do Task Scheduler gọi): xoá Registry rồi thoát, không mở giao diện.
            if (args != null && args.Length >= 3 && args[0] == SystemRegCleaner.SysArg)
            {
                SystemRegCleaner.RunSystemWorker(args[1], args[2]);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
