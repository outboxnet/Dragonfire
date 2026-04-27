using Dragonfire.Logging.Attributes;
using Microsoft.AspNetCore.Mvc;
using SampleApp.Models;
using SampleApp.Services;
using System.Diagnostics;

namespace SampleApp.Controllers
{
    public class OrderResponse
    {
        [LogProperty]
        public string CreatedOrderId { get; set; }
    }

    [Log]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        IOrderService orderService;

        public HomeController(ILogger<HomeController> logger, IOrderService orderService)
        {
            _logger = logger;
            this.orderService = orderService;
        }

        public async Task<IActionResult> Index([FromQuery] string? id)
        {
            var resuklt = await orderService.GetOrderAsync(id, "AAAA");

            return new JsonResult(new OrderResponse
            {
                CreatedOrderId = "ASDASD"
            });
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
