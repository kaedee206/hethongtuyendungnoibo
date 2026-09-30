// Logic xử lý trang đăng nhập
document.addEventListener("DOMContentLoaded", function () {
    // 1. Hiện / ẩn mật khẩu
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

    // 2. Nút điền nhanh tài khoản demo các vai trò (EP-01 Navigation testing)
    const roleBtns = document.querySelectorAll(".ats-role-btn");
    const emailInput = document.querySelector("input[name='Email']");
    const passwordInput = document.querySelector("input[name='Password']");

    roleBtns.forEach(btn => {
        btn.addEventListener("click", function () {
            const email = this.getAttribute("data-email");
            const pass = this.getAttribute("data-pass");

            if (emailInput && email) {
                emailInput.value = email;
                emailInput.focus();
            }
            if (passwordInput && pass) {
                passwordInput.value = pass;
            }

            // Visual feedback
            roleBtns.forEach(b => b.classList.remove("active-role-pick"));
            this.classList.add("active-role-pick");
        });
    });
});
