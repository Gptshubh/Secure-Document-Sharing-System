using StackExchange.Redis;
using System.Security.Claims;

namespace FileShare.Middleware
{
    public class RateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConnectionMultiplexer _redis;
        private const int LIMIT = 5; // 20 requests per minute

        public RateLimitMiddleware(RequestDelegate next, IConnectionMultiplexer redis)
        {
            _next = next;
            _redis = redis;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Skip rate limiting for anonymous endpoints like register, login and downloading through link
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                await _next(context);
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                await _next(context);
                return;
            }

            var db = _redis.GetDatabase();
            var key = $"rate:{userId}";

            var count = await db.StringIncrementAsync(key);

            if (count == 1)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromMinutes(1));
            }

            if (count > LIMIT)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsync("Rate limit exceeded. Try again later.");
                return;
            }

            await _next(context);
        }
    }
}
