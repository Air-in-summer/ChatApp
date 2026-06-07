using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.User.Core.Entities;

namespace MultiRoomChatWebApp.Server.Infrastructure.Database;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMember> RoomMembers => Set<RoomMember>();
    public DbSet<ReadReceipt> ReadReceipts => Set<ReadReceipt>();
    public DbSet<Modules.Group.Core.Entities.Group> Groups => Set<Modules.Group.Core.Entities.Group>();
    public DbSet<Modules.Group.Core.Entities.GroupMember> GroupMembers => Set<Modules.Group.Core.Entities.GroupMember>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<UserPresenceState> UserPresenceStates => Set<UserPresenceState>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global Query Filter cho Soft Delete của Group
        modelBuilder.Entity<Modules.Group.Core.Entities.Group>()
                    .HasQueryFilter(g => g.DeletedAt == null);

        modelBuilder.Entity<Room>()
                    .HasQueryFilter(r => r.DeletedAt == null);

        modelBuilder.Entity<ReadReceipt>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoomId });

            entity.HasOne(d => d.User)
                  .WithMany()
                  .HasForeignKey(d => d.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Room)
                  .WithMany()
                  .HasForeignKey(d => d.RoomId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Kích hoạt extension pg_trgm của PostgreSQL để hỗ trợ ILIKE '%...%'
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            
            // Tách riêng GIN Index cho tìm kiếm (Không dùng làm Unique vì GIN không hỗ trợ)
            entity.HasIndex(e => e.Username)
                  .HasDatabaseName("IX_Users_Username_Trgm")
                  .HasMethod("gin")
                  .HasOperators("gin_trgm_ops");
                  
            entity.HasIndex(e => e.DisplayName)
                  .HasDatabaseName("IX_Users_DisplayName_Trgm")
                  .HasMethod("gin")
                  .HasOperators("gin_trgm_ops");

            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DisplayName).HasMaxLength(100);
            entity.Property(e => e.PasswordHash);
            entity.Property(e => e.AvatarUrl).HasMaxLength(2048);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            
            entity.HasOne(d => d.User)
                  .WithMany(p => p.RefreshTokens)
                  .HasForeignKey(d => d.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalLogin>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ProviderUserId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ProviderEmail).IsRequired().HasMaxLength(255);

            entity.HasOne(d => d.User)
                  .WithMany(p => p.ExternalLogins)
                  .HasForeignKey(d => d.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FriendRequest>(entity =>
        {
            entity.ToTable("FriendRequests", table =>
                table.HasCheckConstraint("CK_FriendRequests_NotSelf", "\"RequesterId\" <> \"ReceiverId\""));

            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ReceiverId, e.Status, e.CreatedAt });
            entity.HasIndex(e => new { e.RequesterId, e.Status, e.CreatedAt });
            entity.HasIndex(e => new { e.RequesterId, e.ReceiverId, e.Status });

            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(d => d.Requester)
                  .WithMany()
                  .HasForeignKey(d => d.RequesterId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Receiver)
                  .WithMany()
                  .HasForeignKey(d => d.ReceiverId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.ToTable("Friendships", table =>
                table.HasCheckConstraint("CK_Friendships_NormalizedPair", "\"UserAId\" < \"UserBId\""));

            entity.HasKey(e => new { e.UserAId, e.UserBId });
            entity.HasIndex(e => new { e.UserBId, e.UserAId });

            entity.HasOne(d => d.UserA)
                  .WithMany()
                  .HasForeignKey(d => d.UserAId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.UserB)
                  .WithMany()
                  .HasForeignKey(d => d.UserBId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserBlock>(entity =>
        {
            entity.ToTable("UserBlocks", table =>
                table.HasCheckConstraint("CK_UserBlocks_NotSelf", "\"BlockerId\" <> \"BlockedId\""));

            entity.HasKey(e => new { e.BlockerId, e.BlockedId });
            entity.HasIndex(e => new { e.BlockedId, e.BlockerId });

            entity.HasOne(d => d.Blocker)
                  .WithMany()
                  .HasForeignKey(d => d.BlockerId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Blocked)
                  .WithMany()
                  .HasForeignKey(d => d.BlockedId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserPresenceState>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.HasOne(d => d.User)
                  .WithMany()
                  .HasForeignKey(d => d.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.ToTable("MediaAssets", table =>
                table.HasCheckConstraint("CK_MediaAssets_SizeBytes_NonNegative", "\"SizeBytes\" >= 0"));

            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.BucketName, e.StorageKey }).IsUnique();
            entity.HasIndex(e => new { e.OwnerUserId, e.CreatedAt });
            entity.HasIndex(e => new { e.RoomId, e.CreatedAt });
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
            entity.HasIndex(e => new { e.Status, e.ReservedAt });
            entity.HasIndex(e => new { e.Scope, e.OwnerUserId });

            entity.Property(e => e.Scope).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.AccessLevel).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

            entity.Property(e => e.BucketName).IsRequired().HasMaxLength(120);
            entity.Property(e => e.StorageKey).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(120);
            entity.Property(e => e.PublicUrl).HasMaxLength(2048);
            entity.Property(e => e.MessageId).HasMaxLength(64);
            entity.Property(e => e.ReservedByMessageId).HasMaxLength(64);

            entity.HasOne(d => d.OwnerUser)
                  .WithMany()
                  .HasForeignKey(d => d.OwnerUserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.CreatedBy);
            
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.DeletedAt);
            
            entity.HasOne(d => d.Creator)
                  .WithMany(p => p.CreatedRooms)
                  .HasForeignKey(d => d.CreatedBy)
                  .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ với Group
            entity.HasOne<Modules.Group.Core.Entities.Group>()
                  .WithMany(p => p.Rooms)
                  .HasForeignKey(d => d.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoomMember>(entity =>
        {
            entity.HasKey(e => new { e.RoomId, e.UserId });
            entity.HasIndex(e => new { e.UserId, e.RoomId });
            
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            
            entity.HasOne(d => d.Room)
                  .WithMany(p => p.Members)
                  .HasForeignKey(d => d.RoomId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(d => d.User)
                  .WithMany(p => p.RoomMemberships)
                  .HasForeignKey(d => d.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Modules.Group.Core.Entities.Group>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InviteCode).IsUnique();
            entity.HasIndex(e => e.OwnerId);

            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.InviteCode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Description).HasMaxLength(255);

            entity.HasOne(d => d.Owner)
                  .WithMany()
                  .HasForeignKey(d => d.OwnerId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Modules.Group.Core.Entities.GroupMember>(entity =>
        {
            entity.HasKey(e => new { e.GroupId, e.UserId });
            
            // Composite Index tối ưu cho "Lấy danh sách Server của tôi"
            entity.HasIndex(e => new { e.UserId, e.GroupId });

            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(d => d.Group)
                  .WithMany(p => p.Members)
                  .HasForeignKey(d => d.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.User)
                  .WithMany(p => p.GroupMemberships)
                  .HasForeignKey(d => d.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
