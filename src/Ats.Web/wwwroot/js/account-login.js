// Logic xử lý trang đăng nhập
document.addEventListener("DOMContentLoaded", function () {
    const togglePasswordBtn = document.querySelector(".login-card__toggle-password");
    if (togglePasswordBtn) {
        const passwordInput = document.querySelector("input[name='Password']");
        const icon = togglePasswordBtn.querySelector("i");

        togglePasswordBtn.addEventListener("click", function () {
            if (!passwordInput) return;

            const isPassword = passwordInput.getAttribute("type") === "password";
            passwordInput.setAttribute("type", isPassword ? "text" : "password");

            if (icon) {
                icon.classList.toggle("bi-eye", !isPassword);
                icon.classList.toggle("bi-eye-slash", isPassword);
            }

            togglePasswordBtn.setAttribute("aria-pressed", isPassword ? "true" : "false");
        });
    }
});
