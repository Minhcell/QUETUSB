# Quản Lý Hệ Thống

Tiện ích quản trị máy tính Windows (.exe), **chạy từ Windows 7 (SP1) đến Windows 11**.
Nền tảng **.NET Framework 4.5.2**. Cần chạy bằng **quyền Administrator** (phần mềm tự xin quyền khi mở).

## Phần mềm chạy HOÀN TOÀN OFFLINE
Mọi chức năng (quét thiết bị, gỡ driver, dọn rác, khoá USB) đều chạy nội bộ trên máy, **không gọi mạng**.
Máy chạy phần mềm **không cần internet**. Chỉ việc *build ra file .exe* mới cần mạng — và việc đó làm trên
**PC có mạng**, sau đó chép file .exe qua **USB** sang PC offline.

## 3 chức năng
1. **Thiết bị / Driver / Card mạng** — Quét (chỉ liệt kê, không tự xoá) USB cắm ngoài, gói driver bên thứ ba
   (gồm driver Wi-Fi), card mạng (gồm card Wi-Fi). Tích chọn 1 hoặc nhiều mục rồi bấm **Xoá mục đã chọn**.
   Gỡ bằng Windows SetupAPI (API gốc Microsoft), có tuỳ chọn xoá luôn dấu vết trong Registry và xoá lịch sử USBSTOR.
2. **Dọn rác / Temp** — Temp, cache Office, INetCache, CrashDumps, Prefetch, cache Windows Update, Thùng rác...
3. **Khoá / Mở cổng USB** — với thiết bị lưu trữ ngoài (USBSTOR); chuột, bàn phím USB không bị ảnh hưởng.

---

## QUY TRÌNH DÙNG CHO MÁY OFFLINE (làm 1 lần)

### Bước A — Build file .exe trên PC CÓ MẠNG (qua GitHub, không cần cài gì)
1. Đẩy toàn bộ mã nguồn này lên 1 repo GitHub (xem phần "Tải mã nguồn lên GitHub" bên dưới).
2. GitHub tự chạy → vào tab **Actions**, chờ chấm xanh ✓ (2–4 phút).
3. Mở lần chạy đó → kéo xuống **Artifacts** → tải **QuanLyHeThong-win** → giải nén lấy **`QuanLyHeThong.exe`**.

### Bước B — Chép sang PC OFFLINE
- Chép **`QuanLyHeThong.exe`** vào USB → cắm sang PC offline → copy ra ổ cứng.
- Chuột phải vào file → **Run as administrator**. Xong — chạy luôn, không cần mạng.

### Nếu PC offline là Win7 và báo thiếu .NET Framework
Hầu hết máy Win7 đang dùng đã có sẵn .NET 4.x. Nếu máy báo thiếu:
1. Trên **PC có mạng**, tải bộ cài **.NET Framework 4.5.2 (hoặc 4.8) bản OFFLINE** từ trang Microsoft
   (gói "offline installer", 1 file ~60–110 MB).
2. Chép file đó qua USB sang PC offline → chạy cài 1 lần (không cần mạng khi cài).
3. Cài xong thì mở `QuanLyHeThong.exe` như Bước B.

> Win10 / Win11 đã có sẵn .NET Framework 4.8 — không cần làm gì thêm.

---

## Tải mã nguồn lên GitHub (cách dễ, không cần gõ lệnh)
1. github.com → **New** → đặt tên repo → **Create repository** (không tích Add README).
2. **Add file → Upload files** → kéo thả **toàn bộ file bên trong** thư mục này
   (các file `.cs`, `QuanLyHeThong.csproj`, `app.manifest`) vào — *file .csproj phải nằm ở gốc repo*.
3. Tạo riêng file workflow: **Add file → Create new file** → đặt tên:
   `.github/workflows/build.yml` → dán nội dung file build.yml trong thư mục này → **Commit changes**.
4. Vào tab **Actions** để lấy file .exe như Bước A.

## Build tay (nếu PC có mạng đã cài .NET SDK / Visual Studio)
```powershell
dotnet publish QuanLyHeThong.csproj -c Release -o publish
```

## Xoá USB triệt để trong Registry (PsExec nhúng sẵn)
Các khoá `Enum\USB` do SYSTEM/TrustedInstaller sở hữu, Administrator không xoá được.
**PsExec.exe (Microsoft Sysinternals) đã được NHÚNG thẳng vào `QuanLyHeThong.exe`** — khi bấm xoá, app tự
giải nén PsExec ra rồi chạy `PsExec -s reg import ...` dưới quyền SYSTEM. **Chỉ cần 1 file `QuanLyHeThong.exe`**,
chạy ở máy nào / thư mục nào cũng được, KHÔNG cần chép PsExec.exe kèm theo.

## Lưu ý
- Luôn **Run as administrator**.
- Gỡ driver/card mạng (gồm Wi-Fi) có thể làm mất kết nối thiết bị đó tới khi cài lại.
- Là công cụ "sửa hệ thống" chưa ký số nên SmartScreen có thể cảnh báo lần đầu — chọn *More info → Run anyway*.
