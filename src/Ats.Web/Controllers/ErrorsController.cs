using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

/// <summary>
/// Controller xử lý các trang lỗi HTTP thân thiện (401, 403, 404, 500).
/// </summary>
[AllowAnonymous]
[Route("errors")]
public class ErrorsController : Controller
{
    [HttpGet("{statusCode:int}")]
    [HttpPost("{statusCode:int}")]
    public IActionResult HandleStatusCode(int statusCode)
    {
        ViewData["StatusCode"] = statusCode;

        return statusCode switch
        {
            401 => View("UnauthorizedError"),
            403 => View("ForbiddenError"),
            404 => View("NotFoundError"),
            500 => View("ServerError"),
            _ => View("GenericError")
        };
    }
}
