using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileShare.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        [Authorize(Roles = "Admin")]
        [HttpGet("dashboard")]
        public IActionResult AdminOnly()
        {
            return Ok("Welcome Admin!");
        }
    }
}
