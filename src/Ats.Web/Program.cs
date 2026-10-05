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


// Khởi tạo seed data tự động
using (var scope = app.Services.CreateScope())
{
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

    await DatabaseSeeder.SeedAsync(dbContext);
}

app.Run();
