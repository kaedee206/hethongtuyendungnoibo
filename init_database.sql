-- ================================================================================
-- HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) - DATABASE SCHEMA & SEED DATA SCRIPT
-- Target Database: PostgreSQL 15+ / Supabase
-- Hỗ trợ đầy đủ: Entity Framework Core 9 (Snake Case) & Nghiệp vụ tuyển dụng ATS
-- ================================================================================

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- --------------------------------------------------------------------------------
-- 1. BẢNG QUẢN LÝ MIGRATION CỦA ENTITY FRAMEWORK CORE (__EFMigrationsHistory)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id VARCHAR(150) NOT NULL PRIMARY KEY,
    product_version VARCHAR(32) NOT NULL
);

-- --------------------------------------------------------------------------------
-- 2. BẢNG VAI TRÒ HỆ THỐNG (roles)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS roles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code INTEGER NOT NULL DEFAULT 0,
    name TEXT NOT NULL,
    description TEXT,
    is_system BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 3. BẢNG PHÒNG BAN (departments)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS departments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    parent_id UUID REFERENCES departments(id) ON DELETE SET NULL,
    manager_id UUID, -- Sẽ bổ sung Foreign Key tới users(id) sau khi bảng users được tạo
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 4. BẢNG VỊ TRÍ CÔNG VIỆC (job_positions)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS job_positions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code TEXT NOT NULL UNIQUE,
    title TEXT NOT NULL,
    department_id UUID NOT NULL REFERENCES departments(id) ON DELETE CASCADE,
    job_level TEXT NOT NULL, -- JUNIOR, MIDDLE, SENIOR, LEAD, MANAGER
    description TEXT,
    standard_competencies JSONB,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 5. BẢNG NGƯỜI DÙNG / TÀI KHOẢN (users)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    full_name TEXT NOT NULL,
    role TEXT NOT NULL DEFAULT 'Candidate',
    department TEXT,
    failed_login_attempts INTEGER NOT NULL DEFAULT 0,
    locked_until TIMESTAMPTZ,
    status TEXT NOT NULL DEFAULT 'ACTIVE',
    last_login_at TIMESTAMPTZ,
    last_activity_at TIMESTAMPTZ,
    password_reset_token TEXT,
    password_reset_token_expires_at TIMESTAMPTZ,
    failed_change_password_attempts INTEGER NOT NULL DEFAULT 0,
    change_password_locked_until TIMESTAMPTZ,
    role_id UUID REFERENCES roles(id) ON DELETE SET NULL,
    activation_token TEXT,
    created_by UUID,
    updated_by UUID,
    lock_reason TEXT,
    locked_at TIMESTAMPTZ,
    locked_by UUID,
    avatar_url TEXT,
    department_id UUID REFERENCES departments(id) ON DELETE SET NULL,
    job_position_id UUID REFERENCES job_positions(id) ON DELETE SET NULL,
    phone_number TEXT,
    user_type INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Bổ sung các cột nếu bảng users đã tồn tại trước đó (Migration compatibility)
ALTER TABLE users ADD COLUMN IF NOT EXISTS role TEXT NOT NULL DEFAULT 'Candidate';
ALTER TABLE users ADD COLUMN IF NOT EXISTS department TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS last_activity_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS job_title TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS password_reset_token TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS password_reset_token_expires_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS failed_change_password_attempts INTEGER NOT NULL DEFAULT 0;
ALTER TABLE users ADD COLUMN IF NOT EXISTS change_password_locked_until TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS role_id UUID REFERENCES roles(id) ON DELETE SET NULL;
ALTER TABLE users ADD COLUMN IF NOT EXISTS activation_token TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS created_by UUID;
ALTER TABLE users ADD COLUMN IF NOT EXISTS updated_by UUID;
ALTER TABLE users ADD COLUMN IF NOT EXISTS lock_reason TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS locked_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS locked_by UUID;
ALTER TABLE users ADD COLUMN IF NOT EXISTS avatar_url TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS department_id UUID REFERENCES departments(id) ON DELETE SET NULL;
ALTER TABLE users ADD COLUMN IF NOT EXISTS job_position_id UUID REFERENCES job_positions(id) ON DELETE SET NULL;
ALTER TABLE users ADD COLUMN IF NOT EXISTS phone_number TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS user_type INTEGER NOT NULL DEFAULT 0;

