// Logic hiển thị/ẩn mật khẩu trên form đăng nhập
document.addEventListener('DOMContentLoaded', function () {
    const toggleButton = document.querySelector('.login-card__toggle-password');
    const passwordInput = document.querySelector('input[type="password"], input[name="Password"]');

    if (toggleButton && passwordInput) {
        toggleButton.addEventListener('click', function () {
            const isPassword = passwordInput.getAttribute('type') === 'password';
            passwordInput.setAttribute('type', isPassword ? 'text' : 'password');

            const icon = toggleButton.querySelector('i');
            if (icon) {
                icon.classList.toggle('bi-eye', !isPassword);
                icon.classList.toggle('bi-eye-slash', isPassword);
            }

            toggleButton.setAttribute('aria-pressed', isPassword ? 'true' : 'false');
        });
    }
});
