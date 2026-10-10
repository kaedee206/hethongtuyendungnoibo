# SỔ TAY TRIỂN KHAI HỆ THỐNG ATS & SSO ĐỊNH DANH TRÊN VPS (2 CPU - 2GB RAM - 30GB SSD)

Tài liệu này hướng dẫn chi tiết từng bước cách đưa toàn bộ hệ thống Tuyển dụng nội bộ **ATS** cùng **Hệ thống Quản lý Định danh Single Sign-On (SSO)** lên 1 VPS duy nhất với chi phí **0 đồng** (không tốn phí Google Workspace hay bản quyền thương mại).

---

## 1. Cấu Trúc Thư Mục Triển Khai (`deploy/`)

```
deploy/
├── docker-compose.yml              # Quản lý 4 container với trần RAM giới hạn cho 2GB VPS
├── setup_vps.sh                    # Script tự động: Tạo 4GB Swap, mở port UFW, cài Docker
├── init-db/
│   └── 01-init-databases.sh        # Tự tạo 2 database (ats_db và authelia_db) trên cùng 1 Postgres
├── authelia/
│   ├── configuration.yml           # Cấu hình OpenID Connect Provider, Issuer, Client Secret
│   └── users_database.yml          # Danh bạ nhân viên nội bộ (@noveratech.digital) băm Argon2id
└── nginx/
    ├── nginx.conf                  # Cấu hình Web Server lõi (Gzip, Upload 50M, Worker tối ưu)
    └── conf.d/
        └── ats_and_sso.conf        # Định tuyến: tuyendung.noveratech.digital & sso.noveratech.digital
```

---

## 2. Bước 1: Cấu Hình Tên Miền (DNS Records)

Đăng nhập vào trang quản trị tên miền của bạn (Cloudflare, Namecheap, v.v.) và trỏ 2 bản ghi `A` về IP của VPS:

| Loại (Type) | Tên (Host/Name) | Giá trị (Value / IPv4) | Proxy Status |
| :--- | :--- | :--- | :--- |
| **A** | `tuyendung` | `IP_CUA_VPS_CUA_BAN` | DNS only (Hoặc Proxy nếu dùng Cloudflare) |
| **A** | `sso` | `IP_CUA_VPS_CUA_BAN` | DNS only (Hoặc Proxy nếu dùng Cloudflare) |

---

## 3. Bước 2: Thiết Lập VPS Bằng 1 Lệnh Duy Nhất

Sau khi SSH vào VPS qua terminal:
```bash
# 1. Tải mã nguồn về VPS (hoặc copy thư mục project lên VPS)
git clone https://github.com/kaedee206/hethongtuyendungnoibo.git
cd hethongtuyendungnoibo/deploy

# 2. Cấp quyền thực thi và chạy script tự động
chmod +x setup_vps.sh
./setup_vps.sh
```

Script sẽ tự động:
1. Tạo file **Swap 4GB** làm đệm an toàn chống tràn RAM trên ổ cứng SSD 30GB.
2. Thiết lập tường lửa **UFW**: Khóa toàn bộ các cổng ngoài, chỉ cho phép cổng **22 (SSH)**, **80 (HTTP)**, **443 (HTTPS)**.
3. Cài đặt Docker & Docker Compose mới nhất.
4. Xin chứng chỉ SSL miễn phí từ **Let's Encrypt Certbot** cho 2 subdomain.

---

## 4. Bước 3: Khởi Động Cụm Dịch Vụ Docker

Tại thư mục `deploy/`:
```bash
docker compose up -d --build
```

Kiểm tra trạng thái các container đang chạy:
```bash
docker compose ps
docker stats
```
Bạn sẽ thấy 4 container chạy êm dịu với tổng lượng RAM sử dụng chỉ khoảng **~800MB - 950MB**:
- `ats-postgres`: ~180MB RAM
- `ats-authelia-sso`: ~45MB RAM
- `ats-web-app`: ~250MB RAM
- `ats-nginx-proxy`: ~25MB RAM

---

## 5. Hướng Dẫn Quản Lý Tài Khoản Nhân Viên (@noveratech.digital)

Để thêm hoặc đổi mật khẩu cho nhân viên nội bộ trong hệ thống SSO:

### 1. Tạo chuỗi mật khẩu băm Argon2id:
Chạy lệnh trực tiếp qua Docker:
```bash
docker run --rm authelia/authelia:latest authelia crypto hash generate argon2 --password 'MatKhauMoiCuaNhanVien@2026'
```
Lệnh sẽ trả về chuỗi hash có dạng `$argon2id$v=19$m=65536,t=3,p=4$...`.

### 2. Thêm vào tệp `deploy/authelia/users_database.yml`:
```yaml
users:
  hoang.le:
    displayname: "Lê Hoàng HR"
    password: "$argon2id$v=19$m=65536,t=3,p=4$..." # Chuỗi hash vừa tạo
    email: hoang.le@noveratech.digital
    groups:
      - recruiters
```

Lưu tệp lại. Authelia tự động tải lại danh sách tài khoản mà **không cần khởi động lại container** (`watch: true`).

---

## 6. Luồng Trải Nghiệm Đăng Nhập

1. Nhân viên truy cập `https://tuyendung.noveratech.digital/Account/StaffLogin`.
2. Bấm nút màu xanh: **"Đăng nhập với NoveraTech ID SSO (@noveratech.digital)"**.
3. Hệ thống chuyển hướng sang `https://sso.noveratech.digital`.
4. Nhân viên nhập username / mật khẩu hoặc xác thực 2FA.
5. Sau khi đăng nhập thành công, SSO tự động chuyển về ATS và cấp phiên làm việc với đúng vai trò được chỉ định.
