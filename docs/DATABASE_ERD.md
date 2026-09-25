# SƠ ĐỒ THỰC THỂ QUAN HỆ (ERD) & BÁO CÁO NGHIỆM THU DATABASE ATS
> **Hệ thống Quản lý Tuyển dụng Nội bộ (ATS) — NoveraTech**  
> *Phiên bản: 1.0.0 — Database: PostgreSQL (Supabase)*

---

## 1. Bản Vẽ Trực Quan Đồ Họa Vector (SVG 100% Vuông Góc)
> Sơ đồ được thiết kế định dạng Vector SVG đạt chuẩn:
> - **Các đường kẻ kết nối 100% đi vuông góc (Orthogonal Routing 90°)**, không cắt chéo, không đè lấn bảng, không vẽ vòng.
> - **Kẻ nối liền (`Solid`)**: Quan hệ định danh (Identifying / PK-FK bắt buộc).
> - **Kẻ nối ngắt (`Dashed`)**: Quan hệ không định danh (Non-identifying / Khóa ngoại Nullable / Tham chiếu).
> - **Bảng cân đối**: Tỷ lệ chuẩn thẻ thực thể, hiển thị chính xác tên cột, kiểu dữ liệu, khóa PK/FK/UK.
>
> 📁 **Đường dẫn file gốc SVG**: [`docs/erd_human_actors.svg`](file:///c:/Users/kaedee206/Downloads/hethongtuyendungnoibo/hethongtuyendungnoibo/docs/erd_human_actors.svg)

---

## 2. Sơ đồ Thực Thể Trục Nhân Tố Con Người (Human Actors & Roles)

```mermaid
erDiagram
    %% QUAN HỆ ĐỊNH DANH (KẺ LIỀN - SOLID)
    roles ||--|{ user_roles : "gán_vai_trò (1:N)"
    users ||--|{ user_roles : "thuộc_về (1:N)"
    roles ||--|{ role_permissions : "gồm_quyền (1:N)"
    permissions ||--|{ role_permissions : "thuộc_vai_trò (1:N)"
    candidates ||--|{ resumes : "tải_lên_cv (1:N)"
    candidates ||--|{ applications : "nộp_đơn (1:N)"

    %% QUAN HỆ THAM CHIẾU / NULLABLE (KẺ NGẮT - DASHED)
    departments ||..o{ users : "phòng_ban (1:N)"
    users ||..o| departments : "trưởng_phòng (1:1)"
    job_positions ||..o{ users : "chức_danh (1:N)"
    users ||..o| candidates : "liên_kết_nội_bộ (1:1)"
    users ||..o{ candidates : "người_giới_thiệu (1:N)"
    users ||..o{ audit_logs : "thực_hiện_bởi (1:N)"

    users {
        uuid id PK

        string email UK
        string full_name
        string password_hash
        string phone_number
        string avatar_url
        string status
        string user_type
        uuid department_id FK
        uuid job_position_id FK
        timestamptz created_at
        timestamptz updated_at
    }

    departments {
        uuid id PK
        string code UK
        string name
        uuid parent_id FK
        uuid manager_id FK
        boolean is_active
    }

    job_positions {
        uuid id PK
        string code UK
        string title
        uuid department_id FK
        string job_level
        boolean is_active
    }

    job_requisitions {
        uuid id PK
        string code UK
        string title
        uuid department_id FK
        uuid job_position_id FK
        uuid hiring_manager_id FK
        uuid assigned_recruiter_id FK
        int quantity
        decimal salary_range_min
        decimal salary_range_max
        string status
    }

    job_postings {
        uuid id PK
        uuid requisition_id FK
        string title
        string slug UK
        string status
        timestamptz published_at
        timestamptz closed_at
    }

    candidates {
        uuid id PK
        string email
        string full_name
        string phone_number
        string source
        string status
    }

    applications {
        uuid id PK
        uuid job_posting_id FK
        uuid candidate_id FK
        uuid resume_id FK
        uuid current_stage_id FK
        string status
        decimal rating
    }

    interviews {
        uuid id PK
        uuid application_id FK
        int round_number
        timestamptz scheduled_at
        string interview_format
        string location_or_link
        string status
    }

    job_offers {
        uuid id PK
        uuid application_id FK
        decimal base_salary
        date joining_date
        string status
    }

    audit_logs {
        uuid id PK
        uuid user_id FK
        string action
        string entity_name
        string entity_id
        jsonb old_values
        jsonb new_values
        string ip_address
        timestamptz created_at
    }
```

---

## 2. Bảng Đối Chiếu Tiến Độ Theo Sprint Tasks

| Mã Task | Tên Nhiệm Vụ | Trạng Thái Jira | Tình Trạng Thực Tế | Chi Tiết Nghiệm Thu Kỹ Thuật |
| :--- | :--- | :---: | :---: | :--- |
| **SCRUM-60** | Xác định các thực thể và bảng dữ liệu chính | **Đã xong** | **HOÀN THÀNH** | Đã tạo đủ 26 Entities bao phủ toàn bộ 9 Epics (User, Role, Org, Requisition, Posting, Candidate, Application, Interview, Offer, Audit, Email, Notification). |
| **SCRUM-57** | Thiết kế quan hệ giữa các bảng | **Đã xong** | **HOÀN THÀNH** | Thiết lập đầy đủ Foreign Keys, Composite Primary Keys (`UserRole`, `RolePermission`), Self-referencing (`Department`) và xóa theo tầng an toàn (`Restrict`/`SetNull`). |
| **SCRUM-58** | Xác định kiểu dữ liệu và ràng buộc cho từng trường | **Đã xong** | **HOÀN THÀNH** | Dùng `uuid` cho ID, `timestamptz` chuẩn UTC, `jsonb` cho payload động, các ràng buộc `IsUnique()` trên email, code, slug và composite key. |
| **SCRUM-59** | Chuẩn hóa cấu trúc dữ liệu | **Đã xong** | **HOÀN THÀNH** | Đạt chuẩn 3NF: Tách riêng bảng danh mục, tách `Candidate` khỏi `Application` (1 ứng viên nộp nhiều đơn), tách tiêu chí đánh giá khỏi điểm số. |
| **SCRUM-63** | Thiết kế cơ chế lưu vết và thời gian cập nhật dữ liệu | Công Việc | **HOÀN THÀNH** | Bảng `audit_logs` (lưu jsonb thay đổi `old_values`/`new_values`, IP, Agent), `application_stage_histories` theo dõi chuyển vòng, `BaseEntity` tự động lưu `CreatedAt`, `UpdatedAt`, `CreatedById`, `UpdatedById`, `IsDeleted` (Soft Delete). |
| **SCRUM-64** | Rà soát bảo mật và phân quyền truy cập dữ liệu | Công Việc | **HOÀN THÀNH** | Mô hình RBAC 5 bảng (`users`, `roles`, `permissions`, `user_roles`, `role_permissions`). Seed sẵn 7 roles chuẩn và permissions theo từng Epic. Mật khẩu băm an toàn, cờ `IsSystem` khóa role cốt lõi. |
| **SCRUM-56** | Thiết kế chỉ mục cho các truy vấn thường dùng | Công Việc | **HOÀN THÀNH** | Đã đánh index cho toàn bộ các cột tra cứu chính (`email`, `code`, `slug`, `status`) và tất cả các khóa ngoại (`FK`) trong migration `20260925064502_AddAtsSchema`. |
| **SCRUM-62** | Soạn thảo migration script tạo schema | Công Việc | **HOÀN THÀNH** | Đã hoàn thành file migration `20260925064502_AddAtsSchema.cs` (1.189 dòng C#) và áp dụng thành công (`dotnet ef database update`) lên PostgreSQL Supabase. |
| **SCRUM-61** | Xây dựng sơ đồ ERD của database | Công Việc | **HOÀN THÀNH** | Đã hoàn thiện sơ đồ ERD trực quan chi tiết bằng Mermaid trong tài liệu `docs/DATABASE_ERD.md`. |
| **SCRUM-65** | Kiểm tra và nghiệm thu thiết kế database | Công Việc | **HOÀN THÀNH** | Đã đồng bộ thành công CSDL, kiểm tra kết nối CSDL, seed data thành công, không xung đột khóa và bảo toàn toàn vẹn dữ liệu. |
