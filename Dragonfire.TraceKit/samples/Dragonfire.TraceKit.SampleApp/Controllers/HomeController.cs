using Microsoft.AspNetCore.Mvc;

namespace Dragonfire.TraceKit.SampleApp.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
