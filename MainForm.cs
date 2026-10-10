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
        private Label lblUsbStatus, lblPolicyStatus;
        private Button btnLockUsb, btnUnlockUsb;
        private Button btnDenyAll, btnAllowPolicy, btnReadOnly, btnReadWrite;

        // ===== Tab 4: USB chi tiết (kiểu USBDeview) =====
        private ListView lvUsbHist;
        private Button btnScanUsbHist, btnDeleteUsbHist, btnCheckAllUsbHist, btnSweepHklm;
        private CheckBox chkOnlyHistory, chkProtectSystem;
        private TextBox txtLog4;

        // ===== Tab 5: Kích hoạt Windows / Office (chỉ kiểm tra) =====
        private Button btnCheckActivation, btnCheckOffice;
        private Label lblActivation;
        private TextBox txtActivation;

        public MainForm()
        {
            Text = "Quản Lý Hệ Thống — Thiết bị • Dọn rác • Khoá USB";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 640);
            Size = new Size(960, 680);
            Font = new Font("Segoe UI", 9f);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            // Vẽ tab kiểu 3D, tab đang chọn nổi màu để dễ nhận biết
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.Multiline = true;
            tabs.ItemSize = new Size(178, 34);
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem += TabsDrawItem;
            tabs.TabPages.Add(BuildTabDevices());
            tabs.TabPages.Add(BuildTabUsbHistory());
            tabs.TabPages.Add(BuildTabJunk());
            tabs.TabPages.Add(BuildTabUsb());
            tabs.TabPages.Add(BuildTabActivation());

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
            lvDevices.Groups.Add(new ListViewGroup("driver", "Driver USB & Wi-Fi"));
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

                    if (cleanReg)
                    {
                        // Xoá triệt để bằng quyền SYSTEM cho các khoá Enum còn sót (USB / card)
                        var subPaths = new List<string>();
                        foreach (var it in chosen)
                            if (!string.IsNullOrEmpty(it.InstanceId))
                                subPaths.Add(@"SYSTEM\CurrentControlSet\Enum\" + it.InstanceId);
                        if (subPaths.Count > 0)
                            Log(txtLog1, SystemRegCleaner.DeleteKeysAsSystem(subPaths, line => Log(txtLog1, line)));
                    }
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
            Log(txtLog1, "Đang xoá lịch sử USBSTOR bằng quyền SYSTEM...");
            try
            {
                await Task.Run(() =>
                {
                    var paths = SystemScanner.GetUsbStorSubPaths();
                    if (paths.Count == 0) { Log(txtLog1, "Không có mục USBSTOR nào."); return; }
                    // Thử xoá kiểu Administrator trước (nhanh), phần còn sót đẩy sang SYSTEM
                    SystemScanner.ClearUsbStorHistory();
                    Log(txtLog1, SystemRegCleaner.DeleteKeysAsSystem(paths, line => Log(txtLog1, line)));
                });
            }
            finally { SetBusy(false, btnScanDev, btnDeleteDev, btnClearUsbHist); }
        }

        // ============================================================
        // TAB 2 — USB chi tiết kiểu USBDeview (đang cắm + lịch sử)
        // ============================================================
        private TabPage BuildTabUsbHistory()
        {
            var tab = new TabPage("2. USB chi tiết (USBDeview)");

            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            btnScanUsbHist = new Button { Text = "Quét USB (đang cắm + lịch sử)", Location = new Point(8, 7), Width = 220, Height = 30 };
            btnScanUsbHist.Click += async delegate { await ScanUsbHistory(); };
            chkOnlyHistory = new CheckBox { Text = "Chỉ hiện USB lịch sử (không cắm)", AutoSize = true, Location = new Point(238, 13) };
            chkOnlyHistory.CheckedChanged += delegate { ApplyUsbHistFilter(); };
            chkProtectSystem = new CheckBox { Text = "Bảo vệ thiết bị hệ thống (hub/chuột/phím/camera) — không xoá", AutoSize = true, Location = new Point(470, 13), Checked = true };
            top.Controls.Add(btnScanUsbHist);
            top.Controls.Add(chkOnlyHistory);
            top.Controls.Add(chkProtectSystem);

            lvUsbHist = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true
            };
            lvUsbHist.Columns.Add("Mô tả", 300);
            lvUsbHist.Columns.Add("VID/PID", 160);
            lvUsbHist.Columns.Add("Số seri / Instance", 220);
            lvUsbHist.Columns.Add("Loại", 80);
            lvUsbHist.Columns.Add("Hiện diện", 90);
            lvUsbHist.Columns.Add("Thời gian cắm", 130);
            lvUsbHist.Columns.Add("Phân loại", 150);

            var mid = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 6) };
            btnCheckAllUsbHist = new Button { Text = "Chọn/Bỏ tất cả", Location = new Point(8, 7), Width = 130, Height = 30 };
            btnCheckAllUsbHist.Click += delegate { ToggleAllUsbHist(); };
            btnDeleteUsbHist = new Button { Text = "Xoá mục đã chọn (+ Registry)", Location = new Point(148, 7), Width = 220, Height = 30 };
            btnDeleteUsbHist.Click += async delegate { await DeleteUsbHistory(); };
            btnSweepHklm = new Button { Text = "Kiểm tra & xoá toàn bộ lịch sử USB cắm ngoài (HKLM)", Location = new Point(378, 7), Width = 340, Height = 30 };
            btnSweepHklm.Click += async delegate { await SweepHklmHistory(); };
            mid.Controls.Add(btnCheckAllUsbHist);
            mid.Controls.Add(btnDeleteUsbHist);
            mid.Controls.Add(btnSweepHklm);

            txtLog4 = MakeLog();
            var logHost = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(8, 0, 8, 8) };
            logHost.Controls.Add(txtLog4);

            tab.Controls.Add(lvUsbHist);
            tab.Controls.Add(mid);
            tab.Controls.Add(top);
            tab.Controls.Add(logHost);
            return tab;
        }

        private List<UsbRecord> usbHistAll = new List<UsbRecord>();

        private async Task ScanUsbHistory()
        {
            SetBusy(true, btnScanUsbHist, btnDeleteUsbHist, btnCheckAllUsbHist);
            Log(txtLog4, "Đang quét USB ngoại vi (đang cắm + đã từng cắm) từ Registry...");
            try
            {
                usbHistAll = await Task.Run(() => UsbHistoryScanner.ScanAll());
                ApplyUsbHistFilter();
                int present = 0;
                foreach (var r in usbHistAll) if (r.Present) present++;
                Log(txtLog4, "Quét xong: tổng " + usbHistAll.Count + " thiết bị (" + present + " đang cắm, " +
                    (usbHistAll.Count - present) + " lịch sử). Tích chọn rồi bấm 'Xoá mục đã chọn'.");
            }
            catch (Exception ex) { Log(txtLog4, "Lỗi: " + ex.Message); }
            finally { SetBusy(false, btnScanUsbHist, btnDeleteUsbHist, btnCheckAllUsbHist); }
        }

        private void ApplyUsbHistFilter()
        {
            if (lvUsbHist == null) return;
            bool onlyHist = chkOnlyHistory.Checked;
            lvUsbHist.BeginUpdate();
            lvUsbHist.Items.Clear();
            foreach (var r in usbHistAll)
            {
                if (onlyHist && r.Present) continue;
                var lvi = new ListViewItem(r.Description);
                lvi.SubItems.Add(r.VidPid);
                lvi.SubItems.Add(r.Serial);
                lvi.SubItems.Add(r.Type);
                lvi.SubItems.Add(r.Present ? "● Đang cắm" : "Lịch sử");
                lvi.SubItems.Add(string.IsNullOrEmpty(r.TimeInfo) ? "—" : r.TimeInfo);
                lvi.SubItems.Add(r.Protected ? "⚠ Hệ thống (giữ)" : "Cắm ngoài");
                lvi.Tag = r;
                if (r.Present)
                    lvi.BackColor = System.Drawing.Color.FromArgb(220, 245, 220); // xanh lá nhạt = đang hoạt động
                else
                    lvi.ForeColor = System.Drawing.Color.Gray;                    // lịch sử = xám nhạt
                if (r.Protected) lvi.ForeColor = System.Drawing.Color.DarkGray;
                lvUsbHist.Items.Add(lvi);
            }
            lvUsbHist.EndUpdate();
        }

        private void ToggleAllUsbHist()
        {
            bool anyUnchecked = false;
            foreach (ListViewItem lvi in lvUsbHist.Items) if (!lvi.Checked) { anyUnchecked = true; break; }
            foreach (ListViewItem lvi in lvUsbHist.Items) lvi.Checked = anyUnchecked;
        }

        private async Task DeleteUsbHistory()
        {
            var chosen = new List<UsbRecord>();
            int skipped = 0;
            // Lấy các mục được TICK hoặc được BÔI ĐEN (kéo chuột chọn vùng)
            foreach (ListViewItem lvi in lvUsbHist.Items)
            {
                if (!(lvi.Tag is UsbRecord)) continue;
                if (!lvi.Checked && !lvi.Selected) continue;
                var rec = (UsbRecord)lvi.Tag;
                if (chkProtectSystem.Checked && rec.Protected) { skipped++; continue; }
                chosen.Add(rec);
            }

            if (chosen.Count == 0)
            {
                MessageBox.Show(skipped > 0
                    ? "Các mục đã chọn đều là thiết bị hệ thống (hub/chuột/phím/camera) đang được bảo vệ nên không xoá. " +
                      "Nếu thực sự muốn xoá, bỏ tích ô 'Bảo vệ thiết bị hệ thống'."
                    : "Chưa chọn mục nào. Hãy TICK ô vuông, hoặc KÉO CHUỘT bôi đen các dòng cần xoá.");
                return;
            }

            var r = MessageBox.Show(
                "Sẽ xoá " + chosen.Count + " thiết bị USB cắm ngoài đã chọn và xoá dấu vết trong Registry." +
                (skipped > 0 ? "\n(Đã bỏ qua " + skipped + " thiết bị hệ thống được bảo vệ.)" : "") +
                "\n\nLưu ý: KHÔNG xoá driver — cắm lại Windows tự nhận, không phải cài lại.\n\nTiếp tục?",
                "Xác nhận xoá", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            SetBusy(true, btnScanUsbHist, btnDeleteUsbHist, btnCheckAllUsbHist);
            Log(txtLog4, "Đang xoá " + chosen.Count + " thiết bị...");
            try
            {
                await Task.Run(() =>
                {
                    foreach (var rec in chosen)
                        Log(txtLog4, UsbHistoryScanner.Remove(rec));

                    // Xoá triệt để bằng quyền SYSTEM cho các khoá còn sót (mọi nhánh)
                    var subPaths = new List<string>();
                    foreach (var rec in chosen)
                        if (!string.IsNullOrEmpty(rec.FullKeyPath))
                            subPaths.Add(rec.FullKeyPath);
                    if (subPaths.Count > 0)
                        Log(txtLog4, SystemRegCleaner.DeleteKeysAsSystem(subPaths, line => Log(txtLog4, line)));
                });
                Log(txtLog4, "Hoàn tất. Nên quét lại để cập nhật danh sách.");
            }
            catch (Exception ex) { Log(txtLog4, "Lỗi: " + ex.Message); }
            finally { SetBusy(false, btnScanUsbHist, btnDeleteUsbHist, btnCheckAllUsbHist); }
        }

        private async Task SweepHklmHistory()
        {
            SetBusy(true, btnScanUsbHist, btnDeleteUsbHist, btnCheckAllUsbHist, btnSweepHklm);
            Log(txtLog4, "Đang kiểm tra toàn bộ HKLM tìm lịch sử USB cắm ngoài...");
            try
            {
                var paths = await Task.Run(() => UsbHistoryScanner.CollectExternalHistoryHklm());
                if (paths.Count == 0)
                {
                    Log(txtLog4, "✔ Không tìm thấy lịch sử USB cắm ngoài nào trong HKLM.");
                    MessageBox.Show("Không có lịch sử USB cắm ngoài nào trong HKLM.", "Kết quả",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var r = MessageBox.Show(
                    "Tìm thấy " + paths.Count + " mục lịch sử USB cắm ngoài trong HKLM " +
                    "(USB, USBSTOR, điện thoại/MTP, thiết bị di động...).\n\n" +
                    "Sẽ xoá toàn bộ. KHÔNG xoá driver USB và KHÔNG xoá driver/card WiFi " +
                    "(đã tự bỏ qua hub, chuột, phím, camera, bluetooth, card mạng).\n\nTiếp tục?",
                    "Xác nhận xoá lịch sử USB (HKLM)", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;

                await Task.Run(() =>
                {
                    Log(txtLog4, "Xoá " + paths.Count + " mục bằng quyền SYSTEM...");
                    Log(txtLog4, SystemRegCleaner.DeleteKeysAsSystem(paths, line => Log(txtLog4, line)));
                });
                Log(txtLog4, "Hoàn tất. Nên quét lại để cập nhật danh sách.");
            }
            catch (Exception ex) { Log(txtLog4, "Lỗi: " + ex.Message); }
            finally { SetBusy(false, btnScanUsbHist, btnDeleteUsbHist, btnCheckAllUsbHist, btnSweepHklm); }
        }

        // ============================================================
        // TAB 3 — Dọn rác / Temp
        // ============================================================
        private TabPage BuildTabJunk()
        {
            var tab = new TabPage("3. Dọn rác / Temp");

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
            var tab = new TabPage("4. Khoá / Mở cổng USB");

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
                Location = new Point(24, 128),
                Size = new Size(860, 60),
                Text =
                    "• Mức 1 (USBSTOR): khoá/mở USB lưu trữ ngoài; chuột, bàn phím USB KHÔNG bị ảnh hưởng.\r\n" +
                    "• Thiết bị đang cắm có thể cần rút ra cắm lại để áp dụng.",
                ForeColor = Color.FromArgb(60, 60, 60)
            };

            // ----- Mức mạnh hơn: Group Policy + Write Protect -----
            var lblStrong = new Label
            {
                Location = new Point(24, 196),
                AutoSize = true,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Text = "Mức mạnh hơn (chặn cả điện thoại, thẻ nhớ / chống copy ra):"
            };

            btnDenyAll = new Button { Text = "⛔ CHẶN toàn bộ thiết bị di động", Location = new Point(24, 228), Width = 260, Height = 44 };
            btnDenyAll.Click += async delegate { await DoPolicy(() => UsbGuard.SetDenyAll(true)); };
            btnAllowPolicy = new Button { Text = "✔ BỎ chặn (toàn bộ)", Location = new Point(300, 228), Width = 180, Height = 44 };
            btnAllowPolicy.Click += async delegate { await DoPolicy(() => UsbGuard.SetDenyAll(false)); };

            btnReadOnly = new Button { Text = "📄 Chỉ ĐỌC (cấm copy ra)", Location = new Point(24, 284), Width = 260, Height = 44 };
            btnReadOnly.Click += async delegate { await DoPolicy(() => UsbGuard.SetWriteProtect(true)); };
            btnReadWrite = new Button { Text = "✏ Cho GHI lại", Location = new Point(300, 284), Width = 180, Height = 44 };
            btnReadWrite.Click += async delegate { await DoPolicy(() => UsbGuard.SetWriteProtect(false)); };

            lblPolicyStatus = new Label
            {
                Location = new Point(24, 340),
                Size = new Size(860, 70),
                ForeColor = Color.FromArgb(60, 60, 60),
                Text = "Trạng thái nâng cao: (đang kiểm tra)"
            };

            tab.Controls.Add(lblUsbStatus);
            tab.Controls.Add(btnLockUsb);
            tab.Controls.Add(btnUnlockUsb);
            tab.Controls.Add(note);
            tab.Controls.Add(lblStrong);
            tab.Controls.Add(btnDenyAll);
            tab.Controls.Add(btnAllowPolicy);
            tab.Controls.Add(btnReadOnly);
            tab.Controls.Add(btnReadWrite);
            tab.Controls.Add(lblPolicyStatus);
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

        private async Task DoPolicy(Func<string> action)
        {
            SetBusy(true, btnDenyAll, btnAllowPolicy, btnReadOnly, btnReadWrite);
            try
            {
                string msg = await Task.Run(action);
                MessageBox.Show(msg, "Kết quả", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                SetBusy(false, btnDenyAll, btnAllowPolicy, btnReadOnly, btnReadWrite);
                RefreshUsbStatus();
            }
        }

        private void RefreshUsbStatus()
        {
            bool? denyAll = UsbGuard.IsDenyAll();
            bool? readOnly = UsbGuard.IsWriteProtect();
            if (lblPolicyStatus != null)
            {
                string s1 = denyAll == true ? "⛔ Đang CHẶN toàn bộ thiết bị di động" : "Không chặn toàn bộ";
                string s2 = readOnly == true ? "📄 USB đang CHỈ ĐỌC (chống copy)" : "Cho ghi bình thường";
                lblPolicyStatus.Text = "Trạng thái nâng cao:\r\n• " + s1 + "\r\n• " + s2;
                lblPolicyStatus.ForeColor = (denyAll == true || readOnly == true) ? Color.Firebrick : Color.SeaGreen;
            }

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
        // TAB 5 — Kiểm tra trạng thái kích hoạt Windows (chỉ đọc)
        // ============================================================
        private TabPage BuildTabActivation()
        {
            var tab = new TabPage("5. Kích hoạt Windows / Office");

            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            btnCheckActivation = new Button { Text = "Kiểm tra Windows", Location = new Point(8, 7), Width = 180, Height = 30 };
            btnCheckActivation.Click += async delegate { await CheckActivation(); };
            btnCheckOffice = new Button { Text = "Kiểm tra Office", Location = new Point(198, 7), Width = 180, Height = 30 };
            btnCheckOffice.Click += async delegate { await CheckOffice(); };
            top.Controls.Add(btnCheckActivation);
            top.Controls.Add(btnCheckOffice);

            lblActivation = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(10, 8, 0, 0),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Text = "Trạng thái: (bấm Kiểm tra)"
            };

            txtActivation = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9.5f)
            };

            var note = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                Padding = new Padding(10, 6, 10, 6),
                ForeColor = Color.FromArgb(60, 60, 60),
                Text = "Tab này chỉ KIỂM TRA, không thay đổi gì. Nếu máy chưa kích hoạt, hãy vào " +
                       "Settings → System → Activation để nhập key hoặc kích hoạt qua máy chủ KMS của cơ quan."
            };

            var fill = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            fill.Controls.Add(txtActivation);

            tab.Controls.Add(fill);
            tab.Controls.Add(note);
            tab.Controls.Add(lblActivation);
            tab.Controls.Add(top);
            return tab;
        }

        private async Task CheckActivation()
        {
            SetBusy(true, btnCheckActivation, btnCheckOffice);
            txtActivation.Text = "Đang kiểm tra Windows...";
            try
            {
                string report = await Task.Run(() => WindowsActivation.GetStatus());
                bool? act = await Task.Run(() => WindowsActivation.IsActivated());
                txtActivation.Text = report.Replace("\n", Environment.NewLine);
                if (act == true)
                {
                    lblActivation.Text = "Trạng thái: ✔ Windows ĐÃ kích hoạt";
                    lblActivation.ForeColor = Color.SeaGreen;
                }
                else if (act == false)
                {
                    lblActivation.Text = "Trạng thái: ✘ Windows CHƯA kích hoạt";
                    lblActivation.ForeColor = Color.Firebrick;
                }
                else
                {
                    lblActivation.Text = "Trạng thái: (không xác định)";
                    lblActivation.ForeColor = Color.Gray;
                }
            }
            catch (Exception ex) { txtActivation.Text = "Lỗi: " + ex.Message; }
            finally { SetBusy(false, btnCheckActivation, btnCheckOffice); }
        }

        private async Task CheckOffice()
        {
            SetBusy(true, btnCheckActivation, btnCheckOffice);
            txtActivation.Text = "Đang kiểm tra Office...";
            try
            {
                string report = await Task.Run(() => OfficeActivation.GetStatus());
                txtActivation.Text = ("=== TRẠNG THÁI OFFICE ===\n\n" + report).Replace("\n", Environment.NewLine);
                lblActivation.Text = "Trạng thái: xem chi tiết Office bên dưới";
                lblActivation.ForeColor = Color.FromArgb(33, 48, 74);
            }
            catch (Exception ex) { txtActivation.Text = "Lỗi: " + ex.Message; }
            finally { SetBusy(false, btnCheckActivation, btnCheckOffice); }
        }

        // ============================================================
        // Tiện ích chung
        // ============================================================
        // Vẽ tab: tab đang chọn nổi 3D màu xanh, tab khác phẳng xám
        private void TabsDrawItem(object sender, DrawItemEventArgs e)
        {
            var tc = (TabControl)sender;
            var r = tc.GetTabRect(e.Index);
            bool selected = (e.Index == tc.SelectedIndex);
            Color back = selected ? Color.FromArgb(33, 118, 255) : Color.FromArgb(226, 229, 236);
            Color fore = selected ? Color.White : Color.FromArgb(50, 50, 50);
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, r);
            ControlPaint.DrawBorder3D(e.Graphics, r,
                selected ? Border3DStyle.RaisedInner : Border3DStyle.SunkenOuter);
            var text = tc.TabPages[e.Index].Text;
            TextRenderer.DrawText(e.Graphics, text, tc.Font, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
