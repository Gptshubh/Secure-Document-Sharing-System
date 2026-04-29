using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileShare.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        [Authorize]
        [HttpGet("secure")]
        public IActionResult SecureEndpoint()
        {
            return Ok("You accessed a protected route!");
        }
    }
}
