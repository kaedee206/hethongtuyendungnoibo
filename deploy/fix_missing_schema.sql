-- Fix missing database columns and tables for ATS Supabase Database
BEGIN;

-- 1. applications table: add missing columns
ALTER TABLE applications ADD COLUMN IF NOT EXISTS stage integer NOT NULL DEFAULT 0;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS tracking_code text;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS referrer_user_id uuid;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS referral_note text;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS rejection_reason_id uuid;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS rejection_note text;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS withdraw_token text;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS withdrawn_at timestamp with time zone;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS withdraw_reason text;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS is_sla_overdue boolean NOT NULL DEFAULT false;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS sla_overdue_alerted_at timestamp with time zone;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS stage_entered_at timestamp with time zone;

CREATE INDEX IF NOT EXISTS ix_applications_tracking_code ON applications (tracking_code);
CREATE INDEX IF NOT EXISTS ix_applications_referrer_user_id ON applications (referrer_user_id);
CREATE INDEX IF NOT EXISTS ix_applications_rejection_reason_id ON applications (rejection_reason_id);

-- 2. candidates table
ALTER TABLE candidates ADD COLUMN IF NOT EXISTS is_merged boolean NOT NULL DEFAULT false;
ALTER TABLE candidates ADD COLUMN IF NOT EXISTS merged_at timestamp with time zone;
ALTER TABLE candidates ADD COLUMN IF NOT EXISTS merged_into_candidate_id uuid;

-- 3. job_postings table
ALTER TABLE job_postings ADD COLUMN IF NOT EXISTS code text;

-- 4. job_positions table
ALTER TABLE job_positions ADD COLUMN IF NOT EXISTS min_salary numeric;
ALTER TABLE job_positions ADD COLUMN IF NOT EXISTS max_salary numeric;
ALTER TABLE job_positions ADD COLUMN IF NOT EXISTS competency_framework_id uuid;

-- 5. departments table
ALTER TABLE departments ADD COLUMN IF NOT EXISTS level integer NOT NULL DEFAULT 1;
ALTER TABLE departments ADD COLUMN IF NOT EXISTS path text NOT NULL DEFAULT '';

-- 6. users table
ALTER TABLE users ADD COLUMN IF NOT EXISTS job_title text;
ALTER TABLE users ADD COLUMN IF NOT EXISTS phone_number text;
ALTER TABLE users ADD COLUMN IF NOT EXISTS department_id uuid;
ALTER TABLE users ADD COLUMN IF NOT EXISTS job_position_id uuid;

-- 7. rejection_reasons table
CREATE TABLE IF NOT EXISTS rejection_reasons (
    id uuid NOT NULL PRIMARY KEY,
    code text NOT NULL,
    name text NOT NULL,
    description text,
    display_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    created_by_id uuid,
    updated_by_id uuid,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_at timestamp with time zone
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_rejection_reasons_code ON rejection_reasons (code);
CREATE INDEX IF NOT EXISTS ix_rejection_reasons_display_order ON rejection_reasons (display_order);
CREATE INDEX IF NOT EXISTS ix_rejection_reasons_is_active ON rejection_reasons (is_active);

-- Seed rejection reasons (including 62131a0f-f7e7-40a9-abf9-0b7a9edf83d8 referenced by application)
INSERT INTO rejection_reasons (id, code, name, description, display_order, is_active, created_at, updated_at, is_deleted)
VALUES 
    ('62131a0f-f7e7-40a9-abf9-0b7a9edf83d8', 'REJ_SKILL', 'Chuyên môn chưa đạt yêu cầu', 'Kỹ năng kỹ thuật hoặc năng lực chuyên môn chưa đáp ứng yêu cầu của vị trí tuyển dụng', 1, true, now(), now(), false),
    (gen_random_uuid(), 'REJ_EXP', 'Chưa đủ số năm kinh nghiệm', 'Số năm kinh nghiệm làm việc thực tế chưa đạt mức kỳ vọng của vị trí', 2, true, now(), now(), false),
    (gen_random_uuid(), 'REJ_SALARY', 'Không thỏa thuận được mức lương', 'Kỳ vọng thu nhập của ứng viên vượt khung ngân sách hoặc chế độ đãi ngộ của công ty', 3, true, now(), now(), false),
    (gen_random_uuid(), 'REJ_CULTURE', 'Chưa phù hợp văn hóa doanh nghiệp', 'Phong cách làm việc hoặc giá trị cốt lõi chưa tương thích với văn hóa đội ngũ', 4, true, now(), now(), false),
    (gen_random_uuid(), 'REJ_WITHDRAW', 'Ứng viên chủ động rút lui', 'Ứng viên nhận được offer khác hoặc rút hồ sơ vì lý do cá nhân', 5, true, now(), now(), false),
    (gen_random_uuid(), 'REJ_LOCATION', 'Địa điểm làm việc không thuận tiện', 'Khoảng cách địa lý không phù hợp hoặc không đáp ứng thời gian làm việc tại văn phòng', 6, true, now(), now(), false),
    (gen_random_uuid(), 'REJ_OTHER', 'Lý do khác', 'Các lý do loại hồ sơ phát sinh khác', 7, true, now(), now(), false)
ON CONFLICT (code) DO NOTHING;

-- Foreign key for rejection_reason_id on applications
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_applications_rejection_reasons_rejection_reason_id'
    ) THEN
        ALTER TABLE applications 
        ADD CONSTRAINT fk_applications_rejection_reasons_rejection_reason_id 
        FOREIGN KEY (rejection_reason_id) REFERENCES rejection_reasons(id) ON DELETE SET NULL;
    END IF;
