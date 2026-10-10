using System;
using System.Diagnostics;
using System.Security.Principal;
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

            // Bảo đảm chạy với quyền Administrator: nếu chưa, tự khởi động lại ở chế độ nâng quyền.
            if (!IsAdmin())
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Process.GetCurrentProcess().MainModule.FileName,
                        UseShellExecute = true,
                        Verb = "runas" // kích hoạt UAC nâng quyền
                    };
                    Process.Start(psi);
                    return; // thoát bản chưa nâng quyền
                }
                catch
                {
                    MessageBox.Show(
                        "Phần mềm cần quyền Administrator để xoá Registry. Hãy chuột phải vào file và chọn 'Run as administrator'.",
                        "Cần quyền Administrator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    // vẫn mở app để xem, nhưng xoá sẽ không được
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static bool IsAdmin()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
