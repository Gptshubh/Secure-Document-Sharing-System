using FileShare.Data;
using FileShare.DTO;
using FileShare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace FileShare.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class AuthController : ControllerBase
	{
		private readonly AppDbContext _context;
		private readonly IConfiguration _config;
        private readonly IConnectionMultiplexer _redis;

        public AuthController(AppDbContext context, IConfiguration config, IConnectionMultiplexer redis)
		{
			_context = context;
			_config = config;
			_redis = redis;
		}

		[HttpPost("register")]
		public async Task<IActionResult> Register(RegisterDto dto)
		{
			if (await _context.Users.AnyAsync(x => x.Email == dto.Email))
				return BadRequest("User already exists.");

			var user = new User
			{
				Id = Guid.NewGuid(),
				Email = dto.Email
			};

			var hasher = new PasswordHasher<User>();
			user.PasswordHash = hasher.HashPassword(user, dto.Password);

			_context.Users.Add(user);
			await _context.SaveChangesAsync();

			return Ok("User created successfully.");
		}

		[HttpPost("login")]
		public async Task<IActionResult> Login(LoginDto dto)
		{
			var user = await _context.Users
				.FirstOrDefaultAsync(x => x.Email == dto.Email);

			if (user == null)
				return Unauthorized("Invalid credentials.");

			var hasher = new PasswordHasher<User>();
			var result = hasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);

			if (result == PasswordVerificationResult.Failed)
				return Unauthorized("Invalid credentials.");

			var claims = new[]
			{
				new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
				new Claim(ClaimTypes.Role, user.Role),
				new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
			};

			var key = new SymmetricSecurityKey(
				Encoding.UTF8.GetBytes(_config["Jwt:Key"]));

			var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

			var token = new JwtSecurityToken(
				issuer: _config["Jwt:Issuer"],
				audience: _config["Jwt:Audience"],
				claims: claims,
				expires: DateTime.UtcNow.AddMinutes(
					Convert.ToDouble(_config["Jwt:ExpiryMinutes"])),
				signingCredentials: creds);

			return Ok(new
			{
				token = new JwtSecurityTokenHandler().WriteToken(token)
			});
		}


        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);

            if (string.IsNullOrEmpty(jti))
                return BadRequest("Invalid token.");

            var db = _redis.GetDatabase();

            // Get token expiry
            var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);

            var expiryTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expClaim));
            var remainingTime = expiryTime - DateTimeOffset.UtcNow;

            if (remainingTime.TotalSeconds > 0)
            {
                await db.StringSetAsync(
                    $"blacklist:{jti}",
                    "revoked",
                    remainingTime);
            }

            return Ok("Logged out successfully.");
        }
    }
}
