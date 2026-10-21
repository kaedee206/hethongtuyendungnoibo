# HƯỚNG DẪN TRIỂN KHAI DOCKER: DEV & DEPLOY (PRODUCTION)
> **Dự án:** Hệ thống Tuyển dụng Nội bộ (ATS NoveraTech)  
> **Kiến trúc:** Tách biệt 2 thư mục độc lập: `dev/` (Môi trường Phát triển) và `deploy/` (Môi trường Vận hành Production VPS)

---

## 1. Cấu Trúc Dự Án Tinh Gọn

```
hethongtuyendungnoibo/
├── dev/                         # 🚀 MÔI TRƯỜNG PHÁT TRIỂN & TEST CỤC BỘ
│   ├── docker-compose.yml       # Cấu hình Web + Nginx (Development mode, live reload)
│   ├── nginx/                   # Cấu hình Web Server Nginx cho local dev
│   │   ├── Dockerfile
│   │   ├── nginx.conf
│   │   └── conf.d/default.conf
│   ├── .env                     # Biến môi trường dev (kết nối Supabase Dev / Test DB)
│   ├── .env.example             # File mẫu biến môi trường dev
│   ├── run.sh                   # Script 1-chạm: cd dev && ./run.sh
│   └── README.md                # Sổ tay chi tiết môi trường dev
│
├── deploy/                      # 🌐 MÔI TRƯỜNG VẬN HÀNH PRODUCTION VPS (ALL-IN-ONE)
│   ├── docker-compose.yml       # All-in-one VPS: PostgreSQL + Authelia SSO + Web + Nginx SSL
│   ├── authelia/                # Hệ thống định danh Single Sign-On (@noveratech.digital)
│   ├── init-db/                 # Script tự động tạo cơ sở dữ liệu (ats_db, authelia_db)
│   ├── nginx/                   # Định tuyến domain (tuyendung / sso) & chứng chỉ SSL Certbot
│   ├── setup_vps.sh             # Script tự động cài đặt VPS (UFW firewall, Swap 4GB, Certbot)
│   ├── update_passwords.sh      # Script quản lý & cập nhật mật khẩu SSO Argon2id
│   ├── .env                     # Biến môi trường production VPS
│   ├── .env.example             # File mẫu production VPS
│   ├── run.sh                   # Script 1-chạm: cd deploy && ./run.sh
│   └── README.md                # Sổ tay chi tiết triển khai VPS
│
├── src/                         # Mã nguồn ứng dụng (.NET 9 ASP.NET Core)
├── tests/                       # Toàn bộ 165 bài kiểm thử xUnit
├── docs/                        # Tài liệu quy chuẩn thiết kế & bảo mật (RULE, SECURITY)
└── Dockerfile                   # Dockerfile đa tầng (.NET 9) dùng chung
```

---

## 2. So Sánh Hai Môi Trường

| Tiêu chí | Môi trường `dev/` (Phát triển / Test) | Môi trường `deploy/` (Production VPS) |
| :--- | :--- | :--- |
| **Mục đích** | Lập trình viên phát triển tính năng, kiểm thử, demo | Vận hành chính thức trên máy chủ VPS thực tế |
| **Cơ sở dữ liệu** | Kết nối Supabase Dev hoặc Database ngoài | PostgreSQL 16 nội bộ (`ats-postgres`) trên VPS |
| **Xác thực SSO** | Google OAuth hoặc Local Cookie Auth | Authelia SSO OIDC Provider (`ats-authelia-sso`) |
| **ASPNETCORE_ENVIRONMENT** | `Development` (Hiển thị chi tiết lỗi, stack trace) | `Production` (Bảo mật tối đa, ẩn chi tiết lỗi) |
| **Cổng truy cập** | `http://localhost:80` (Web) & `8080` (Direct debug) | `https://tuyendung.noveratech.digital` (SSL 443) |
| **Chính sách khởi động** | `restart: unless-stopped` | `restart: always` |
| **Giới hạn RAM / CPU** | Không giới hạn (Tối đa tốc độ build) | Giới hạn trần RAM tối ưu cho VPS 2GB |

---

## 3. Hướng Dẫn Vận Hành

### 3.1. Khởi chạy Môi trường Phát triển (`dev/`)
```bash
# 1. Di chuyển vào thư mục dev
cd dev

# 2. Khởi chạy bằng script 1-chạm (tự động build & up)
./run.sh

# Hoặc bằng lệnh Docker Compose tiêu chuẩn:
docker compose up -d --build
```
- **Truy cập Web:** `http://localhost` (Cổng 80)
- **Truy cập Debug trực tiếp:** `http://localhost:8080`
- **Xem logs Web:** `docker compose logs -f web`
- **Xem logs Nginx:** `docker compose logs -f nginx`
- **Dừng hệ thống:** `docker compose down`

---

### 3.2. Khởi chạy Môi trường Production VPS (`deploy/`)
```bash
# 1. Di chuyển vào thư mục deploy
cd deploy

# 2. Khởi chạy bằng script 1-chạm
./run.sh

# Hoặc bằng lệnh Docker Compose tiêu chuẩn:
docker compose up -d --build
```
- **Kiểm tra trạng thái:** `docker compose ps`
- **Xem logs toàn bộ:** `docker compose logs -f`
- **Cập nhật mật khẩu SSO:** `./update_passwords.sh`
- **Dừng hệ thống:** `docker compose down`
