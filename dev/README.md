# MÔI TRƯỜNG PHÁT TRIỂN & TEST CỤC BỘ (DEVELOPMENT)

Thư mục này chứa toàn bộ cấu hình Docker để khởi chạy hệ thống Tuyển dụng ATS ở môi trường **Development / Local Test**.

---

## 1. Cấu Trúc Thư Mục `dev/`

```
dev/
├── docker-compose.yml       # Cấu hình khởi chạy 2 container (Web + Nginx)
├── nginx/                   # Cấu hình Nginx port 80 & 8080 cục bộ
│   ├── Dockerfile
│   ├── nginx.conf
│   └── conf.d/default.conf
├── .env                     # Biến môi trường Development (kết nối Supabase Dev / Test DB)
├── .env.example             # File mẫu
├── run.sh                   # Script 1-chạm: ./run.sh
└── README.md                # Tài liệu này
```

---

## 2. Hướng Dẫn Khởi Chạy

### Cách 1: Chạy bằng script tiện ích (Khuyên dùng)
```bash
cd dev
./run.sh
```

### Cách 2: Chạy bằng Docker Compose tiêu chuẩn
```bash
cd dev
docker compose up -d --build
```

---

## 3. Cổng Truy Cập
- **Cổng Web chính thức:** `http://localhost:80` (qua Nginx)
- **Cổng Debug ứng dụng:** `http://localhost:8080` (truy cập thẳng ASP.NET Core)

## 4. Quản Trị Container
- **Xem logs Web:** `docker compose logs -f web`
- **Xem logs Nginx:** `docker compose logs -f nginx`
- **Dừng hệ thống:** `docker compose down`
