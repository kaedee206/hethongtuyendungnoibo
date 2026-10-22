using System.Threading.RateLimiting;
using Ats.Web.Data;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
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

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
});

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddMemoryCache();

// Lưu trữ khóa mã hóa cố định tránh mất session và token xác thực khi khởi động lại server
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "dataprotection-keys");
if (builder.Environment.IsProduction() && Directory.Exists("/app"))
{
    keysFolder = "/app/dataprotection-keys";
}
if (!Directory.Exists(keysFolder))
{
    Directory.CreateDirectory(keysFolder);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("AtsWeb");

// Nén phản hồi HTTP (Brotli & Gzip) tối ưu tốc độ tải trang
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "text/html",
        "text/plain",
        "text/css",
        "application/javascript",
        "text/javascript",
        "application/json",
        "text/json",
        "image/svg+xml"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});

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
           .UseSnakeCaseNamingConvention()
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// Đăng ký Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<ICandidateService, CandidateService>();
builder.Services.AddScoped<ICompetencyFrameworkService, CompetencyFrameworkService>();
builder.Services.AddScoped<IEvaluationCriteriaService, EvaluationCriteriaService>();
builder.Services.AddScoped<IRequisitionService, RequisitionService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IApprovalRuleService, ApprovalRuleService>();
builder.Services.AddScoped<ISecurityAuditService, SecurityAuditService>();
builder.Services.AddScoped<IRecruitmentCatalogService, RecruitmentCatalogService>();
builder.Services.AddScoped<IApplicationTrackingService, ApplicationTrackingService>();
builder.Services.AddScoped<ICandidateReferralService, CandidateReferralService>();
builder.Services.AddScoped<ICandidateMergeService, CandidateMergeService>();
builder.Services.AddScoped<ICandidateStageService, CandidateStageService>();
builder.Services.AddScoped<ICandidateTimelineService, CandidateTimelineService>();
builder.Services.AddScoped<IJobPostingService, JobPostingService>();
builder.Services.AddScoped<IDepartmentBudgetService, DepartmentBudgetService>();
builder.Services.AddScoped<IRequisitionAssignmentService, RequisitionAssignmentService>();
builder.Services.AddScoped<ISlaService, SlaService>();
builder.Services.AddHostedService<SlaOverdueBackgroundService>();
builder.Services.AddScoped<ITalentPoolService, TalentPoolService>();
builder.Services.AddScoped<IInterviewEvaluationService, InterviewEvaluationService>();
builder.Services.AddScoped<IEvaluationService, EvaluationService>();
builder.Services.AddScoped<IInterviewService, InterviewService>();
builder.Services.AddScoped<ICvParserService, CvParserService>();
builder.Services.AddScoped<ICandidateExportService, CandidateExportService>();
builder.Services.AddScoped<ICandidateEvaluationComparisonService, CandidateEvaluationComparisonService>();
builder.Services.AddScoped<ICandidateInterviewService, CandidateInterviewService>();
builder.Services.AddScoped<ICandidateMultiRoundEvaluationService, CandidateMultiRoundEvaluationService>();
builder.Services.AddSingleton<IJobExpirationService, JobExpirationService>();
builder.Services.AddHostedService<JobExpirationBackgroundService>();

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
var cookieSecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(idleTimeoutMinutes);
    options.Cookie.Name = "Ats.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
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
        options.Cookie.SecurePolicy = cookieSecurePolicy;
    })
    .AddCookie("ExternalCookieScheme", options =>
    {
        options.Cookie.Name = "Ats.ExternalCookie";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
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
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment() && (oidcAuthority?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?? false);
        options.PushedAuthorizationBehavior = Microsoft.AspNetCore.Authentication.OpenIdConnect.PushedAuthorizationBehavior.Disable;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("groups");

        options.Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
        {
            OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OpenIdConnect");
                logger.LogWarning(context.Failure, "OIDC remote failure: {Message}", context.Failure?.Message);

                var errorMsg = context.Failure?.Message ?? "Quá trình xác thực SSO thất bại hoặc đã bị hủy.";
                if (errorMsg.Contains("invalid_request_uri", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("expired", StringComparison.OrdinalIgnoreCase))
                {
                    errorMsg = "Phiên làm việc SSO đã hết hạn hoặc không hợp lệ. Vui lòng thử đăng nhập lại.";
                }

                context.Response.Redirect($"/Account/StaffLogin?remoteError={Uri.EscapeDataString(errorMsg)}");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
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
app.UseResponseCompression();

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

// Phục vụ tệp tĩnh (bao gồm tệp người dùng tải lên trong wwwroot/uploads & tệp Excel mẫu đính kèm)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        if (path.StartsWith("/templates/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = System.IO.Path.GetFileName(path);
            ctx.Context.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        }
        else if (path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("/img/", StringComparison.OrdinalIgnoreCase))
        {
            // Aggressive caching cho static assets có query string asp-append-version (1 năm)
            if (ctx.Context.Request.Query.ContainsKey("v"))
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
            }
            else
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=86400");
            }
        }
    }
});
app.MapStaticAssets();

