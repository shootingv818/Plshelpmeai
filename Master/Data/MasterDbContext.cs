using IvaScanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace IvaScanner.Master.Data
{
    public class MasterDbContext : DbContext
    {
        public MasterDbContext(DbContextOptions<MasterDbContext> options) : base(options)
        {
        }

        public DbSet<Worker> Workers { get; set; }
        public DbSet<IvaAccount> IvaAccounts { get; set; }
        public DbSet<ScanJob> ScanJobs { get; set; }
        public DbSet<ScanTask> ScanTasks { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
    
    // Remote Server Management
    public DbSet<RemoteServer> RemoteServers { get; set; }
    public DbSet<RemoteWorker> RemoteWorkers { get; set; }
    public DbSet<ServerHealthCheck> ServerHealthChecks { get; set; }
    public DbSet<DeploymentJob> DeploymentJobs { get; set; }
    public DbSet<DeploymentStep> DeploymentSteps { get; set; }
        
        // Proxy Management
        public DbSet<ProxyServer> ProxyServers { get; set; }
        public DbSet<ProxyUsageLog> ProxyUsageLogs { get; set; }
        public DbSet<ProxyHealthCheck> ProxyHealthChecks { get; set; }
        public DbSet<ProxyPool> ProxyPools { get; set; }
        public DbSet<ProxyPoolMember> ProxyPoolMembers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Worker configuration
            modelBuilder.Entity<Worker>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.LastHeartbeat);
                entity.Property(e => e.Name).HasMaxLength(100);
                entity.Property(e => e.ProxyUrl).HasMaxLength(500);
                entity.Property(e => e.LastError).HasMaxLength(1000);

                // One-to-one relationship with IvaAccount
                entity.HasOne(w => w.IvaAccount)
                      .WithOne(a => a.AssignedWorker)
                      .HasForeignKey<IvaAccount>(a => a.AssignedWorkerId)
                      .IsRequired(false);

                // One-to-many relationship with ScanTasks
                entity.HasMany(w => w.Tasks)
                      .WithOne(t => t.Worker)
                      .HasForeignKey(t => t.WorkerId)
                      .IsRequired(false);
            });

            // IvaAccount configuration
            modelBuilder.Entity<IvaAccount>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PhoneNumber).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.IsActive);
                entity.Property(e => e.PhoneNumber).HasMaxLength(15);
                entity.Property(e => e.LastError).HasMaxLength(1000);
            });

            // ScanJob configuration
            modelBuilder.Entity<ScanJob>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);
                entity.Property(e => e.CardNumber).HasMaxLength(16);
                entity.Property(e => e.PhoneNumbers).HasMaxLength(2000);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.ErrorMessage).HasMaxLength(2000);

                // One-to-many relationship with ScanTasks
                entity.HasMany(j => j.Tasks)
                      .WithOne(t => t.Job)
                      .HasForeignKey(t => t.JobId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ScanTask configuration
            modelBuilder.Entity<ScanTask>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.JobId);
                entity.HasIndex(e => e.WorkerId);
                entity.HasIndex(e => e.LeaseExpiry);
                entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            });

            // SystemLog configuration
            modelBuilder.Entity<SystemLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.Level);
                entity.HasIndex(e => e.Source);
                entity.HasIndex(e => e.WorkerId);
                entity.HasIndex(e => e.JobId);
                entity.Property(e => e.Level).HasConversion<string>().HasMaxLength(20);
                entity.Property(e => e.Source).HasMaxLength(200);
                entity.Property(e => e.Category).HasMaxLength(100);
                entity.Property(e => e.Message).HasMaxLength(2000);
                entity.Property(e => e.Exception).HasMaxLength(4000);
                entity.Property(e => e.Properties).HasMaxLength(4000);
                entity.Property(e => e.WorkerId).HasMaxLength(50);
                entity.Property(e => e.JobId).HasMaxLength(50);
                entity.Property(e => e.TaskId).HasMaxLength(50);
            });
        }
    }
}

            // Configure ProxyServer
            modelBuilder.Entity<ProxyServer>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Host).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Port).IsRequired();
                entity.Property(e => e.Type).IsRequired();
                entity.Property(e => e.Status).IsRequired();
                entity.HasIndex(e => new { e.Host, e.Port }).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.Country);
                entity.HasIndex(e => e.Priority);
                entity.HasIndex(e => e.IsActive);
                
                // Relationships
                entity.HasMany(e => e.UsageLogs)
                      .WithOne(e => e.Proxy)
                      .HasForeignKey(e => e.ProxyId)
                      .OnDelete(DeleteBehavior.Cascade);
                      
                entity.HasMany(e => e.HealthChecks)
                      .WithOne(e => e.Proxy)
                      .HasForeignKey(e => e.ProxyId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure ProxyUsageLog
            modelBuilder.Entity<ProxyUsageLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProxyId).IsRequired();
                entity.HasIndex(e => new { e.ProxyId, e.UsedAt });
                entity.HasIndex(e => e.WorkerId);
                entity.HasIndex(e => e.JobId);
                entity.HasIndex(e => e.Success);
            });

            // Configure ProxyHealthCheck
            modelBuilder.Entity<ProxyHealthCheck>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProxyId).IsRequired();
                entity.Property(e => e.TestUrl).IsRequired().HasMaxLength(500);
                entity.HasIndex(e => new { e.ProxyId, e.CheckedAt });
                entity.HasIndex(e => e.IsHealthy);
            });

            // Configure ProxyPool
            modelBuilder.Entity<ProxyPool>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Strategy).IsRequired();
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.IsActive);
                
                // Relationships
                entity.HasMany(e => e.Members)
                      .WithOne(e => e.ProxyPool)
                      .HasForeignKey(e => e.ProxyPoolId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure ProxyPoolMember
            modelBuilder.Entity<ProxyPoolMember>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProxyPoolId).IsRequired();
                entity.Property(e => e.ProxyId).IsRequired();
                entity.HasIndex(e => new { e.ProxyPoolId, e.ProxyId }).IsUnique();
                entity.HasIndex(e => e.IsEnabled);
                
                // Relationships
                entity.HasOne(e => e.Proxy)
                      .WithMany()
                      .HasForeignKey(e => e.ProxyId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

        
        // Configure RemoteServer entities
        modelBuilder.Entity<RemoteServer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.IpAddress).IsRequired().HasMaxLength(45);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.IpAddress).IsUnique();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<RemoteWorker>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WorkerId).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.Server)
                .WithMany(s => s.Workers)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServerHealthCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Server)
                .WithMany(s => s.HealthChecks)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.CheckedAt);
        });

        modelBuilder.Entity<DeploymentJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Server)
                .WithMany()
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.StartedAt);
        });

        modelBuilder.Entity<DeploymentStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Command).IsRequired();
            entity.HasOne(e => e.Job)
                .WithMany(j => j.Steps)
                .HasForeignKey(e => e.JobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.Order);
        });