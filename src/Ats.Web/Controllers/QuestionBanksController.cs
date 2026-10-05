using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ats.Web.Constants;

namespace Ats.Web.Controllers;

[Authorize(Roles = UserRoles.HRManager + "," + UserRoles.Recruiter + "," + UserRoles.Admin)]
[Route("question-banks")]
public class QuestionBanksController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }
}
