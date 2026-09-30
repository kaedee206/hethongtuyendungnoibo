namespace Ats.Web.Constants;

public static class EmailTemplates
{
    public static string LoginSuccess(string fullName, string timeString) => $@"
            <p>Xin chào <b>{fullName}</b>,</p>
            <p>Tài khoản của bạn vừa đăng nhập thành công vào hệ thống ATS lúc {timeString}.</p>";

    public static string ForgotPassword(string resetLink) => $@"
                <h3>Yêu cầu đặt lại mật khẩu</h3>
                <p>Xin chào,</p>
                <p>Bạn đã yêu cầu đặt lại mật khẩu cho tài khoản hệ thống ATS. Vui lòng click vào liên kết bên dưới để đặt lại mật khẩu:</p>
                <p><a href='{resetLink}'>{resetLink}</a></p>
                <p>Liên kết này sẽ hết hạn trong vòng 30 phút.</p>
                <p>Nếu bạn không yêu cầu, vui lòng bỏ qua email này.</p>
            ";

    public static string PasswordChanged(string timeString, string ip, string userAgent) => $@"
            <h3>Cảnh báo bảo mật</h3>
            <p>Xin chào,</p>
            <p>Mật khẩu cho tài khoản ATS của bạn vừa được thay đổi thành công vào lúc {timeString} (UTC).</p>
            <ul>
                <li><strong>IP thực hiện:</strong> {ip}</li>
                <li><strong>Thiết bị/Trình duyệt:</strong> {userAgent}</li>
            </ul>
            <p>Nếu bạn không thực hiện yêu cầu này, tài khoản của bạn có thể đã bị xâm phạm. Vui lòng liên hệ với Quản trị viên (Admin) ngay lập tức để khóa tài khoản và được hỗ trợ.</p>
        ";

    public static string AccountCreated(string fullName, string email, string tempPassword, string loginLink) => $@"
            <h3>Chào mừng bạn đến với ATS</h3>
            <p>Xin chào {fullName},</p>
            <p>Tài khoản nội bộ của bạn đã được tạo thành công.</p>
            <p>Dưới đây là thông tin đăng nhập của bạn:</p>
            <ul>
                <li><strong>Email:</strong> {email}</li>
                <li><strong>Mật khẩu tạm:</strong> {tempPassword}</li>
            </ul>
            <p>Vui lòng đăng nhập tại <a href='{loginLink}'>{loginLink}</a> và tiến hành <strong>đổi mật khẩu ngay lập tức</strong> để đảm bảo an toàn.</p>
        ";

    public static string ActivationEmailResent(string fullName, string email, string tempPassword, string loginLink) => $@"
            <h3>Chào mừng bạn đến với ATS</h3>
            <p>Xin chào {fullName},</p>
            <p>Admin vừa gửi lại thông tin đăng nhập cho bạn.</p>
            <p>Dưới đây là mật khẩu mới của bạn (mật khẩu cũ đã bị vô hiệu hóa):</p>
            <ul>
                <li><strong>Email:</strong> {email}</li>
                <li><strong>Mật khẩu tạm:</strong> {tempPassword}</li>
            </ul>
            <p>Vui lòng đăng nhập tại <a href='{loginLink}'>{loginLink}</a> và đổi mật khẩu ngay lập tức.</p>";
}
