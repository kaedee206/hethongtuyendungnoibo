using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class ProfilesController : Controller
{
    [HttpGet("/ho-so")]
    public IActionResult Index()
    {
        return View();
    }
}
