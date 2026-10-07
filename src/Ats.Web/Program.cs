using Ats.Web.Data;
using Ats.Web.Services;
using Ats.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

DotNetEnv.Env.TraversePath().Load();
if (File.Exists(".env")) DotNetEnv.Env.Load(".env");
if (File.Exists("src/Ats.Web/.env")) DotNetEnv.Env.Load("src/Ats.Web/.env");
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

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
    })
    .AddCookie("ExternalCookieScheme", options =>
    {
        options.Cookie.Name = "Ats.ExternalCookie";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
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

app.UseHttpsRedirection();
app.UseRouting();

// Kích hoạt Session & Auth đúng thứ tự pipeline
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Kích hoạt Middleware gia hạn phiên tự động
app.UseMiddleware<Ats.Web.Middlewares.SessionActivityMiddleware>();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


// Khởi tạo seed data tự động và đảm bảo schema bảng đầy đủ
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        await dbContext.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE departments ADD COLUMN IF NOT EXISTS level INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE departments ADD COLUMN IF NOT EXISTS path TEXT NOT NULL DEFAULT '';
            UPDATE departments SET path = '/' || id || '/' WHERE path = '' OR path IS NULL;
            ALTER TABLE job_requisitions ADD COLUMN IF NOT EXISTS salary_band_explanation TEXT;
            ALTER TABLE job_requisitions ADD COLUMN IF NOT EXISTS location_id UUID;
            ALTER TABLE job_requisitions ADD COLUMN IF NOT EXISTS work_type_id UUID;
            ALTER TABLE job_positions ADD COLUMN IF NOT EXISTS competency_framework_id UUID;

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
                framework_id UUID NOT NULL REFERENCES competency_frameworks(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                description TEXT,
                weight INTEGER NOT NULL DEFAULT 1,
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

    await DatabaseSeeder.SeedAsync(dbContext);
}

app.Run();
