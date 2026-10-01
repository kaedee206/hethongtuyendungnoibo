using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ats.Web.Models;

namespace Ats.Web.Controllers;

public class HomeController(ILogger<HomeController> logger) : Controller
{
    private readonly ILogger<HomeController> _logger = logger;


    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("/landing")]
    public IActionResult Landing()
    {
        return View("Landing");
    }


    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
