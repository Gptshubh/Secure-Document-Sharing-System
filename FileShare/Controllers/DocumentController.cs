using FileShare.Data;
using FileShare.DTO;
using FileShare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FileShare.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConnectionMultiplexer _redis;

        public DocumentController(AppDbContext context, IConnectionMultiplexer redis)
        {
            _context = context;
            _redis = redis;
        }

        [Authorize]
        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromForm] UploadDocumentDto dto)
        {
            if (dto.File == null || dto.File.Length == 0)
                return BadRequest("File is required.");

            var allowedExtensions = new[] { ".pdf", ".docx", ".png", ".jpg" };
            var extension = Path.GetExtension(dto.File.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                return BadRequest("Invalid file type.");

            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier));

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid() + extension;

            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await dto.File.CopyToAsync(stream);
            }

            var document = new Document
            {
                Id = Guid.NewGuid(),
                FileName = dto.File.FileName,
                FilePath = uniqueFileName,
                UploadedBy = userId
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            return Ok(document);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetMyDocuments()
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier));

            var documents = await _context.Documents
                .Where(d => d.UploadedBy == userId && !d.IsDeleted)
                .ToListAsync();

            return Ok(documents);
        }

        [Authorize]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var cacheKey = $"doc:{id}";
            var db = _redis.GetDatabase();

            // Try Redis
            var cachedData = await db.StringGetAsync(cacheKey);

            if (!cachedData.IsNullOrEmpty)
            {
                var cachedDocument = System.Text.Json.JsonSerializer
                    .Deserialize<Document>(cachedData);

                return Ok(cachedDocument);
            }

            // If not in Redis → Fetch from SQL
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

            if (document == null)
                return NotFound();

            // Store in Redis (TTL 5 minutes)
            var serialized = System.Text.Json.JsonSerializer.Serialize(document);

            await db.StringSetAsync(cacheKey, serialized, TimeSpan.FromMinutes(5));

            return Ok(document);
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var document = await _context.Documents
        .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

            if (document == null)
                return NotFound();

            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier));

            var userRole = User.FindFirstValue(ClaimTypes.Role);

            if (document.UploadedBy != userId && userRole != "Admin")
                return Forbid();

            document.IsDeleted = true;
            await _context.SaveChangesAsync();

            var cacheKey = $"doc:{document.Id}";
            await _redis.GetDatabase().KeyDeleteAsync(cacheKey);

            return Ok("Deleted successfully.");
        }



        [HttpGet("{id}/download")]
        public async Task<IActionResult> Download(Guid id)
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

            if (document == null)
                return NotFound();

            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier));

            var userRole = User.FindFirstValue(ClaimTypes.Role);

            // Ownership check
            if (document.UploadedBy != userId && userRole != "Admin")
                return Forbid();

            var uploadsFolder = Path.Combine(
                Directory.GetCurrentDirectory(), "uploads");

            var filePath = Path.Combine(uploadsFolder, document.FilePath);

            if (!System.IO.File.Exists(filePath))
                return NotFound("File not found on server.");

            // LOG DOWNLOAD
            var log = new DownloadLog
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                DownloadedBy = userId,
                Timestamp = DateTime.UtcNow,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };

            _context.DownloadLogs.Add(log);
            await _context.SaveChangesAsync();

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);

            return File(fileBytes, "application/octet-stream", document.FileName);
        }

        [Authorize]
        [HttpPost("{id}/generate-link")]
        public async Task<IActionResult> GenerateDownloadLink(Guid id)
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

            if (document == null)
                return NotFound();

            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier));

            var userRole = User.FindFirstValue(ClaimTypes.Role);

            if (document.UploadedBy != userId && userRole != "Admin")
                return Forbid();

            var token = Guid.NewGuid().ToString();

            var redisDb = _redis.GetDatabase();

            await redisDb.StringSetAsync(
                $"download:{token}",
                document.Id.ToString(),
                TimeSpan.FromMinutes(2));

            var downloadUrl = $"{Request.Scheme}://{Request.Host}/api/documents/download/{token}";

            return Ok(new { url = downloadUrl });
        }

        [AllowAnonymous]
        [HttpGet("download/{token}")]
        public async Task<IActionResult> DownloadWithToken(string token)
        {
            var redisDb = _redis.GetDatabase();

            var documentIdString = await redisDb.StringGetAsync($"download:{token}");

            if (documentIdString.IsNullOrEmpty)
                return Unauthorized("Invalid or expired token.");

            var documentId = Guid.Parse(documentIdString);

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted);

            if (document == null)
                return NotFound();

            //Delete token (single-use)
            await redisDb.KeyDeleteAsync($"download:{token}");

            var uploadsFolder = Path.Combine(
                Directory.GetCurrentDirectory(), "uploads");

            var filePath = Path.Combine(uploadsFolder, document.FilePath);

            if (!System.IO.File.Exists(filePath))
                return NotFound("File not found.");

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

            return File(stream, "application/octet-stream", document.FileName);
        }
    }
}
