using FileShare.Models;
using Microsoft.EntityFrameworkCore;

namespace FileShare.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<DownloadLog> DownloadLogs { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Document>()
                .HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
