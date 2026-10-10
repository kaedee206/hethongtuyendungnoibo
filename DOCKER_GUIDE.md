# HƯỚNG DẪN TRIỂN KHAI DOCKER & TỐI ƯU HÓA HỆ THỐNG ATS NOVERATECH

## 1. Cấu Trúc Các Tệp Triển Khai

```
hethongtuyendungnoibo/
├── Dockerfile                   # Multi-stage build .NET 9 tối ưu cache & runtime ~220MB
├── .dockerignore                # Loại bỏ file thừa, giảm 95% dung lượng build context
├── nginx/                       # Cấu hình Nginx Reverse Proxy
│   ├── Dockerfile               # Build container Nginx Alpine nhẹ ~25MB
│   ├── nginx.conf               # Cấu hình gốc (Gzip, Worker, WebSocket support, 50MB upload)
│   └── conf.d/
│       └── default.conf         # Reverse proxy port 80 -> ASP.NET Core web:8080
├── compose.yaml                 # Cấu hình Docker Compose gốc (services: web, nginx, network, volume)
├── compose.preview.yaml         # Override cho môi trường Preview (Development mode, live reload conf)
├── compose.prod.yaml            # Override cho môi trường Production (Resource limits, log rotation)
├── .env                         # BẢNG ĐIỀU KHIỂN: Cổng HTTP_PORT=80, chuyển Preview <-> Prod
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

# Cổng truy cập Web (Nginx mở port 80 ra ngoài)
HTTP_PORT=80
```

Chạy lệnh:
```bash
docker compose up -d --build
```
Docker Compose tự động khởi chạy 2 container phối hợp:
1. `ats-nginx`: Lắng nghe cổng **80** ngoài máy chủ.
2. `ats-web`: Chạy ứng dụng .NET 9 bên trong mạng `ats_network` (cổng nội bộ 8080).

---

## 3. Các Điểm Tối Ưu Hóa

1. **Nginx Reverse Proxy & Port 80**:
   - Mở chuẩn cổng 80 cho người dùng và các dịch vụ bên ngoài.
   - Chuyển tiếp đầy đủ `X-Forwarded-*` headers, tương thích `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.
   - Hỗ trợ kết nối real-time WebSocket / SignalR (`Upgrade`, `Connection`).
   - Mở rộng `client_max_body_size 50M` cho tải file CV và ảnh đại diện.
   - Nén Gzip tự động cho CSS, JS, SVG, JSON giảm băng thông và tăng tốc tải trang.
2. **Tốc độ build & Cache layer**:
   - Sử dụng Docker Layer Caching chuẩn quốc tế, tương thích 100% mọi phiên bản Docker (kể cả không có BuildKit).
   - Tách riêng tầng restore `Ats.Web.csproj`, không bị cache miss khi sửa code C#.
3. **Kích thước image**:
   - Image runtime `mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim` (~220MB).
   - Nginx Alpine (~25MB).
4. **Quản lý ổ cứng & Log Rotation**:
   - Log giới hạn tối đa 10MB x 3 file (`json-file`) cho cả Web và Nginx.
5. **Bảo toàn dữ liệu tải lên**:
   - Volume `ats_uploads_data` gắn cố định tại `/app/wwwroot/uploads` để lưu trữ CV và ảnh đại diện.
6. **Bảo mật**:
   - Container ASP.NET Core chạy dưới tài khoản không đặc quyền `app` (non-root).
   - Thêm các Security Headers (`X-Frame-Options`, `X-Content-Type-Options`, `X-XSS-Protection`, `Referrer-Policy`).
   - Ẩn Nginx server tokens.

---

## 4. Hướng Dẫn Vận Hành

### Chạy Preview (Demo Thầy cô / Mentor)
1. Đảm bảo dòng `COMPOSE_FILE=compose.yaml:compose.preview.yaml` trong `.env`.
2. Khởi chạy:
   ```bash
   docker compose up -d --build
   ```
3. Truy cập ngay: **`http://localhost`** (Cổng 80 qua Nginx).
   *(Cổng 8080 vẫn có thể truy cập song song `http://localhost:8080` khi cần debug trực tiếp)*.

### Chạy Production
1. Đổi sang `COMPOSE_FILE=compose.yaml:compose.prod.yaml` trong `.env`.
2. Tạo file `.env.production` từ `.env.production.example` và điền thông tin Supabase Prod.
3. Khởi chạy:
   ```bash
   docker compose up -d --build
   ```
4. Truy cập qua cổng 80 máy chủ: `http://<IP_MÁY_CHỦ>`.

---

## 5. Các Lệnh Quản Trị Hữu Ích

| Thao tác | Câu lệnh |
| :--- | :--- |
| **Xem logs Nginx** | `docker compose logs -f nginx` |
| **Xem logs Web** | `docker compose logs -f web` |
| **Kiểm tra trạng thái** | `docker compose ps` |
| **Reload cấu hình Nginx (Zero Downtime)** | `docker compose exec nginx nginx -s reload` |
| **Dừng hệ thống** | `docker compose down` |
| **Khởi động lại** | `docker compose restart` |
| **Dọn dẹp image thừa** | `docker image prune -f` |
