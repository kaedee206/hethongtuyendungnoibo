# HƯỚNG DẪN TRIỂN KHAI DOCKER & TỐI ƯU HÓA HỆ THỐNG ATS NOVERATECH

## 1. Cấu Trúc Các Tệp Triển Khai

```
hethongtuyendungnoibo/
├── Dockerfile                   # Multi-stage build .NET 9 tối ưu cache & runtime ~220MB
├── .dockerignore                # Loại bỏ file thừa, giảm 95% dung lượng build context
├── compose.yaml                 # Cấu hình Docker Compose gốc (service web, network, volume)
├── compose.preview.yaml         # Override cho môi trường Preview (Development mode)
├── compose.prod.yaml            # Override cho môi trường Production (Resource limits, log rotation)
├── .env                         # BẢNG ĐIỀU KHIỂN: Đổi mode Preview <-> Prod bằng 1 dòng
├── .env.preview                 # Cấu hình Preview (Supabase Dev + Email Doanh nghiệp thật) [Git Ignored]
├── .env.preview.example         # File mẫu cho môi trường Preview
├── .env.production.example      # File mẫu cho môi trường Production (Supabase chính thức)
└── DOCKER_GUIDE.md              # Sổ tay hướng dẫn này
```

---

## 2. Cách Chuyển Đổi Chế Độ Bằng 1 Dòng Duy Nhất

Trong tệp [`.env`](file:///d:/Projects/hethongtuyendungnoibo/.env) ở thư mục gốc:

```ini
# Chế độ Preview (Demo Thầy cô/Mentor):
COMPOSE_FILE=compose.yaml:compose.preview.yaml

# Hoặc chế độ Production (Vận hành chính thức):
# COMPOSE_FILE=compose.yaml:compose.prod.yaml
```

Chạy lệnh:
```bash
docker compose up -d --build
```
Docker Compose tự động nạp file override và file `.env` tương ứng (`.env.preview` cho Preview, `.env.production` cho Production).

---

## 3. Các Điểm Tối Ưu Hóa

1. **Tốc độ build & Cache layer**:
   - Sử dụng BuildKit Cache Mount (`--mount=type=cache,id=nuget`).
   - Tách riêng tầng restore `Ats.Web.csproj`, không bị cache miss khi sửa code C#.
2. **Kích thước image**:
   - Image runtime `mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim` (~220MB) thay vì SDK (>1.2GB).
   - Đầy đủ phông chữ và ICU tiếng Việt cho thư viện ClosedXML xuất Excel.
3. **Quản lý ổ cứng & Log Rotation**:
   - Log giới hạn tối đa 10MB x 3 file (`json-file`), chống tràn ổ cứng máy chủ.
4. **Bảo toàn dữ liệu tải lên**:
   - Volume `ats_uploads_data` gắn cố định tại `/app/wwwroot/uploads` để lưu trữ CV và ảnh đại diện.
5. **Bảo mật**:
   - Container chạy dưới tài khoản không đặc quyền `app` (non-root).
   - Toàn bộ file bí mật `.env*` đều được `.gitignore` bảo vệ.

---

## 4. Hướng Dẫn Vận Hành

### Chạy Preview (Demo Thầy cô / Mentor)
1. Đảm bảo dòng `COMPOSE_FILE=compose.yaml:compose.preview.yaml` trong `.env`.
2. Khởi chạy:
   ```bash
   docker compose up -d --build
   ```
3. Truy cập: `http://localhost:8080`.

### Chạy Production
1. Đổi sang `COMPOSE_FILE=compose.yaml:compose.prod.yaml` trong `.env`.
2. Tạo file `.env.production` từ `.env.production.example` và điền thông tin Supabase Prod.
3. Khởi chạy:
   ```bash
   docker compose up -d --build
   ```

---

## 5. Các Lệnh Quản Trị Hữu Ích

| Thao tác | Câu lệnh |
| :--- | :--- |
| **Xem logs** | `docker compose logs -f web` |
| **Kiểm tra trạng thái** | `docker compose ps` |
| **Dừng hệ thống** | `docker compose down` |
| **Khởi động lại** | `docker compose restart web` |
| **Dọn dẹp image thừa** | `docker image prune -f` |
| **Dọn dẹp cache build** | `docker builder prune -f` |
