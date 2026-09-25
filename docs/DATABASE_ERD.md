# SƠ ĐỒ THỰC THỂ QUAN HỆ (ERD) & BÁO CÁO NGHIỆM THU DATABASE ATS
> **Hệ thống Quản lý Tuyển dụng Nội bộ (ATS) — NoveraTech**  
> *Phiên bản: 1.0.0 — Database: PostgreSQL (Supabase)*

---

## 1. Sơ đồ ERD Tổng thể (Mermaid Diagram)

```mermaid
erDiagram
    %% MODULE: TỔ CHỨC & PHÂN QUYỀN (EP-01, EP-02)
    departments ||--o{ departments : "parent/children"
    departments ||--o{ job_positions : "has"
    departments ||--o{ users : "employs"
    job_positions ||--o{ users : "assigned_to"
    users ||--o{ user_roles : "has"
    roles ||--o{ user_roles : "assigned_to"
    roles ||--o{ role_permissions : "has"
    permissions ||--o{ role_permissions : "granted_to"

    %% MODULE: YÊU CẦU & TIN TUYỂN DỤNG (EP-03, EP-04)
    departments ||--o{ job_requisitions : "belongs_to"
    job_positions ||--o{ job_requisitions : "requests_for"
    users ||--o{ job_requisitions : "hiring_manager"
    users ||--o{ job_requisitions : "assigned_recruiter"
    job_requisitions ||--o{ requisition_approvals : "requires"
    users ||--o{ requisition_approvals : "approver"
    job_requisitions ||--o{ job_postings : "publishes"

    %% MODULE: ỨNG VIÊN & PIPELINE (EP-05)
    candidates ||--o{ resumes : "uploads"
    job_postings ||--o{ applications : "receives"
    candidates ||--o{ applications : "submits"
    resumes ||--o{ applications : "attaches"
    pipeline_stages ||--o{ applications : "current_stage"
    applications ||--o{ application_stage_histories : "tracks"
    pipeline_stages ||--o{ application_stage_histories : "stage"
    users ||--o{ application_stage_histories : "changed_by"

    %% MODULE: PHỎNG VẤN & ĐÁNH GIÁ (EP-06)
    applications ||--o{ interviews : "scheduled_for"
    interviews ||--o{ interview_panelists : "includes"
    users ||--o{ interview_panelists : "interviewer"
    interviews ||--o{ interview_evaluations : "evaluated_in"
    users ||--o{ interview_evaluations : "evaluated_by"
    interview_evaluations ||--o{ evaluation_scores : "contains"
    evaluation_criterias ||--o{ evaluation_scores : "scored_against"

    %% MODULE: ĐỀ NGHỊ TUYỂN DỤNG & ONBOARDING (EP-07)
    applications ||--o{ job_offers : "offers_to"
    job_offers ||--o{ offer_approvals : "requires"
    users ||--o{ offer_approvals : "approver"

    %% MODULE: GIÁM SÁT & LƯU VẾT
    users ||--o{ audit_logs : "performed_by"
    users ||--o{ notifications : "receives"

    %% CHI TIẾT CÁC THỰC THỂ CHÍNH
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
