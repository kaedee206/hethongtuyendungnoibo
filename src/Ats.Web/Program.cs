using System.Threading.RateLimiting;
using Ats.Web.Data;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

DotNetEnv.Env.TraversePath().Load();
if (File.Exists(".env")) DotNetEnv.Env.Load(".env");
if (File.Exists("src/Ats.Web/.env")) DotNetEnv.Env.Load("src/Ats.Web/.env");

var contentRoot = Directory.GetCurrentDirectory();
if (!Directory.Exists(Path.Combine(contentRoot, "Views")) && Directory.Exists(Path.Combine(contentRoot, "src", "Ats.Web", "Views")))
{
    contentRoot = Path.Combine(contentRoot, "src", "Ats.Web");
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot
});

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddMemoryCache();

builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Cấu hình Antiforgery cho cả Form body và HTTP Header (RequestVerificationToken)
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// 2. Lấy thông tin CSDL từ biến môi trường
var dbHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "ats_db";
var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres";
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "";

// Ghép thành chuỗi kết nối
var connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";

// 3. Đăng ký DbContext với chuỗi kết nối từ .env
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());

// Đăng ký Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<ICompetencyFrameworkService, CompetencyFrameworkService>();
builder.Services.AddScoped<IEvaluationCriteriaService, EvaluationCriteriaService>();
builder.Services.AddScoped<IRequisitionService, RequisitionService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IApprovalRuleService, ApprovalRuleService>();
builder.Services.AddScoped<ISecurityAuditService, SecurityAuditService>();

// Cấu hình Rate Limiting bảo vệ các endpoint nhạy cảm (OWASP A04 / A07)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"message\":\"Yêu cầu quá nhanh. Vui lòng chờ 1 phút trước khi thử lại để đảm bảo an toàn hệ thống.\"}",
            token);
    };

    // Policy 1: Giới hạn đăng nhập & OTP (10 requests / 1 phút / IP)
    options.AddPolicy("AuthRateLimit", httpContext =>
    {
        var clientIp = Ats.Web.Common.ClientIpHelper.GetClientIp(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // Policy 2: Giới hạn nộp hồ sơ ứng tuyển (5 requests / 10 phút / IP)
    options.AddPolicy("ApplyRateLimit", httpContext =>
    {
        var clientIp = Ats.Web.Common.ClientIpHelper.GetClientIp(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            });
    });
});


// Cấu hình thời gian Session (Idle timeout)
var idleTimeoutMinutesStr = Environment.GetEnvironmentVariable("SESSION_IDLE_TIMEOUT_MINUTES") ?? "30";
int idleTimeoutMinutes = int.TryParse(idleTimeoutMinutesStr, out var parsedIdle) ? parsedIdle : 30;

// 1. Thêm cấu hình Session với thời gian hết hạn (Idle timeout)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(idleTimeoutMinutes);
    options.Cookie.Name = "Ats.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// 2. Thêm cấu hình Authentication Cookie & External Google OAuth
var googleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")?.Trim();
var googleClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")?.Trim();