-- Indexes trên bảng users cho tìm kiếm tối ưu
CREATE INDEX IF NOT EXISTS ix_users_email ON users(email);
CREATE INDEX IF NOT EXISTS ix_users_full_name ON users(full_name);
CREATE INDEX IF NOT EXISTS ix_users_department ON users(department);
CREATE INDEX IF NOT EXISTS ix_users_role_id ON users(role_id);
CREATE INDEX IF NOT EXISTS ix_users_status ON users(status);

-- Bổ sung FK departments.manager_id -> users.id
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'fk_departments_users_manager_id'
    ) THEN
        ALTER TABLE departments 
        ADD CONSTRAINT fk_departments_users_manager_id 
        FOREIGN KEY (manager_id) REFERENCES users(id) ON DELETE SET NULL;
    END IF;
END $$;

-- --------------------------------------------------------------------------------
-- 6. BẢNG PHÂN QUYỀN NHIỀU VAI TRÒ (user_roles)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS user_roles (
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role_id UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);

-- --------------------------------------------------------------------------------
-- 7. BẢNG PHIÊN ĐĂNG NHẬP NGƯỜI DÙNG (user_sessions)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS user_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    session_id TEXT NOT NULL,
    ip_address TEXT NOT NULL,
    user_agent TEXT NOT NULL,
    is_revoked BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_active_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_user_sessions_user_id ON user_sessions(user_id);
CREATE INDEX IF NOT EXISTS ix_user_sessions_session_id ON user_sessions(session_id);

-- --------------------------------------------------------------------------------
-- 8. BẢNG AUDIT LOG XÁC THỰC (auth_audit_logs)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS auth_audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    email TEXT NOT NULL,
    event_type TEXT NOT NULL,
    is_success BOOLEAN NOT NULL DEFAULT true,
    reason TEXT NOT NULL DEFAULT '',
    ip_address TEXT NOT NULL DEFAULT '',
    user_agent TEXT NOT NULL DEFAULT '',
    session_id TEXT,
    timestamp TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_auth_audit_logs_user_id ON auth_audit_logs(user_id);
CREATE INDEX IF NOT EXISTS ix_auth_audit_logs_timestamp ON auth_audit_logs(timestamp);

-- --------------------------------------------------------------------------------
-- 9. BẢNG QUYỀN HẠN (permissions) & BẢNG ÁNH XẠ (role_permissions)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS permissions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code TEXT NOT NULL UNIQUE,
    module TEXT NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS role_permissions (
    role_id UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission_id UUID NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);

