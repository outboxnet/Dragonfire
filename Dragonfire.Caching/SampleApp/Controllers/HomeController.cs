using CacheTesting.Service;
using Microsoft.AspNetCore.Mvc;
using SampleApp.Models;
using System.Diagnostics;

namespace CacheTesting.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        IDataService dataService;

        public HomeController(ILogger<HomeController> logger, IDataService dataService)
        {
            _logger = logger;
            this.dataService = dataService;
        }

        public async Task<IActionResult> Index([FromQuery] string id)
        {
            var data = await dataService.GetAsync(id);

            return new JsonResult(data);
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
}
