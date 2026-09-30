namespace Ats.Web.Constants;

public static class AppConstants
{
    // Lấy URL frontend từ biến môi trường (FRONTEND_URL), mặc định là localhost:3000 nếu chưa cấu hình
    public static string FrontendUrl => Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:3000";

    // Các đường dẫn (link) được sử dụng trong email
    public static string LoginUrl => $"{FrontendUrl}/dang-nhap";
    public static string ForgotPasswordUrl => $"{FrontendUrl}/dat-lai-mat-khau";
}