-- --------------------------------------------------------------------------------
-- 10. BẢNG ỨNG VIÊN (candidates) & HỒ SƠ CV (resumes)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS candidates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    email TEXT NOT NULL,
    phone TEXT,
    date_of_birth DATE,
    current_company TEXT,
    current_title TEXT,
    address TEXT,
    linkedin_url TEXT,
    source INTEGER NOT NULL DEFAULT 0,
    referrer_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    is_blacklisted BOOLEAN NOT NULL DEFAULT false,
    blacklist_reason TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS resumes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    file_name TEXT NOT NULL,
    file_path TEXT NOT NULL,
    file_size BIGINT NOT NULL,
    mime_type TEXT NOT NULL,
    parsed_text TEXT,
    is_primary BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 11. BẢNG GIAI ĐOẠN QUY TRÌNH PIPELINE (pipeline_stages)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pipeline_stages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL,
    stage_order INTEGER NOT NULL,
    color_code TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 12. BẢNG YÊU CẦU TUYỂN DỤNG (job_requisitions) & PHÊ DUYỆT (requisition_approvals)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS job_requisitions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code TEXT NOT NULL UNIQUE,
    job_position_id UUID NOT NULL REFERENCES job_positions(id) ON DELETE RESTRICT,
    department_id UUID NOT NULL REFERENCES departments(id) ON DELETE RESTRICT,
    hiring_manager_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assigned_recruiter_id UUID REFERENCES users(id) ON DELETE SET NULL,
    quantity INTEGER NOT NULL DEFAULT 1,
    headcount_type INTEGER NOT NULL DEFAULT 0,
    reason TEXT,
    min_salary NUMERIC,
    max_salary NUMERIC,
    currency TEXT NOT NULL DEFAULT 'VND',
    target_hire_date DATE,
    status INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS requisition_approvals (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    requisition_id UUID NOT NULL REFERENCES job_requisitions(id) ON DELETE CASCADE,
    approver_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    step_order INTEGER NOT NULL DEFAULT 1,
    status INTEGER NOT NULL DEFAULT 0,
    comment TEXT,
    decided_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 13. BẢNG TIN TUYỂN DỤNG (job_postings)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS job_postings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    requisition_id UUID NOT NULL REFERENCES job_requisitions(id) ON DELETE RESTRICT,
    title TEXT NOT NULL,
    slug TEXT NOT NULL UNIQUE,
    work_location TEXT NOT NULL,
    employment_type INTEGER NOT NULL DEFAULT 0,
    salary_display TEXT,
    job_description TEXT,
    requirements TEXT,
    benefits TEXT,
    published_at TIMESTAMPTZ,
    expired_at TIMESTAMPTZ,
    status INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 14. BẢNG ỨNG TUYỂN (applications) & LỊCH SỬ GIAI ĐOẠN (application_stage_histories)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS applications (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_posting_id UUID NOT NULL REFERENCES job_postings(id) ON DELETE RESTRICT,
    candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE RESTRICT,
    resume_id UUID NOT NULL REFERENCES resumes(id) ON DELETE RESTRICT,
    current_stage_id UUID NOT NULL REFERENCES pipeline_stages(id) ON DELETE RESTRICT,
    status INTEGER NOT NULL DEFAULT 0,
    rejection_reason_id UUID,
    rejection_note TEXT,
    rating INTEGER,
    applied_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS application_stage_histories (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id UUID NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
    from_stage_id UUID REFERENCES pipeline_stages(id) ON DELETE SET NULL,
    to_stage_id UUID NOT NULL REFERENCES pipeline_stages(id) ON DELETE RESTRICT,
    changed_by_user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    comment TEXT,
    duration_hours INTEGER,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 15. BẢNG PHỎNG VẤN (interviews), HỘI ĐỒNG (interview_panelists) & ĐÁNH GIÁ (interview_evaluations)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS interviews (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id UUID NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
    round_number INTEGER NOT NULL DEFAULT 1,
    title TEXT NOT NULL,
    interview_type INTEGER NOT NULL DEFAULT 0,
    location_or_link TEXT,
    start_time TIMESTAMPTZ,
    end_time TIMESTAMPTZ,
    candidate_confirmed INTEGER NOT NULL DEFAULT 0,
    candidate_notes TEXT,
    status INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS interview_panelists (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    interview_id UUID NOT NULL REFERENCES interviews(id) ON DELETE CASCADE,
    interviewer_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    is_lead BOOLEAN NOT NULL DEFAULT false,
    attended BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS interview_evaluations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    interview_id UUID NOT NULL REFERENCES interviews(id) ON DELETE CASCADE,
    interviewer_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    overall_score NUMERIC,
    recommendation INTEGER NOT NULL DEFAULT 0,
    strengths TEXT,
    weaknesses TEXT,
    detailed_feedback TEXT,
    is_submitted BOOLEAN NOT NULL DEFAULT false,
    submitted_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 16. BẢNG TIÊU CHÍ ĐÁNH GIÁ (evaluation_criterias) & ĐIỂM SỐ (evaluation_scores)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS evaluation_criterias (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL,
    description TEXT,
    weight NUMERIC NOT NULL DEFAULT 1.0,
    criteria_type TEXT NOT NULL DEFAULT 'GENERAL',
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS evaluation_scores (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    evaluation_id UUID NOT NULL REFERENCES interview_evaluations(id) ON DELETE CASCADE,
    criteria_id UUID NOT NULL REFERENCES evaluation_criterias(id) ON DELETE RESTRICT,
    score INTEGER NOT NULL DEFAULT 0,
    comment TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 17. BẢNG ĐỀ NGHỊ TUYỂN DỤNG (job_offers) & PHÊ DUYỆT OFFER (offer_approvals)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS job_offers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id UUID NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
    created_by_recruiter_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    base_salary NUMERIC NOT NULL,
    bonus_allowance NUMERIC,
    total_package NUMERIC NOT NULL,
    proposed_join_date DATE,
    probation_period_months INTEGER,
    contract_type TEXT,
    offer_letter_path TEXT,
    is_above_budget BOOLEAN NOT NULL DEFAULT false,
    status INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS offer_approvals (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    offer_id UUID NOT NULL REFERENCES job_offers(id) ON DELETE CASCADE,
    approver_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    level INTEGER NOT NULL DEFAULT 1,
    status INTEGER NOT NULL DEFAULT 0,
    comment TEXT,
    decided_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

-- --------------------------------------------------------------------------------
-- 18. BẢNG THÔNG BÁO (notifications), EMAIL LOG (email_logs) & AUDIT LOG CHUNG (audit_logs)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS notifications (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    content TEXT NOT NULL,
    link TEXT,
    is_read BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS email_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    recipient_email TEXT NOT NULL,
    subject TEXT NOT NULL,
    status TEXT NOT NULL,
    error_message TEXT,
    sent_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    action TEXT NOT NULL,
    entity_name TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    old_values JSONB,
    new_values JSONB,
    ip_address TEXT,
    user_agent TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- ================================================================================
-- PHẦN II: SEED DATA CHI TIẾT
-- ================================================================================

-- 1. Đăng ký EF Migrations đã hoàn tất vào __EFMigrationsHistory
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version) VALUES
    ('20260924161040_InitialAuthTables', '9.0.15'),
    ('20260925064502_AddAtsSchema', '9.0.15'),
    ('20260925070637_AddLastActivityAtToUsers', '9.0.15'),
    ('20260925081428_AddRoleToUser', '9.0.15'),
    ('20260925085618_AddUserRoleColumn', '9.0.15'),
    ('20260929152101_AddAuthAuditLog', '9.0.15'),
    ('20260929155137_AddUserSessions', '9.0.15'),
    ('20260929155731_AddAuthAuditLogEvents', '9.0.15'),
    ('20260929160652_AddPasswordResetFields', '9.0.15'),
    ('20260929162656_AddPasswordResetColumns', '9.0.15'),
    ('20260929183651_AddChangePasswordLockToUser', '9.0.15'),
    ('20260929185911_AddDepartmentToUser', '9.0.15'),
    ('20260930010812_Scrum131UserSchemaUpdate', '9.0.15'),
    ('20260930011227_AddUpdatedByToUser', '9.0.15')
ON CONFLICT (migration_id) DO NOTHING;

-- 2. Seed Roles
INSERT INTO roles (id, code, name, description, is_system) VALUES
    ('800641ee-364f-4d96-805a-2e753cad672a', 0, 'Quản trị viên hệ thống', 'Toàn quyền trên hệ thống', true),
    ('44435438-f646-43fc-9845-ff01b9937c8b', 1, 'Trưởng phòng nhân sự', 'Quản lý tuyển dụng, duyệt yêu cầu và offer', true),
    ('457e7212-815e-44d8-8588-cd2ca6792a71', 2, 'Chuyên viên tuyển dụng', 'Đăng tin, lọc hồ sơ, xếp lịch phỏng vấn', true),
    ('f53390e0-d249-4354-8f8d-c14fe8effa1e', 3, 'Quản lý chuyên môn', 'Tạo yêu cầu tuyển dụng, tham gia phỏng vấn', true),
    ('d16d4a6e-36ae-4021-a9ec-641955590ba0', 4, 'Người phỏng vấn', 'Tham gia phỏng vấn và đánh giá ứng viên', true),
    ('b16d87fb-a5de-48e1-ad07-989049e30c5a', 5, 'Người phê duyệt', 'Phê duyệt yêu cầu tuyển dụng và offer', true),
    ('da7a7828-db74-4d71-8f8f-53871eb71573', 6, 'Ứng viên nội bộ', 'Xem và ứng tuyển các vị trí nội bộ', true)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code,
    name = EXCLUDED.name,
    description = EXCLUDED.description;

-- 3. Seed Departments (manager_id để null trước, update sau)
INSERT INTO departments (id, code, name, parent_id, manager_id, is_active) VALUES
    ('6cfae957-e8f0-494f-90a2-e2b924ad5c3e', 'BOD', 'Ban Giám Đốc', NULL, NULL, true),
    ('f30e32de-1d91-46e1-95aa-549384ebb8a7', 'HR', 'Phòng Nhân Sự', '6cfae957-e8f0-494f-90a2-e2b924ad5c3e', NULL, true),
    ('1dc060f4-1b86-4161-bd37-21811fa7486b', 'IT', 'Phòng Công Nghệ Thông Tin', '6cfae957-e8f0-494f-90a2-e2b924ad5c3e', NULL, true),
    ('7f0c7044-5198-4882-81d6-bd3aea8d1ee6', 'SALES', 'Phòng Kinh Doanh', '6cfae957-e8f0-494f-90a2-e2b924ad5c3e', NULL, true)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code,
    name = EXCLUDED.name,
    parent_id = EXCLUDED.parent_id;

-- 4. Seed Job Positions
INSERT INTO job_positions (id, code, title, department_id, job_level, is_active) VALUES
    ('f561079a-9db1-4585-b937-2a838cf222f3', 'HR-MANAGER', 'HR Manager', 'f30e32de-1d91-46e1-95aa-549384ebb8a7', 'LEAD', true),
    ('37e2349d-0a27-45ca-8786-a0b5d2c01536', 'REC-SENIOR', 'Senior Recruiter', 'f30e32de-1d91-46e1-95aa-549384ebb8a7', 'SENIOR', true),
    ('c84218e3-bc8e-4551-b96a-d720f4d111d6', 'SWE-SENIOR', 'Senior Software Engineer', '1dc060f4-1b86-4161-bd37-21811fa7486b', 'SENIOR', true),
    ('4edbd93d-e16c-4a72-a6d9-ba1cf0ed6566', 'QA-MID', 'Middle QA Engineer', '1dc060f4-1b86-4161-bd37-21811fa7486b', 'MIDDLE', true)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code,
    title = EXCLUDED.title,
    department_id = EXCLUDED.department_id,
    job_level = EXCLUDED.job_level;

-- 5. Seed Pipeline Stages
INSERT INTO pipeline_stages (id, stage_order, name, color_code) VALUES
    ('56821b1d-04f1-4124-b2ac-26f37e9bc05c', 1, 'Ứng tuyển mới', '#0d6efd'),
    ('2d86196b-00fb-4fd6-8ce0-d1d2bc83e8c1', 2, 'Sàng lọc CV', '#6c757d'),
    ('8bd61ee0-950e-4c32-a3e1-7fcf20f596f7', 3, 'Phỏng vấn sơ loại', '#0dcaf0'),
    ('c93bc784-a712-4c3f-b50a-20ded71ce302', 4, 'Phỏng vấn chuyên môn', '#ffc107'),
    ('f4f7b1a6-47ff-4b7f-ad32-60c62c8d73c1', 5, 'Đề nghị tuyển dụng (Offer)', '#fd7e14'),
    ('a58a370a-e8c0-4d36-b525-fd83d36267be', 6, 'Tuyển dụng thành công', '#198754'),
    ('b78356af-e5da-4ec0-a5f3-bbc04de19a0a', 7, 'Từ chối', '#dc3545')
ON CONFLICT (id) DO UPDATE SET
    stage_order = EXCLUDED.stage_order,
    name = EXCLUDED.name,
    color_code = EXCLUDED.color_code;

-- 6. Seed Permissions
INSERT INTO permissions (id, code, module, description) VALUES
    ('8f7b7372-8af8-43aa-9ef1-ad8f3e35dc1c', 'user.view', 'EP-01_UserRole', 'Xem danh sách người dùng'),
    ('ef7b4e0b-0fd7-4268-9508-80e70c3b9204', 'user.manage', 'EP-01_UserRole', 'Quản lý tài khoản và phân quyền'),
    ('1e2eb2a3-34fa-47ec-a379-fa3416059e0f', 'org.manage', 'EP-02_Organization', 'Quản lý cơ cấu phòng ban và vị trí'),
    ('feaff9fa-09aa-46ea-acef-d80fb57fb8cc', 'requisition.create', 'EP-03_Requisition', 'Tạo yêu cầu tuyển dụng'),
    ('94eac9b5-47ee-4605-9604-9e6dac2e4a6f', 'requisition.approve', 'EP-03_Requisition', 'Phê duyệt yêu cầu tuyển dụng'),
    ('35fc8feb-ff0c-4b44-8091-f2ddb6592e65', 'job.post', 'EP-04_JobPosting', 'Đăng tin tuyển dụng'),
    ('91f0f6ec-3202-4b4f-911b-cee215dd3a17', 'candidate.manage', 'EP-05_CandidatePipeline', 'Quản lý hồ sơ và pipeline ứng viên'),
    ('1011479b-5039-45c3-8245-5c727c758619', 'interview.manage', 'EP-06_InterviewEvaluation', 'Xếp lịch và đánh giá phỏng vấn'),
    ('16513e2e-da6b-408c-99ad-dbd3e1904584', 'offer.manage', 'EP-07_OfferOnboarding', 'Tạo và duyệt thư mời nhận việc'),
    ('7c199fe6-f6b1-4042-9f8a-6196818bb2b8', 'analytics.view', 'EP-09_AnalyticsDashboard', 'Xem báo cáo thống kê tuyển dụng')
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code,
    module = EXCLUDED.module,
    description = EXCLUDED.description;

-- 7. Seed Role Permissions (Gán 10 quyền cho Admin)
INSERT INTO role_permissions (role_id, permission_id) VALUES
    ('800641ee-364f-4d96-805a-2e753cad672a', '8f7b7372-8af8-43aa-9ef1-ad8f3e35dc1c'),
    ('800641ee-364f-4d96-805a-2e753cad672a', 'ef7b4e0b-0fd7-4268-9508-80e70c3b9204'),
    ('800641ee-364f-4d96-805a-2e753cad672a', '1e2eb2a3-34fa-47ec-a379-fa3416059e0f'),
    ('800641ee-364f-4d96-805a-2e753cad672a', 'feaff9fa-09aa-46ea-acef-d80fb57fb8cc'),
    ('800641ee-364f-4d96-805a-2e753cad672a', '94eac9b5-47ee-4605-9604-9e6dac2e4a6f'),
    ('800641ee-364f-4d96-805a-2e753cad672a', '35fc8feb-ff0c-4b44-8091-f2ddb6592e65'),
    ('800641ee-364f-4d96-805a-2e753cad672a', '91f0f6ec-3202-4b4f-911b-cee215dd3a17'),
    ('800641ee-364f-4d96-805a-2e753cad672a', '1011479b-5039-45c3-8245-5c727c758619'),
    ('800641ee-364f-4d96-805a-2e753cad672a', '16513e2e-da6b-408c-99ad-dbd3e1904584'),
    ('800641ee-364f-4d96-805a-2e753cad672a', '7c199fe6-f6b1-4042-9f8a-6196818bb2b8')
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- 8. Seed 9 Tài khoản Người dùng (Users)
-- Mật khẩu mặc định: 123456@@ (AuthService hỗ trợ fallback so sánh plain text và tự băm thành BCrypt an toàn)
INSERT INTO users (
    id, email, password_hash, full_name, role, department, 
    role_id, department_id, job_position_id, status, failed_login_attempts
) VALUES
    (
        '51d7c7ad-18e5-40c3-86c9-b31512113350',
        'luong.hieu@noveratech.digital',
        '123456@@',
        'Lường Minh Hiếu',
        'Admin',
        'Phòng Công Nghệ Thông Tin',
        '800641ee-364f-4d96-805a-2e753cad672a',
        '1dc060f4-1b86-4161-bd37-21811fa7486b',
        'c84218e3-bc8e-4551-b96a-d720f4d111d6',
        'ACTIVE',
        0
    ),
    (
        '3dbde712-2654-4b7c-a5d3-da24272b4f77',
        'duc.anh@noveratech.digital',
        '123456@@',
        'Nguyễn Đức Anh',
        'Approver',
        'Ban Giám Đốc',
        'b16d87fb-a5de-48e1-ad07-989049e30c5a',
        '6cfae957-e8f0-494f-90a2-e2b924ad5c3e',
        NULL,
        'ACTIVE',
        0
    ),
    (
        'e19d5cf0-7b8f-4296-a9b8-e35ed080cc55',
        'phuong.nguyen@noveratech.digital',
        '123456@@',
        'Nguyễn Mai Phương',
        'HRManager',
        'Phòng Nhân Sự',
        '44435438-f646-43fc-9845-ff01b9937c8b',
        'f30e32de-1d91-46e1-95aa-549384ebb8a7',
        'f561079a-9db1-4585-b937-2a838cf222f3',
        'ACTIVE',
        0
    ),
    (
        'caccc0e1-8c9c-4ccb-b742-dbc9f64decec',
        'long.vu@noveratech.digital',
        '123456@@',
        'Vũ Thành Long',
        'HiringManager',
        'Phòng Công Nghệ Thông Tin',
        'f53390e0-d249-4354-8f8d-c14fe8effa1e',
        '1dc060f4-1b86-4161-bd37-21811fa7486b',
        'c84218e3-bc8e-4551-b96a-d720f4d111d6',
        'ACTIVE',
        0
    ),
    (
        'a4c487b8-dd2b-4f5a-839c-374090c6a4f7',
        'bao.hoang@noveratech.digital',
        '123456@@',
        'Hoàng Gia Bảo',
        'HiringManager',
        'Phòng Kinh Doanh',
        'f53390e0-d249-4354-8f8d-c14fe8effa1e',
        '7f0c7044-5198-4882-81d6-bd3aea8d1ee6',
        NULL,
        'ACTIVE',
        0
    ),
    (
        'cce812d5-26cb-4bd3-a5bc-35630406852b',
        'dung.le@noveratech.digital',
        '123456@@',
        'Lê Thùy Dung',
        'Recruiter',
        'Phòng Nhân Sự',
        '457e7212-815e-44d8-8588-cd2ca6792a71',
        'f30e32de-1d91-46e1-95aa-549384ebb8a7',
        '37e2349d-0a27-45ca-8786-a0b5d2c01536',
        'ACTIVE',
        0
    ),
    (
        'd8700e5d-a5f5-4ded-912d-a099377a8a7c',
        'anh.pham@noveratech.digital',
        '123456@@',
        'Phạm Quốc Anh',
        'Recruiter',
        'Phòng Nhân Sự',
        '457e7212-815e-44d8-8588-cd2ca6792a71',
        'f30e32de-1d91-46e1-95aa-549384ebb8a7',
        '37e2349d-0a27-45ca-8786-a0b5d2c01536',
        'ACTIVE',
        0
    ),
    (
        '240f242d-84d9-4a96-a41e-68a94e9a59df',
        'huy.ngo@noveratech.digital',
        '123456@@',
        'Ngô Quang Huy',
        'Interviewer',
        'Phòng Công Nghệ Thông Tin',
        'd16d4a6e-36ae-4021-a9ec-641955590ba0',
        '1dc060f4-1b86-4161-bd37-21811fa7486b',
        'c84218e3-bc8e-4551-b96a-d720f4d111d6',
        'ACTIVE',
        0
    ),
    (
        '118d7bb4-868c-4bae-95bb-113bec357b44',
        'tuan.bui@noveratech.digital',
        '123456@@',
        'Bùi Minh Tuấn',
        'Candidate',
        'Phòng Công Nghệ Thông Tin',
        'da7a7828-db74-4d71-8f8f-53871eb71573',
        '1dc060f4-1b86-4161-bd37-21811fa7486b',
        '4edbd93d-e16c-4a72-a6d9-ba1cf0ed6566',
        'ACTIVE',
        0
    )
ON CONFLICT (id) DO UPDATE SET
    email = EXCLUDED.email,
    full_name = EXCLUDED.full_name,
    role = EXCLUDED.role,
    department = EXCLUDED.department,
    role_id = EXCLUDED.role_id,
    department_id = EXCLUDED.department_id,
    job_position_id = EXCLUDED.job_position_id,
    status = EXCLUDED.status;

-- Đồng bộ cập nhật các cột role và department cho users nếu bản ghi cũ chưa có giá trị
UPDATE users SET role = 'Admin', department = 'Phòng Công Nghệ Thông Tin', role_id = '800641ee-364f-4d96-805a-2e753cad672a' WHERE id = '51d7c7ad-18e5-40c3-86c9-b31512113350' AND (role IS NULL OR role = '' OR department IS NULL);
UPDATE users SET role = 'Approver', department = 'Ban Giám Đốc', role_id = 'b16d87fb-a5de-48e1-ad07-989049e30c5a' WHERE id = '3dbde712-2654-4b7c-a5d3-da24272b4f77' AND (role IS NULL OR role = '' OR department IS NULL);
UPDATE users SET role = 'HRManager', department = 'Phòng Nhân Sự', role_id = '44435438-f646-43fc-9845-ff01b9937c8b' WHERE id = 'e19d5cf0-7b8f-4296-a9b8-e35ed080cc55' AND (role IS NULL OR role = '' OR department IS NULL);
UPDATE users SET role = 'HiringManager', department = 'Phòng Công Nghệ Thông Tin', role_id = 'f53390e0-d249-4354-8f8d-c14fe8effa1e' WHERE id = 'caccc0e1-8c9c-4ccb-b742-dbc9f64decec' AND (role IS NULL OR role = '' OR department IS NULL);
UPDATE users SET role = 'HiringManager', department = 'Phòng Kinh Doanh', role_id = 'f53390e0-d249-4354-8f8d-c14fe8effa1e' WHERE id = 'a4c487b8-dd2b-4f5a-839c-374090c6a4f7' AND (role IS NULL OR role = '' OR department IS NULL);
UPDATE users SET role = 'Recruiter', department = 'Phòng Nhân Sự', role_id = '457e7212-815e-44d8-8588-cd2ca6792a71' WHERE id = 'cce812d5-26cb-4bd3-a5bc-35630406852b' AND (role IS NULL OR role = '' OR department IS NULL);
UPDATE users SET role = 'Recruiter', department = 'Phòng Nhân Sự', role_id = '457e7212-815e-44d8-8588-cd2ca6792a71' WHERE id = 'd8700e5d-a5f5-4ded-912d-a099377a8a7c' AND (role IS NULL OR role = '' OR department IS NULL);
UPDATE users SET role = 'Interviewer', department = 'Phòng Công Nghệ Thông Tin', role_id = 'd16d4a6e-36ae-4021-a9ec-641955590ba0' WHERE id = '240f242d-84d9-4a96-a41e-68a94e9a59df' AND (role IS NULL OR role = '' OR department IS NULL);
UPDATE users SET role = 'Candidate', department = 'Phòng Công Nghệ Thông Tin', role_id = 'da7a7828-db74-4d71-8f8f-53871eb71573' WHERE id = '118d7bb4-868c-4bae-95bb-113bec357b44' AND (role IS NULL OR role = '' OR department IS NULL);

-- 9. Gán vai trò vào bảng user_roles
INSERT INTO user_roles (user_id, role_id) VALUES
    ('51d7c7ad-18e5-40c3-86c9-b31512113350', '800641ee-364f-4d96-805a-2e753cad672a'), -- Admin
    ('3dbde712-2654-4b7c-a5d3-da24272b4f77', 'b16d87fb-a5de-48e1-ad07-989049e30c5a'), -- Approver
    ('e19d5cf0-7b8f-4296-a9b8-e35ed080cc55', '44435438-f646-43fc-9845-ff01b9937c8b'), -- HRManager
    ('caccc0e1-8c9c-4ccb-b742-dbc9f64decec', 'f53390e0-d249-4354-8f8d-c14fe8effa1e'), -- HiringManager IT
    ('a4c487b8-dd2b-4f5a-839c-374090c6a4f7', 'f53390e0-d249-4354-8f8d-c14fe8effa1e'), -- HiringManager Sales
    ('cce812d5-26cb-4bd3-a5bc-35630406852b', '457e7212-815e-44d8-8588-cd2ca6792a71'), -- Recruiter 1
    ('d8700e5d-a5f5-4ded-912d-a099377a8a7c', '457e7212-815e-44d8-8588-cd2ca6792a71'), -- Recruiter 2
    ('240f242d-84d9-4a96-a41e-68a94e9a59df', 'd16d4a6e-36ae-4021-a9ec-641955590ba0'), -- Interviewer
    ('118d7bb4-868c-4bae-95bb-113bec357b44', 'da7a7828-db74-4d71-8f8f-53871eb71573')  -- Candidate
ON CONFLICT (user_id, role_id) DO NOTHING;

-- 10. Cập nhật Trưởng phòng (manager_id) cho các phòng ban
UPDATE departments SET manager_id = '3dbde712-2654-4b7c-a5d3-da24272b4f77' WHERE id = '6cfae957-e8f0-494f-90a2-e2b924ad5c3e'; -- BOD: Trần Đức Minh
UPDATE departments SET manager_id = 'e19d5cf0-7b8f-4296-a9b8-e35ed080cc55' WHERE id = 'f30e32de-1d91-46e1-95aa-549384ebb8a7'; -- HR: Nguyễn Mai Phương
UPDATE departments SET manager_id = 'caccc0e1-8c9c-4ccb-b742-dbc9f64decec' WHERE id = '1dc060f4-1b86-4161-bd37-21811fa7486b'; -- IT: Vũ Thành Long
UPDATE departments SET manager_id = 'a4c487b8-dd2b-4f5a-839c-374090c6a4f7' WHERE id = '7f0c7044-5198-4882-81d6-bd3aea8d1ee6'; -- SALES: Hoàng Gia Bảo

-- --------------------------------------------------------------------------------
-- SCRUM-276: B?NG C?U H�NH LU?NG PH� DUY?T THEO H?N M?C (approval_rules & approval_rule_steps)
-- --------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS approval_rules (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL,
    department_id UUID REFERENCES departments(id) ON DELETE SET NULL,
    min_salary NUMERIC(18,2) NOT NULL DEFAULT 0,
    max_salary NUMERIC(18,2),
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_approval_rules_department_id ON approval_rules(department_id);

CREATE TABLE IF NOT EXISTS approval_rule_steps (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    approval_rule_id UUID NOT NULL REFERENCES approval_rules(id) ON DELETE CASCADE,
    step_order INTEGER NOT NULL,
    approver_role_id UUID REFERENCES roles(id) ON DELETE RESTRICT,
    approver_user_id UUID REFERENCES users(id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_id UUID,
    updated_by_id UUID,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_approval_rule_steps_approval_rule_id ON approval_rule_steps(approval_rule_id);
CREATE INDEX IF NOT EXISTS ix_approval_rule_steps_approver_role_id ON approval_rule_steps(approver_role_id);
CREATE INDEX IF NOT EXISTS ix_approval_rule_steps_approver_user_id ON approval_rule_steps(approver_user_id);

