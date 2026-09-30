using Ats.Web.Constants;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ats.Web.ViewComponents;

/// <summary>
/// ViewComponent render menu điều hướng theo phân quyền người dùng (EP-01_UserRole).
/// Đọc Role từ ClaimsPrincipal (AtsCookieScheme) để quyết định hiển thị mục menu nào.
/// </summary>
public class NavMenuViewComponent : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var role = HttpContext.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        ViewBag.UserRole = role;
        ViewBag.FullName = HttpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        ViewBag.IsAuthenticated = HttpContext.User.Identity?.IsAuthenticated ?? false;
        return View();
    }
}