// Phục vụ ảnh đại diện và media kèm fallback ảnh mặc định nếu file không tồn tại
app.MapMethods("/uploads/{folder:regex(^(avatars|media)$)}/{fileName}", new[] { "GET", "HEAD" }, (string folder, string fileName, IWebHostEnvironment env) =>
{
    var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    var filePath = Path.Combine(webRoot, "uploads", folder, fileName);

    if (File.Exists(filePath))
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var mime = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
        return Results.File(filePath, mime);
    }

    var altFolder = folder.Equals("avatars", StringComparison.OrdinalIgnoreCase) ? "media" : "avatars";
    var altPath = Path.Combine(webRoot, "uploads", altFolder, fileName);
    if (File.Exists(altPath))
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var mime = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
        return Results.File(altPath, mime);
    }

    var defaultAvatar = Path.Combine(webRoot, "img", "logo-icon-dark-transparent.png");
    return File.Exists(defaultAvatar)
        ? Results.File(defaultAvatar, "image/png")
        : Results.NotFound();
});

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
    try
    {
        await dbContext.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB_MIGRATE_WARN] {ex.Message}");
    }

    // Ensure database columns and custom tables
    try
    {

        // Add missing columns manually
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE departments ADD COLUMN IF NOT EXISTS level integer NOT NULL DEFAULT 1;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE departments ADD COLUMN IF NOT EXISTS path text NOT NULL DEFAULT '';");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS job_title text;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS phone_number text;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS department_id uuid;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS job_position_id uuid;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS stage integer NOT NULL DEFAULT 0;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS tracking_code text;");
        await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_applications_tracking_code ON applications (tracking_code);");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS referrer_user_id uuid;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS referral_note text;");
        await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_applications_referrer_user_id ON applications (referrer_user_id);");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS withdraw_token text;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS withdrawn_at timestamp with time zone;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS withdraw_reason text;");
        await dbContext.Database.ExecuteSqlRawAsync("UPDATE applications SET withdraw_token = md5(random()::text || id::text) WHERE withdraw_token IS NULL;");
        // SCRUM-327: Bổ sung cấu hình SLA và cảnh báo quá hạn
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE pipeline_stages ADD COLUMN IF NOT EXISTS sla_days integer NOT NULL DEFAULT 3;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE pipeline_stages ADD COLUMN IF NOT EXISTS is_sla_enabled boolean NOT NULL DEFAULT true;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS is_sla_overdue boolean NOT NULL DEFAULT false;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS sla_overdue_alerted_at timestamp with time zone;");
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE applications ADD COLUMN IF NOT EXISTS stage_entered_at timestamp with time zone;");
        await dbContext.Database.ExecuteSqlRawAsync("UPDATE applications SET stage_entered_at = COALESCE(applied_at, created_at) WHERE stage_entered_at IS NULL;");
        await dbContext.Database.ExecuteSqlRawAsync(@"
            UPDATE pipeline_stages SET sla_days = 3, is_sla_enabled = true WHERE stage_order IN (1, 2) AND (sla_days IS NULL OR sla_days = 0);
            UPDATE pipeline_stages SET sla_days = 5, is_sla_enabled = true WHERE stage_order = 3 AND (sla_days IS NULL OR sla_days = 0);
            UPDATE pipeline_stages SET sla_days = 7, is_sla_enabled = true WHERE stage_order = 4 AND (sla_days IS NULL OR sla_days = 0);
            UPDATE pipeline_stages SET sla_days = 3, is_sla_enabled = true WHERE stage_order = 5 AND (sla_days IS NULL OR sla_days = 0);
            UPDATE pipeline_stages SET sla_days = 0, is_sla_enabled = false WHERE stage_order IN (6, 7);
        ");
    }
    catch (Exception e)
    {
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
            ALTER TABLE job_positions ADD COLUMN IF NOT EXISTS min_salary NUMERIC;
            ALTER TABLE job_positions ADD COLUMN IF NOT EXISTS max_salary NUMERIC;
            ALTER TABLE job_postings ADD COLUMN IF NOT EXISTS code TEXT;
            ALTER TABLE job_postings ADD COLUMN IF NOT EXISTS unpublish_reason TEXT;
            ALTER TABLE job_postings ADD COLUMN IF NOT EXISTS closed_at TIMESTAMPTZ;

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

            -- Scrum #40: Cột gộp hồ sơ trên bảng candidates
            ALTER TABLE candidates ADD COLUMN IF NOT EXISTS is_merged BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE candidates ADD COLUMN IF NOT EXISTS merged_into_candidate_id UUID;
            ALTER TABLE candidates ADD COLUMN IF NOT EXISTS merged_at TIMESTAMPTZ;

            -- Scrum #40: Bảng lưu vết phiên gộp và hoàn tác 24h
            CREATE TABLE IF NOT EXISTS candidate_merge_histories (
                id UUID PRIMARY KEY,
                primary_candidate_id UUID NOT NULL REFERENCES candidates(id),
                secondary_candidate_id UUID NOT NULL REFERENCES candidates(id),
                merged_by_user_id UUID NOT NULL REFERENCES users(id),
                merged_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                merge_reason TEXT,
                moved_application_ids_json TEXT NOT NULL DEFAULT '[]',
                moved_resume_ids_json TEXT NOT NULL DEFAULT '[]',
                snapshot_secondary_data_json TEXT,
                is_undone BOOLEAN NOT NULL DEFAULT FALSE,
                undone_at TIMESTAMPTZ,
                undone_by_user_id UUID REFERENCES users(id),
                notes TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS candidate_tags (
                id UUID PRIMARY KEY,
                candidate_id UUID NOT NULL,
                tag_name TEXT NOT NULL,
                color_code TEXT,
                created_by_user_id UUID,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
            ALTER TABLE candidate_tags ADD COLUMN IF NOT EXISTS created_by_id UUID;
            ALTER TABLE candidate_tags ADD COLUMN IF NOT EXISTS updated_by_id UUID;

            CREATE TABLE IF NOT EXISTS candidate_notes (
                id UUID PRIMARY KEY,
                candidate_id UUID NOT NULL,
                author_user_id UUID NOT NULL,
                content TEXT NOT NULL,
                note_type TEXT DEFAULT 'GENERAL',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
            ALTER TABLE candidate_notes ADD COLUMN IF NOT EXISTS created_by_id UUID;
            ALTER TABLE candidate_notes ADD COLUMN IF NOT EXISTS updated_by_id UUID;

            -- Scrum #24: Định biên và ngân sách tuyển dụng theo phòng ban
            ALTER TABLE job_requisitions ADD COLUMN IF NOT EXISTS is_over_quota BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE job_requisitions ADD COLUMN IF NOT EXISTS over_quota_reason TEXT;
            ALTER TABLE job_requisitions ADD COLUMN IF NOT EXISTS over_quota_approved_by_id UUID;
            ALTER TABLE job_requisitions ADD COLUMN IF NOT EXISTS over_quota_approved_at TIMESTAMPTZ;

            CREATE TABLE IF NOT EXISTS department_headcount_budgets (
                id UUID PRIMARY KEY,
                department_id UUID NOT NULL REFERENCES departments(id) ON DELETE RESTRICT,
                year INTEGER NOT NULL,
                target_headcount INTEGER NOT NULL DEFAULT 0,
                salary_budget NUMERIC(18, 2) NOT NULL DEFAULT 0,
                currency TEXT NOT NULL DEFAULT 'VND',
                note TEXT,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ,
                CONSTRAINT uq_department_headcount_budgets_dept_year UNIQUE (department_id, year)
            );

            -- Scrum #46: Bảng ghi nhận tương tác, cuộc gọi trao đổi với ứng viên
            CREATE TABLE IF NOT EXISTS candidate_interactions (
                id UUID PRIMARY KEY,
                candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
                application_id UUID REFERENCES applications(id) ON DELETE SET NULL,
                created_by_user_id UUID NOT NULL REFERENCES users(id),
                interaction_type INT NOT NULL DEFAULT 1,
                title TEXT NOT NULL,
                content TEXT NOT NULL,
                outcome TEXT,
                interaction_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS ix_candidate_interactions_candidate_id ON candidate_interactions(candidate_id);
            CREATE INDEX IF NOT EXISTS ix_candidate_interactions_application_id ON candidate_interactions(application_id);
            CREATE INDEX IF NOT EXISTS ix_candidate_interactions_interaction_date ON candidate_interactions(interaction_date);

            -- Scrum #26: Phân công recruiter phụ trách yêu cầu tuyển dụng và lịch sử bàn giao
            CREATE TABLE IF NOT EXISTS requisition_recruiters (
                id UUID PRIMARY KEY,
                requisition_id UUID NOT NULL REFERENCES job_requisitions(id) ON DELETE CASCADE,
                recruiter_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                is_primary BOOLEAN NOT NULL DEFAULT FALSE,
                assigned_by_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
                assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                note TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS ix_requisition_recruiters_req_rec ON requisition_recruiters(requisition_id, recruiter_id);

            CREATE TABLE IF NOT EXISTS requisition_handover_histories (
                id UUID PRIMARY KEY,
                requisition_id UUID NOT NULL REFERENCES job_requisitions(id) ON DELETE CASCADE,
                from_recruiter_id UUID REFERENCES users(id) ON DELETE SET NULL,
                to_recruiter_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
                handover_type VARCHAR(50) NOT NULL DEFAULT 'PRIMARY_TRANSFER',
                reason TEXT NOT NULL DEFAULT '',
                notes TEXT,
                transferred_by_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
                transferred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS ix_requisition_handover_histories_req ON requisition_handover_histories(requisition_id, transferred_at DESC);

            -- Scrum #45: Quản lý kho ứng viên tiềm năng (Talent Pool)
            CREATE TABLE IF NOT EXISTS talent_pool_candidates (
                id UUID PRIMARY KEY,
                candidate_id UUID NOT NULL,
                source_application_id UUID,
                last_stage_reached TEXT,
                reason TEXT NOT NULL,
                target_job_title TEXT NOT NULL,
                skills TEXT,
                experience_years INTEGER,
                rating INTEGER,
                notes TEXT,
                status TEXT NOT NULL DEFAULT 'AVAILABLE',
                added_by_user_id UUID NOT NULL,
                added_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by_id UUID,
                updated_by_id UUID,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                deleted_at TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS ix_talent_pool_candidates_candidate_id ON talent_pool_candidates(candidate_id);
            CREATE INDEX IF NOT EXISTS ix_talent_pool_candidates_target_job_title ON talent_pool_candidates(target_job_title);
            CREATE INDEX IF NOT EXISTS ix_talent_pool_candidates_status ON talent_pool_candidates(status);

            -- Scrum - Số 57: Mở lại phiếu đánh giá trong 24 giờ cho Recruiter
            ALTER TABLE interview_evaluations ADD COLUMN IF NOT EXISTS is_reopened BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE interview_evaluations ADD COLUMN IF NOT EXISTS reopened_at TIMESTAMPTZ;
            ALTER TABLE interview_evaluations ADD COLUMN IF NOT EXISTS reopened_by_user_id UUID REFERENCES users(id) ON DELETE SET NULL;
            ALTER TABLE interview_evaluations ADD COLUMN IF NOT EXISTS reopen_reason TEXT;
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
