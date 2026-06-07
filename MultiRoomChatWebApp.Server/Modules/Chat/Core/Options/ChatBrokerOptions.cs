namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;

/// <summary>
/// Cau hinh broker dung cho luong message V2.
/// </summary>
public sealed class ChatBrokerOptions
{
    public const string SectionName = "ChatBroker";

    public string ConnectionStringName { get; set; } = "ChatBrokerRedis";

    public int Database { get; set; }

    public string InstanceIdEnvironmentVariable { get; set; } = "CHAT_APP_INSTANCE_ID";

    public string ConsumerNamePrefix { get; set; } = "chatapp";

    public string StreamName { get; set; } = "chat:{messages}:v1";

    public string PersistenceGroupName { get; set; } = "message-persistence-v1";

    public string DeliveryGroupName { get; set; } = "message-delivery-v1";

    public int ReadBatchSize { get; set; } = 10;

    public int VisibilityTimeoutSeconds { get; set; } = 60;

    public int MaxAttempts { get; set; } = 5;

    public int RetryBaseDelaySeconds { get; set; } = 60;

    public int RetryMaxDelaySeconds { get; set; } = 900;

    public int RetryStateTtlHours { get; set; } = 168;

    public int ProcessedStreamGraceMinutes { get; set; } = 5;

    public int DeadLetterRetentionHours { get; set; } = 168;

    public int IdempotencyTtlMinutes { get; set; } = 30;

    public int MaintenanceIntervalSeconds { get; set; } = 300;

    public int MaintenanceLockSeconds { get; set; } = 120;

    public TimeSpan VisibilityTimeout => TimeSpan.FromSeconds(VisibilityTimeoutSeconds);

    public TimeSpan RetryBaseDelay => TimeSpan.FromSeconds(RetryBaseDelaySeconds);

    public TimeSpan RetryMaxDelay => TimeSpan.FromSeconds(RetryMaxDelaySeconds);

    public TimeSpan RetryStateTtl => TimeSpan.FromHours(RetryStateTtlHours);

    public TimeSpan ProcessedStreamGrace => TimeSpan.FromMinutes(ProcessedStreamGraceMinutes);

    public TimeSpan DeadLetterRetention => TimeSpan.FromHours(DeadLetterRetentionHours);

    public TimeSpan IdempotencyTtl => TimeSpan.FromMinutes(IdempotencyTtlMinutes);

    public TimeSpan MaintenanceInterval => TimeSpan.FromSeconds(MaintenanceIntervalSeconds);

    public TimeSpan MaintenanceLockTtl => TimeSpan.FromSeconds(MaintenanceLockSeconds);

    /// <summary>
    /// Kiem tra cau hinh broker truoc khi ung dung khoi dong.
    /// </summary>
    public bool IsValid(out string reason)
    {
        if (string.IsNullOrWhiteSpace(ConnectionStringName))
        {
            reason = "ChatBroker:ConnectionStringName khong duoc de trong.";
            return false;
        }

        if (Database is < 0 or > 15)
        {
            reason = "ChatBroker:Database phai nam trong khoang 0..15.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(InstanceIdEnvironmentVariable))
        {
            reason = "ChatBroker:InstanceIdEnvironmentVariable khong duoc de trong.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ConsumerNamePrefix))
        {
            reason = "ChatBroker:ConsumerNamePrefix khong duoc de trong.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(StreamName))
        {
            reason = "ChatBroker:StreamName khong duoc de trong.";
            return false;
        }

        if (!HasRedisHashTag(StreamName))
        {
            reason = "ChatBroker:StreamName phai co Redis hash tag, vi du chat:{messages}:v1.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(PersistenceGroupName))
        {
            reason = "ChatBroker:PersistenceGroupName khong duoc de trong.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DeliveryGroupName))
        {
            reason = "ChatBroker:DeliveryGroupName khong duoc de trong.";
            return false;
        }

        if (PersistenceGroupName == DeliveryGroupName)
        {
            reason = "Persistence group va delivery group phai tach nhau.";
            return false;
        }

        if (ReadBatchSize is < 1 or > 1000)
        {
            reason = "ChatBroker:ReadBatchSize phai nam trong khoang 1..1000.";
            return false;
        }

        if (VisibilityTimeoutSeconds is < 5 or > 3600)
        {
            reason = "ChatBroker:VisibilityTimeoutSeconds phai nam trong khoang 5..3600.";
            return false;
        }

        if (MaxAttempts is < 1 or > 100)
        {
            reason = "ChatBroker:MaxAttempts phai nam trong khoang 1..100.";
            return false;
        }

        if (RetryBaseDelaySeconds < VisibilityTimeoutSeconds ||
            RetryBaseDelaySeconds > 3600)
        {
            reason = "ChatBroker:RetryBaseDelaySeconds phai >= VisibilityTimeoutSeconds va <= 3600.";
            return false;
        }

        if (RetryMaxDelaySeconds < RetryBaseDelaySeconds ||
            RetryMaxDelaySeconds > 86400)
        {
            reason = "ChatBroker:RetryMaxDelaySeconds phai >= RetryBaseDelaySeconds va <= 86400.";
            return false;
        }

        if (RetryStateTtlHours is < 1 or > 2160)
        {
            reason = "ChatBroker:RetryStateTtlHours phai nam trong khoang 1..2160.";
            return false;
        }

        if (ProcessedStreamGraceMinutes is < 0 or > 1440)
        {
            reason = "ChatBroker:ProcessedStreamGraceMinutes phai nam trong khoang 0..1440.";
            return false;
        }

        if (DeadLetterRetentionHours is < 1 or > 2160)
        {
            reason = "ChatBroker:DeadLetterRetentionHours phai nam trong khoang 1..2160.";
            return false;
        }

        if (IdempotencyTtlMinutes is < 1 or > 1440)
        {
            reason = "ChatBroker:IdempotencyTtlMinutes phai nam trong khoang 1..1440.";
            return false;
        }

        if (MaintenanceIntervalSeconds is < 10 or > 86400)
        {
            reason = "ChatBroker:MaintenanceIntervalSeconds phai nam trong khoang 10..86400.";
            return false;
        }

        if (MaintenanceLockSeconds is < 5 or > 3600 ||
            MaintenanceLockSeconds >= MaintenanceIntervalSeconds)
        {
            reason = "ChatBroker:MaintenanceLockSeconds phai >= 5, < MaintenanceIntervalSeconds va <= 3600.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool HasRedisHashTag(string key)
    {
        var start = key.IndexOf('{', StringComparison.Ordinal);
        var end = key.IndexOf('}', StringComparison.Ordinal);
        return start >= 0 && end > start + 1;
    }
}