END $$;

-- 8. candidate_stage_histories table
CREATE TABLE IF NOT EXISTS candidate_stage_histories (
    id uuid NOT NULL PRIMARY KEY,
    application_id uuid NOT NULL,
    from_stage integer,
    to_stage integer NOT NULL,
    rejection_reason_id uuid,
    note text,
    changed_by_user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    created_by_id uuid,
    updated_by_id uuid,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_at timestamp with time zone,
    CONSTRAINT fk_candidate_stage_histories_applications_application_id 
        FOREIGN KEY (application_id) REFERENCES applications(id) ON DELETE CASCADE,
    CONSTRAINT fk_candidate_stage_histories_rejection_reasons_rejection_reason 
        FOREIGN KEY (rejection_reason_id) REFERENCES rejection_reasons(id) ON DELETE SET NULL,
    CONSTRAINT fk_candidate_stage_histories_users_changed_by_user_id 
        FOREIGN KEY (changed_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_candidate_stage_histories_application_id ON candidate_stage_histories (application_id);
CREATE INDEX IF NOT EXISTS ix_candidate_stage_histories_changed_by_user_id ON candidate_stage_histories (changed_by_user_id);
CREATE INDEX IF NOT EXISTS ix_candidate_stage_histories_created_at ON candidate_stage_histories (created_at);
CREATE INDEX IF NOT EXISTS ix_candidate_stage_histories_rejection_reason_id ON candidate_stage_histories (rejection_reason_id);
CREATE INDEX IF NOT EXISTS ix_candidate_stage_histories_to_stage ON candidate_stage_histories (to_stage);

-- 9. candidate_interactions table
CREATE TABLE IF NOT EXISTS candidate_interactions (
    id uuid NOT NULL PRIMARY KEY,
    candidate_id uuid NOT NULL,
    application_id uuid,
    created_by_user_id uuid NOT NULL,
    interaction_type integer NOT NULL,
    title text NOT NULL,
    content text NOT NULL,
    outcome text,
    interaction_date timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    created_by_id uuid,
    updated_by_id uuid,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_at timestamp with time zone,
    CONSTRAINT fk_candidate_interactions_applications_application_id 
        FOREIGN KEY (application_id) REFERENCES applications(id) ON DELETE SET NULL,
    CONSTRAINT fk_candidate_interactions_candidates_candidate_id 
        FOREIGN KEY (candidate_id) REFERENCES candidates(id) ON DELETE CASCADE,
    CONSTRAINT fk_candidate_interactions_users_created_by_user_id 
        FOREIGN KEY (created_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_candidate_interactions_application_id ON candidate_interactions (application_id);
CREATE INDEX IF NOT EXISTS ix_candidate_interactions_candidate_id ON candidate_interactions (candidate_id);
CREATE INDEX IF NOT EXISTS ix_candidate_interactions_created_by_user_id ON candidate_interactions (created_by_user_id);
CREATE INDEX IF NOT EXISTS ix_candidate_interactions_interaction_date ON candidate_interactions (interaction_date);

-- 10. candidate_merge_histories table
CREATE TABLE IF NOT EXISTS candidate_merge_histories (
    id uuid NOT NULL PRIMARY KEY,
    primary_candidate_id uuid NOT NULL,
    secondary_candidate_id uuid NOT NULL,
    merged_by_user_id uuid NOT NULL,
    merged_at timestamp with time zone NOT NULL,
    merge_reason text,
    moved_application_ids_json text NOT NULL,
    moved_resume_ids_json text NOT NULL,
    snapshot_secondary_data_json text,
    is_undone boolean NOT NULL DEFAULT false,
    undone_at timestamp with time zone,
    undone_by_user_id uuid,
    notes text,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    created_by_id uuid,
    updated_by_id uuid,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_at timestamp with time zone,
    CONSTRAINT fk_candidate_merge_histories_candidates_primary_candidate_id 
        FOREIGN KEY (primary_candidate_id) REFERENCES candidates(id) ON DELETE CASCADE,
    CONSTRAINT fk_candidate_merge_histories_candidates_secondary_candidate_id 
        FOREIGN KEY (secondary_candidate_id) REFERENCES candidates(id) ON DELETE CASCADE,
    CONSTRAINT fk_candidate_merge_histories_users_merged_by_user_id 
        FOREIGN KEY (merged_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_candidate_merge_histories_merged_by_user_id ON candidate_merge_histories (merged_by_user_id);
CREATE INDEX IF NOT EXISTS ix_candidate_merge_histories_primary_candidate_id ON candidate_merge_histories (primary_candidate_id);
CREATE INDEX IF NOT EXISTS ix_candidate_merge_histories_secondary_candidate_id ON candidate_merge_histories (secondary_candidate_id);

-- 11. Sync __EFMigrationsHistory
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES 
    ('20261005150218_SyncModel', '9.0.0'),
    ('20261008152049_Scrum276AddApprovalRules', '9.0.0'),
    ('20261011135703_AddTrackingCodeToApplication', '9.0.0'),
    ('20261012113156_AddReferralToApplication', '9.0.0'),
    ('20261012141654_AddCandidateMergeAndDuplicateTracking', '9.0.0'),
    ('20261012180217_AddCandidateInteractionTable', '9.0.0'),
    ('20261013033649_AddCandidateStageAndRejectionReasons', '9.0.0')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
