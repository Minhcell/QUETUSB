# Quản Lý Hệ Thống

Tiện ích quản trị máy tính Windows (.exe), **chạy từ Windows 7 (SP1) đến Windows 11**.
Nền tảng **.NET Framework 4.7.2**. Cần chạy bằng **quyền Administrator** (phần mềm tự xin quyền khi mở).

## 3 chức năng

### 1. Thiết bị / Driver / Card mạng
- **Quét phát hiện**: USB cắm ngoài, gói driver bên thứ ba (oem*.inf, gồm cả driver Wi-Fi), card mạng vật lý (gồm card Wi-Fi).
- Quét **chỉ liệt kê**, KHÔNG tự xoá. Bạn **tích chọn 1 hoặc nhiều mục** rồi bấm **Xoá mục đã chọn** mới xoá.
- Gỡ thiết bị bằng **Windows SetupAPI** — API gốc của Microsoft (chính là cơ chế Device Manager và công cụ DevCon dùng), hoạt động ổn định từ Win7 đến Win11.
- Gỡ gói driver bằng `SetupUninstallOEMInf` (SetupAPI).
- Bật **Xoá cả trong Registry** để dọn nốt dấu vết thiết bị dưới `HKLM\SYSTEM\CurrentControlSet\Enum` (tự chiếm quyền sở hữu khoá nếu bị Windows bảo vệ).
- Nút **Xoá lịch sử USB từng cắm**: dọn toàn bộ `USBSTOR` trong Registry.

### 2. Dọn rác / Temp
Dọn Temp người dùng/hệ thống, cache Office, INetCache, CrashDumps, Prefetch, cache Windows Update, Thùng rác... File đang bị khoá sẽ tự bỏ qua. Có nút **Tính dung lượng** trước khi dọn.

### 3. Khoá / Mở cổng USB
Khoá/mở cổng USB đối với **thiết bị lưu trữ ngoài** (USBSTOR). Bàn phím, chuột USB không bị ảnh hưởng.

## Lấy file .exe (tự động qua GitHub Actions)

1. Tạo repo mới trên GitHub, đẩy toàn bộ thư mục này lên nhánh `main`.
2. Vào tab **Actions** → chờ workflow **Build EXE** chạy xong.
3. Mở lần chạy đó → mục **Artifacts** → tải `QuanLyHeThong-win` → giải nén được `QuanLyHeThong.exe`.
4. (Tuỳ chọn) Đẩy tag `v1.1.0` để tự tạo bản **Release** kèm file .exe.

```bash
git init
git add .
git commit -m "Quản Lý Hệ Thống - chạy Win7+"
git branch -M main
git remote add origin https://github.com/<tài-khoản>/<tên-repo>.git
git push -u origin main
```

## Build tay (nếu có máy Windows cài .NET SDK)

```powershell
dotnet publish QuanLyHeThong.csproj -c Release -o publish
```

## Lưu ý quan trọng
- Luôn **chạy bằng Administrator** (chuột phải → *Run as administrator*).
- Máy đích cần có **.NET Framework 4.7.2** trở lên (Win7 SP1 đã cập nhật Windows Update thường có sẵn; nếu chưa, tải miễn phí từ Microsoft).
- Gỡ driver/card mạng (gồm Wi-Fi) có thể làm mất kết nối thiết bị đó cho đến khi cài lại — hãy cân nhắc.
- Là công cụ "sửa hệ thống" chưa ký số nên SmartScreen/Defender có thể cảnh báo lần chạy đầu; đây là bình thường.
