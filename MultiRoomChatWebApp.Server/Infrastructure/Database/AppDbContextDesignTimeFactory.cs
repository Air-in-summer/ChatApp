using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MultiRoomChatWebApp.Server.Infrastructure.Database;

/// <summary>
/// Tạo <see cref="AppDbContext"/> cho các lệnh EF Core design-time như generate migration script.
/// </summary>
/// <remarks>
/// Luồng xử lý:
/// 1. Ưu tiên đọc connection string từ biến môi trường `ConnectionStrings__PostgreSQL`.
/// 2. Nếu không có, dùng connection string local giả để EF Core vẫn dựng được model/migration.
/// 3. Trả về DbContext trực tiếp, tránh bootstrap `Program.cs` và các dependency runtime như Redis/MinIO/LiveKit.
///
/// Lưu ý:
/// - Factory này không được ASP.NET Core DI dùng khi app chạy thật.
/// - Lệnh `dotnet ef migrations script` không kết nối database, nên fallback local chỉ là cấu hình provider.
/// </remarks>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>
    /// Tạo DbContext cho EF Core CLI.
    /// </summary>
    /// <param name="args">Tham số CLI do EF Core truyền vào, hiện chưa cần dùng.</param>
    /// <returns>DbContext đã cấu hình Npgsql provider để EF Core đọc migrations.</returns>
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? "Host=localhost;Port=5432;Database=ChatAppDB;Username=design_time_user;Password=design_time_password";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
