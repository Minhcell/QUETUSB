using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuanLyHeThong
{
    public class MainForm : Form
    {
        // ===== Tab 1: Thiết bị / Driver / Card mạng =====
        private ListView lvDevices;
        private Button btnScanDev, btnDeleteDev, btnClearUsbHist;
        private CheckBox chkCleanReg;
        private TextBox txtLog1;

        // ===== Tab 2: Dọn rác / Temp =====
        private CheckedListBox clbJunk;
        private List<JunkLocation> junkLocations;
        private Button btnMeasure, btnClean, btnClearRecent;
        private Label lblFreed;
        private TextBox txtLog3;

        // ===== Tab 3: Khoá / Mở cổng USB =====
        private Label lblUsbStatus;
        private Button btnLockUsb, btnUnlockUsb;

        public MainForm()
        {
            Text = "Quản Lý Hệ Thống — Thiết bị • Dọn rác • Khoá USB";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 640);
            Size = new Size(960, 680);
            Font = new Font("Segoe UI", 9f);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildTabDevices());
            tabs.TabPages.Add(BuildTabJunk());
            tabs.TabPages.Add(BuildTabUsb());

            var banner = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Color.FromArgb(33, 48, 74),
                ForeColor = Color.White,
                Text = IsAdmin()
                    ? "● Đang chạy với quyền Administrator"
                    : "● CẢNH BÁO: chưa có quyền Administrator — nhiều chức năng sẽ không chạy. Hãy chọn 'Run as administrator'."
            };

            Controls.Add(tabs);
            Controls.Add(banner);

            Load += delegate { RefreshUsbStatus(); };
        }

        // ============================================================
        // TAB 1 — Thiết bị / Driver / Card mạng
        // (Quét chỉ liệt kê; chỉ xoá khi bấm nút với các mục đã tích chọn)
        // ============================================================
        private TabPage BuildTabDevices()
        {
            var tab = new TabPage("1. USB & Wi-Fi");

            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            btnScanDev = new Button { Text = "Quét phát hiện", Left = 8, Top = 8, Width = 150, Height = 30 };
            btnScanDev.Click += async delegate { await ScanDevices(); };
            top.Controls.Add(btnScanDev);

            lvDevices = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                ShowGroups = true
            };
            lvDevices.Columns.Add("Loại", 110);
            lvDevices.Columns.Add("Tên", 360);
            lvDevices.Columns.Add("Chi tiết", 300);
            lvDevices.Columns.Add("Trạng thái", 90);
            lvDevices.Groups.Add(new ListViewGroup("usb", "USB cắm ngoài"));
            lvDevices.Groups.Add(new ListViewGroup("driver", "Driver Wi-Fi"));
            lvDevices.Groups.Add(new ListViewGroup("nic", "Card Wi-Fi"));

            var mid = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 6) };
            chkCleanReg = new CheckBox { Text = "Xoá cả trong Registry", Checked = true, AutoSize = true, Location = new Point(8, 12) };
            btnDeleteDev = new Button { Text = "Xoá mục đã chọn", Location = new Point(190, 7), Width = 150, Height = 30 };
            btnDeleteDev.Click += async delegate { await DeleteDevices(); };
            btnClearUsbHist = new Button { Text = "Xoá lịch sử USB từng cắm", Location = new Point(350, 7), Width = 200, Height = 30 };
            btnClearUsbHist.Click += async delegate { await ClearUsbHistory(); };
            mid.Controls.Add(chkCleanReg);
            mid.Controls.Add(btnDeleteDev);
            mid.Controls.Add(btnClearUsbHist);

            txtLog1 = MakeLog();
            var logHost = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(8, 0, 8, 8) };
            logHost.Controls.Add(txtLog1);

            tab.Controls.Add(lvDevices);
            tab.Controls.Add(mid);
            tab.Controls.Add(top);
            tab.Controls.Add(logHost);
            return tab;
        }

        private async Task ScanDevices()
        {
            SetBusy(true, btnScanDev, btnDeleteDev, btnClearUsbHist);
            Log(txtLog1, "Bắt đầu quét: USB cắm ngoài, card Wi-Fi, driver Wi-Fi...");
            lvDevices.Items.Clear();
            try
            {
                var all = await Task.Run(() =>
                {
                    var r = new List<SysItem>();
                    r.AddRange(SystemScanner.ScanUsb());
                    r.AddRange(SystemScanner.ScanDrivers());
                    r.AddRange(SystemScanner.ScanNics());
                    return r;
                });

                foreach (var it in all)
                {
                    var lvi = new ListViewItem(it.KindText);
                    lvi.SubItems.Add(it.Name);
                    lvi.SubItems.Add(it.Detail);
                    lvi.SubItems.Add(it.Status);
                    lvi.Tag = it;
                    lvi.Group = it.Kind == ItemKind.Usb ? lvDevices.Groups[0]
                              : it.Kind == ItemKind.Driver ? lvDevices.Groups[1]
                              : lvDevices.Groups[2];
                    lvDevices.Items.Add(lvi);
                }
                Log(txtLog1, "Quét xong. Tìm thấy " + all.Count + " mục. Tích chọn 1 hoặc nhiều mục rồi bấm 'Xoá mục đã chọn'.");
            }
            catch (Exception ex) { Log(txtLog1, "Lỗi: " + ex.Message); }
            finally { SetBusy(false, btnScanDev, btnDeleteDev, btnClearUsbHist); }
        }

        private async Task DeleteDevices()
        {
            var chosen = new List<SysItem>();
            foreach (ListViewItem lvi in lvDevices.Items)
                if (lvi.Checked && lvi.Tag is SysItem) chosen.Add((SysItem)lvi.Tag);

            if (chosen.Count == 0) { MessageBox.Show("Chưa chọn mục nào. Hãy tích chọn các mục cần xoá."); return; }

            var r = MessageBox.Show(
                "Sẽ GỠ/XOÁ " + chosen.Count + " mục đã chọn khỏi hệ thống" +
                (chkCleanReg.Checked ? " và xoá dấu vết trong Registry" : "") +
                ".\n\nLƯU Ý: xoá driver hoặc card mạng (gồm Wi-Fi) có thể làm mất kết nối thiết bị đó cho tới khi cài lại.\n\nTiếp tục?",
                "Xác nhận xoá", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            bool cleanReg = chkCleanReg.Checked;
            SetBusy(true, btnScanDev, btnDeleteDev, btnClearUsbHist);
            Log(txtLog1, "Đang xoá " + chosen.Count + " mục...");
            try
            {
                await Task.Run(() =>
                {
                    foreach (var it in chosen)
                        Log(txtLog1, SystemScanner.Remove(it, cleanReg));
                });
                Log(txtLog1, "Hoàn tất. Nên bấm 'Quét phát hiện' lại để cập nhật danh sách.");
            }
            catch (Exception ex) { Log(txtLog1, "Lỗi: " + ex.Message); }
            finally { SetBusy(false, btnScanDev, btnDeleteDev, btnClearUsbHist); }
        }

        private async Task ClearUsbHistory()
        {
            var r = MessageBox.Show(
                "Xoá toàn bộ lịch sử các USB từng cắm (USBSTOR) trong Registry?\nViệc này không ảnh hưởng USB đang cắm.",
                "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            SetBusy(true, btnScanDev, btnDeleteDev, btnClearUsbHist);
            try
            {
                string msg = await Task.Run(() => SystemScanner.ClearUsbStorHistory());
                Log(txtLog1, msg);
            }
            finally { SetBusy(false, btnScanDev, btnDeleteDev, btnClearUsbHist); }
        }

        // ============================================================
        // TAB 2 — Dọn rác / Temp
        // ============================================================
        private TabPage BuildTabJunk()
        {
            var tab = new TabPage("2. Dọn rác / Temp");

            junkLocations = JunkCleaner.GetLocations();
            clbJunk = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            foreach (var loc in junkLocations)
                clbJunk.Items.Add(loc.Label + "   [" + loc.Path + "]", loc.DefaultChecked);

            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            btnMeasure = new Button { Text = "Tính dung lượng", Location = new Point(8, 7), Width = 140, Height = 30 };
            btnMeasure.Click += async delegate { await MeasureJunk(); };
            btnClean = new Button { Text = "Dọn ngay", Location = new Point(158, 7), Width = 120, Height = 30 };
            btnClean.Click += async delegate { await CleanJunk(); };
            btnClearRecent = new Button { Text = "Xoá lịch sử file đã mở (Recent/Recommended)", Location = new Point(288, 7), Width = 290, Height = 30 };
            btnClearRecent.Click += async delegate { await ClearRecentHistory(); };
            lblFreed = new Label { Text = "", Location = new Point(588, 13), AutoSize = true, ForeColor = Color.DarkGreen, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            top.Controls.Add(btnMeasure);
            top.Controls.Add(btnClean);
            top.Controls.Add(btnClearRecent);
            top.Controls.Add(lblFreed);

            txtLog3 = MakeLog();
            var logHost = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(8, 0, 8, 8) };
            logHost.Controls.Add(txtLog3);

            tab.Controls.Add(clbJunk);
            tab.Controls.Add(top);
            tab.Controls.Add(logHost);
            return tab;
        }

        private async Task MeasureJunk()
        {
            SetBusy(true, btnMeasure, btnClean, btnClearRecent);
            Log(txtLog3, "Đang tính dung lượng...");
            long total = 0;
            try
            {
                for (int i = 0; i < junkLocations.Count; i++)
                {
                    if (!clbJunk.GetItemChecked(i)) continue;
                    var loc = junkLocations[i];
                    if (loc.IsRecycleBin) continue;
                    long sz = await Task.Run(() => JunkCleaner.MeasureSize(loc.Path));
                    total += sz;
                    Log(txtLog3, loc.Label + ": " + JunkCleaner.FormatSize(sz));
                }
                lblFreed.Text = "Ước tính: " + JunkCleaner.FormatSize(total);
                Log(txtLog3, "Tổng ước tính: " + JunkCleaner.FormatSize(total));
            }
            finally { SetBusy(false, btnMeasure, btnClean, btnClearRecent); }
        }

        private async Task ClearRecentHistory()
        {
            var r = MessageBox.Show(
                "Xoá triệt để lịch sử file đã mở (Recent, mục Recommended trên Start, Quick Access trong File Explorer) " +
                "và dọn các khoá Registry liên quan.\n\n" +
                "Thanh taskbar sẽ nháy một cái do Explorer khởi động lại — bình thường.\n\nTiếp tục?",
                "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            SetBusy(true, btnMeasure, btnClean, btnClearRecent);
            Log(txtLog3, "Bắt đầu xoá lịch sử file đã mở...");
            try
            {
                string msg = await Task.Run(() => RecentHistoryCleaner.ClearAll(line => Log(txtLog3, line)));
                Log(txtLog3, msg);
            }
            catch (Exception ex) { Log(txtLog3, "Lỗi: " + ex.Message); }
            finally { SetBusy(false, btnMeasure, btnClean, btnClearRecent); }
        }

        private async Task CleanJunk()
        {
            var r = MessageBox.Show("Dọn các mục đã chọn? File đang được dùng sẽ tự động bỏ qua.",
                "Xác nhận dọn rác", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            SetBusy(true, btnMeasure, btnClean, btnClearRecent);
            Log(txtLog3, "Bắt đầu dọn...");
            long totalBytes = 0; int totalItems = 0;
            try
            {
                for (int i = 0; i < junkLocations.Count; i++)
                {
                    if (!clbJunk.GetItemChecked(i)) continue;
                    var loc = junkLocations[i];
                    if (loc.IsRecycleBin)
                    {
                        await Task.Run(() => Native.EmptyRecycleBin());
                        Log(txtLog3, "Đã dọn Thùng rác.");
                        continue;
                    }
                    var res = await Task.Run(() => JunkCleaner.Clean(loc.Path));
                    totalBytes += res.bytes; totalItems += res.items;
                    Log(txtLog3, loc.Label + ": xoá " + res.items + " mục, " + JunkCleaner.FormatSize(res.bytes));
                }
                lblFreed.Text = "Đã giải phóng: " + JunkCleaner.FormatSize(totalBytes);
                Log(txtLog3, "HOÀN TẤT. Tổng cộng xoá " + totalItems + " mục, giải phóng " + JunkCleaner.FormatSize(totalBytes) + ".");
            }
            catch (Exception ex) { Log(txtLog3, "Lỗi: " + ex.Message); }
            finally { SetBusy(false, btnMeasure, btnClean, btnClearRecent); }
        }

        // ============================================================
        // TAB 3 — Khoá / Mở cổng USB
        // ============================================================
        private TabPage BuildTabUsb()
        {
            var tab = new TabPage("3. Khoá / Mở cổng USB");

            lblUsbStatus = new Label
            {
                Location = new Point(24, 24),
                AutoSize = true,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Text = "Trạng thái: (đang kiểm tra)"
            };

            btnLockUsb = new Button { Text = "🔒 KHOÁ cổng USB", Location = new Point(24, 70), Width = 200, Height = 46 };
            btnLockUsb.Click += async delegate { await ToggleUsb(true); };
            btnUnlockUsb = new Button { Text = "🔓 MỞ cổng USB", Location = new Point(240, 70), Width = 200, Height = 46 };
            btnUnlockUsb.Click += async delegate { await ToggleUsb(false); };

            var note = new Label
            {
                Location = new Point(24, 140),
                Size = new Size(820, 160),
                Text =
                    "• Chức năng này khoá/mở cổng USB đối với THIẾT BỊ LƯU TRỮ ngoài (USB, ổ cứng di động) bằng cách " +
                    "đổi giá trị dịch vụ USBSTOR trong Registry.\r\n\r\n" +
                    "• Bàn phím, chuột USB và các thiết bị khác KHÔNG bị ảnh hưởng — phù hợp để bảo mật, chống sao chép dữ liệu.\r\n\r\n" +
                    "• Khi KHOÁ: thiết bị lưu trữ cắm vào sẽ không nhận. Thiết bị đang cắm có thể cần rút ra cắm lại.\r\n\r\n" +
                    "• Khi MỞ: cho phép thiết bị lưu trữ USB hoạt động bình thường trở lại.",
                ForeColor = Color.FromArgb(60, 60, 60)
            };

            tab.Controls.Add(lblUsbStatus);
            tab.Controls.Add(btnLockUsb);
            tab.Controls.Add(btnUnlockUsb);
            tab.Controls.Add(note);
            return tab;
        }

        private async Task ToggleUsb(bool lockIt)
        {
            SetBusy(true, btnLockUsb, btnUnlockUsb);
            try
            {
                string msg = await Task.Run(() => lockIt ? UsbGuard.Lock() : UsbGuard.Unlock());
                MessageBox.Show(msg, "Kết quả", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                SetBusy(false, btnLockUsb, btnUnlockUsb);
                RefreshUsbStatus();
            }
        }

        private void RefreshUsbStatus()
        {
            bool? locked = UsbGuard.IsLocked();
            if (locked == null)
            {
                lblUsbStatus.Text = "Trạng thái: (không đọc được)";
                lblUsbStatus.ForeColor = Color.Gray;
            }
            else if (locked.Value)
            {
                lblUsbStatus.Text = "Trạng thái: 🔒 ĐANG KHOÁ (chặn USB lưu trữ)";
                lblUsbStatus.ForeColor = Color.Firebrick;
            }
            else
            {
                lblUsbStatus.Text = "Trạng thái: 🔓 ĐANG MỞ (cho phép USB lưu trữ)";
                lblUsbStatus.ForeColor = Color.SeaGreen;
            }
        }

        // ============================================================
        // Tiện ích chung
        // ============================================================
        private static bool IsAdmin()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private TextBox MakeLog()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9f)
            };
        }

        private void Log(TextBox box, string msg)
        {
            if (box.IsDisposed) return;
            if (box.InvokeRequired) { box.BeginInvoke(new Action(() => Log(box, msg))); return; }
            box.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
        }

        private void SetBusy(bool busy, params Control[] controls)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetBusy(busy, controls))); return; }
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            foreach (var c in controls) if (c != null) c.Enabled = !busy;
        }
    }
}