var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "AtsCookieScheme";
        options.DefaultChallengeScheme = "AtsCookieScheme";
    })
    .AddCookie("AtsCookieScheme", options =>
    {
        options.Cookie.Name = "Ats.AuthCookie";
        options.LoginPath = "/Account/StaffLogin";
        options.AccessDeniedPath = "/errors/403";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(idleTimeoutMinutes);
        options.SlidingExpiration = true; // Tự động gia hạn phiên khi user hoạt động > 50% thời hạn
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddCookie("ExternalCookieScheme", options =>
    {
        options.Cookie.Name = "Ats.ExternalCookie";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Kiểm tra xem đã cung cấp ClientId thực tế hay chưa
bool hasValidGoogleConfig = !string.IsNullOrWhiteSpace(googleClientId) && 
                            !string.IsNullOrWhiteSpace(googleClientSecret) &&
                            !googleClientId.Contains("YOUR_GOOGLE_CLIENT_ID", StringComparison.OrdinalIgnoreCase);

if (hasValidGoogleConfig)
{
    authBuilder.AddGoogle(options =>
    {
        options.SignInScheme = "ExternalCookieScheme";
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = true;
    });
}

// 3. Cấu hình SSO Định danh Nội bộ qua OpenID Connect (Authelia / Self-hosted ID Server)
var oidcAuthority = Environment.GetEnvironmentVariable("OIDC_AUTHORITY")?.Trim();
var oidcClientId = Environment.GetEnvironmentVariable("OIDC_CLIENT_ID")?.Trim();
var oidcClientSecret = Environment.GetEnvironmentVariable("OIDC_CLIENT_SECRET")?.Trim();

bool hasValidOidcConfig = !string.IsNullOrWhiteSpace(oidcAuthority) && 
                          !string.IsNullOrWhiteSpace(oidcClientId) &&
                          !oidcClientId.Contains("YOUR_OIDC_CLIENT_ID", StringComparison.OrdinalIgnoreCase);

if (hasValidOidcConfig)
{
    authBuilder.AddOpenIdConnect("NoveraOidcScheme", options =>
    {
        options.SignInScheme = "ExternalCookieScheme";
        options.Authority = oidcAuthority!;
        options.ClientId = oidcClientId!;
        if (!string.IsNullOrWhiteSpace(oidcClientSecret))
        {
            options.ClientSecret = oidcClientSecret;
        }
        options.ResponseType = "code";
        options.ResponseMode = "query";
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment() && (oidcAuthority?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?? false);
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("groups");
    });
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/errors/500");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// SCRUM-124: Đăng ký Global Exception Middleware cho API
app.UseMiddleware<Ats.Web.Middlewares.GlobalExceptionMiddleware>();

// Xử lý status code (401, 403, 404, 500)
app.UseStatusCodePagesWithReExecute("/errors/{0}");

app.UseForwardedHeaders();
app.UseHttpsRedirection();

// Bổ sung các HTTP Security Headers (OWASP A05)
app.UseMiddleware<Ats.Web.Middlewares.SecurityHeadersMiddleware>();

app.UseRouting();

// Áp dụng Rate Limiting bảo vệ hệ thống khỏi DoS và Brute Force
app.UseRateLimiter();

// Kích hoạt Session & Auth đúng thứ tự pipeline
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Kích hoạt Middleware gia hạn phiên tự động
app.UseMiddleware<Ats.Web.Middlewares.SessionActivityMiddleware>();

// Bảo vệ tài liệu nhạy cảm của ứng viên (CV/Resume) - Nghị định 13/2023/NĐ-CP & OWASP Top 10 A01 (Broken Access Control)
app.UseMiddleware<Ats.Web.Middlewares.CandidateDocumentsProtectionMiddleware>();

// Phục vụ tệp tĩnh (bao gồm tệp người dùng tải lên trong wwwroot/uploads)
app.UseStaticFiles();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


// Khởi tạo seed data tự động và đảm bảo schema bảng đầy đủ
using (var scope = app.Services.CreateScope())
{
    var storageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
    storageService.EnsureStorageDirectories();

    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Fix pending migrations in EFHistory
    try {
        await dbContext.Database.ExecuteSqlRawAsync("INSERT INTO \"__EFMigrationsHistory\" (migration_id, product_version) VALUES ('20261002010046_FixMissingColumns', '9.0.0') ON CONFLICT DO NOTHING;");
        await dbContext.Database.ExecuteSqlRawAsync("INSERT INTO \"__EFMigrationsHistory\" (migration_id, product_version) VALUES ('20261002163707_InitialCreate', '9.0.0') ON CONFLICT DO NOTHING;");
        
        // Add missing columns manually
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE departments ADD COLUMN IF NOT EXISTS level integer NOT NULL DEFAULT 1;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE departments ADD COLUMN IF NOT EXISTS path text NOT NULL DEFAULT '';");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS job_title text;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS phone_number text;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS department_id uuid;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS job_position_id uuid;");
        
    } catch(Exception e) {
        Console.WriteLine(e.Message);
    }

    try
    {
        await dbContext.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE departments ADD COLUMN IF NOT EXISTS level INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE departments ADD COLUMN IF NOT EXISTS path TEXT NOT NULL DEFAULT '';
            UPDATE departments SET path = '/' || id || '/' WHERE path = '' OR path IS NULL;
            ALTER TABLE job_requisitions ADD COLUMN IF NOT EXISTS salary_band_explanation TEXT;
            ALTER TABLE job_positions ADD COLUMN IF NOT EXISTS competency_framework_id UUID;

            CREATE TABLE IF NOT EXISTS approval_rules (
                id UUID PRIMARY KEY,
                name TEXT NOT NULL,
                department_id UUID,
                min_salary NUMERIC NOT NULL,
                max_salary NUMERIC,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS approval_rule_steps (
                id UUID PRIMARY KEY,
                approval_rule_id UUID NOT NULL REFERENCES approval_rules(id) ON DELETE CASCADE,
                step_order INTEGER NOT NULL,
                approver_role_id UUID,
                approver_user_id UUID,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );


            CREATE TABLE IF NOT EXISTS competency_frameworks (
                id UUID PRIMARY KEY,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT,
                target_role TEXT,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                is_system BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS competency_criteria (
                id UUID PRIMARY KEY,
                competency_framework_id UUID REFERENCES competency_frameworks(id) ON DELETE CASCADE,
                framework_id UUID,
                name TEXT NOT NULL,
                description TEXT,
                weight INTEGER NOT NULL DEFAULT 1,
                rubric1 TEXT,
                rubric2 TEXT,
                rubric3 TEXT,
                rubric4 TEXT,
                rubric5 TEXT,
                rubric_level1 TEXT,
                rubric_level2 TEXT,
                rubric_level3 TEXT,
                rubric_level4 TEXT,
                rubric_level5 TEXT,
                display_order INTEGER NOT NULL DEFAULT 0,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
            ALTER TABLE competency_criteria ADD COLUMN IF NOT EXISTS competency_framework_id UUID;
            ALTER TABLE competency_criteria ADD COLUMN IF NOT EXISTS rubric1 TEXT;
            ALTER TABLE competency_criteria ADD COLUMN IF NOT EXISTS rubric2 TEXT;
            ALTER TABLE competency_criteria ADD COLUMN IF NOT EXISTS rubric3 TEXT;
            ALTER TABLE competency_criteria ADD COLUMN IF NOT EXISTS rubric4 TEXT;
            ALTER TABLE competency_criteria ADD COLUMN IF NOT EXISTS rubric5 TEXT;
            DO $$ 
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_name = 'competency_criteria' AND column_name = 'framework_id' AND is_nullable = 'NO'
                ) THEN
                    ALTER TABLE competency_criteria ALTER COLUMN framework_id DROP NOT NULL;
                END IF;
            END $$;

            CREATE TABLE IF NOT EXISTS interview_question_banks (
                id UUID PRIMARY KEY,
                content TEXT NOT NULL DEFAULT '',
                competency TEXT NOT NULL DEFAULT '',
                difficulty TEXT NOT NULL DEFAULT 'Cơ bản',
                suggested_answer TEXT,
                competency_framework_id UUID,
                competency_criterion_id UUID,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
            ALTER TABLE interview_question_banks ADD COLUMN IF NOT EXISTS content TEXT NOT NULL DEFAULT '';
            ALTER TABLE interview_question_banks ADD COLUMN IF NOT EXISTS competency TEXT NOT NULL DEFAULT '';
            ALTER TABLE interview_question_banks ADD COLUMN IF NOT EXISTS difficulty TEXT NOT NULL DEFAULT 'Cơ bản';
            ALTER TABLE interview_question_banks ADD COLUMN IF NOT EXISTS suggested_answer TEXT;
            ALTER TABLE interview_question_banks ADD COLUMN IF NOT EXISTS competency_framework_id UUID;
            ALTER TABLE interview_question_banks ADD COLUMN IF NOT EXISTS competency_criterion_id UUID;

            DO $$ 
            DECLARE
                r RECORD;
            BEGIN
                FOR r IN (
                    SELECT column_name 
                    FROM information_schema.columns 
                    WHERE table_name = 'interview_question_banks' 
                      AND is_nullable = 'NO' 
                      AND column_default IS NULL
                      AND column_name NOT IN ('id')
                ) LOOP
                    EXECUTE 'ALTER TABLE interview_question_banks ALTER COLUMN ' || quote_ident(r.column_name) || ' DROP NOT NULL';
                END LOOP;
            END $$;

            CREATE TABLE IF NOT EXISTS recruitment_catalogs (
                id UUID PRIMARY KEY,
                catalog_type TEXT NOT NULL,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT,
                display_order INTEGER NOT NULL DEFAULT 0,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                is_system BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS company_profiles (
                id UUID PRIMARY KEY,
                company_name TEXT NOT NULL DEFAULT 'Tập đoàn Công nghệ NoveraTech',
                headline TEXT NOT NULL DEFAULT 'Cùng NoveraTech kiến tạo tương lai công nghệ số',
                about_text TEXT,
                engineering_culture TEXT,
                tech_stack_json TEXT,
                proof_metrics_json TEXT,
                perks_json TEXT,
                headquarters_address TEXT,
                contact_email TEXT,
                phone_contact TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB_MIGRATION_WARN] {ex.Message}");
    }

    try
    {
        await DatabaseSeeder.SeedAsync(dbContext);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB_SEED_WARN] Không thể kết nối hoặc seed database: {ex.Message}");
    }
}

app.Run();
