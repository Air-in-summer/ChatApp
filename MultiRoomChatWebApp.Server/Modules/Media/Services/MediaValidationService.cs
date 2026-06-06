using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <summary>
/// Validate media upload bang size, extension, MIME va magic bytes.
/// </summary>
public sealed class MediaValidationService : IMediaValidationService
{
    private sealed record ChatMediaValidationRule(
        MediaKind Kind,
        string StoragePrefix,
        IReadOnlySet<string> ContentTypes,
        Func<byte[], int, bool> MagicBytesValidator);

    private static readonly IReadOnlyDictionary<string, string> AvatarContentTypesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

    private static readonly IReadOnlyDictionary<string, ChatMediaValidationRule> ChatMediaRulesByExtension =
        new Dictionary<string, ChatMediaValidationRule>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new(
                MediaKind.Image,
                "chat/images",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg" },
                HasJpegMagicBytes),
            [".jpeg"] = new(
                MediaKind.Image,
                "chat/images",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg" },
                HasJpegMagicBytes),
            [".png"] = new(
                MediaKind.Image,
                "chat/images",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png" },
                HasPngMagicBytes),
            [".webp"] = new(
                MediaKind.Image,
                "chat/images",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/webp" },
                HasWebpMagicBytes),
            [".mp3"] = new(
                MediaKind.Audio,
                "chat/audio",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/mpeg", "audio/mp3" },
                HasMp3MagicBytes),
            [".wav"] = new(
                MediaKind.Audio,
                "chat/audio",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/wav", "audio/x-wav" },
                HasWavMagicBytes),
            [".ogg"] = new(
                MediaKind.Audio,
                "chat/audio",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/ogg", "application/ogg" },
                HasOggMagicBytes),
            [".webm"] = new(
                MediaKind.Video,
                "chat/videos",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "video/webm", "audio/webm" },
                HasWebmMagicBytes),
            [".mp4"] = new(
                MediaKind.Video,
                "chat/videos",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "video/mp4" },
                HasMp4MagicBytes)
        };

    private readonly MediaStorageOptions _options;

    public MediaValidationService(IOptions<MediaStorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ValidatedMediaFile> ValidateAvatarAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            throw ApiException.BadRequest("avatar_file_missing", "Vui lòng chọn file ảnh đại diện.");

        if (file.Length > _options.MaxAvatarBytes)
            throw ApiException.BadRequest("avatar_file_too_large", $"Ảnh đại diện không được vượt quá {_options.MaxAvatarBytes / 1024 / 1024}MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AvatarContentTypesByExtension.TryGetValue(extension, out var expectedContentType))
            throw ApiException.BadRequest("avatar_extension_not_allowed", "Ảnh đại diện chỉ hỗ trợ .jpg, .jpeg, .png hoặc .webp.");

        var actualContentType = NormalizeContentType(file.ContentType);
        if (!string.Equals(actualContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest("avatar_mime_not_allowed", "MIME của ảnh đại diện không khớp định dạng được hỗ trợ.");

        await ValidateMagicBytesAsync(file, expectedContentType, cancellationToken);

        return new ValidatedMediaFile
        {
            OriginalFileName = NormalizeOriginalFileName(file.FileName, extension),
            ContentType = expectedContentType,
            Extension = extension,
            SizeBytes = file.Length,
            Kind = MediaKind.Image,
            StoragePrefix = "avatars"
        };
    }

    /// <inheritdoc />
    public async Task<ValidatedMediaFile> ValidateChatMediaAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            throw ApiException.BadRequest("chat_media_file_missing", "Vui lòng chọn file cần gửi.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ChatMediaRulesByExtension.TryGetValue(extension, out var rule))
            throw ApiException.BadRequest("chat_media_type_not_allowed", "Hiện chỉ hỗ trợ ảnh, audio và video đúng định dạng.");

        var actualContentType = NormalizeContentType(file.ContentType);
        if (!rule.ContentTypes.Contains(actualContentType))
            throw ApiException.BadRequest("chat_media_mime_not_allowed", "MIME của file không khớp định dạng được hỗ trợ.");

        var kind = rule.Kind;
        var storagePrefix = rule.StoragePrefix;
        if (string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actualContentType, "audio/webm", StringComparison.OrdinalIgnoreCase))
        {
            kind = MediaKind.Audio;
            storagePrefix = "chat/audio";
        }

        var maxBytes = GetMaxBytesForKind(kind);
        if (file.Length > maxBytes)
            throw ApiException.BadRequest("chat_media_file_too_large", $"File {GetKindDisplayName(kind)} không được vượt quá {maxBytes / 1024 / 1024}MB.");

        await ValidateMagicBytesAsync(file, rule.MagicBytesValidator, "chat_media_magic_bytes_invalid", cancellationToken);

        return new ValidatedMediaFile
        {
            OriginalFileName = NormalizeOriginalFileName(file.FileName, extension),
            ContentType = actualContentType,
            Extension = extension,
            SizeBytes = file.Length,
            Kind = kind,
            StoragePrefix = storagePrefix
        };
    }

    private static string NormalizeContentType(string? contentType)
    {
        return contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static string NormalizeOriginalFileName(string fileName, string extension)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            return $"avatar{extension}";

        if (safeName.Length <= 255)
            return safeName;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(safeName);
        var maxNameLength = Math.Max(1, 255 - extension.Length);
        return $"{nameWithoutExtension[..Math.Min(nameWithoutExtension.Length, maxNameLength)]}{extension}";
    }

    private static async Task ValidateMagicBytesAsync(
        IFormFile file,
        string expectedContentType,
        CancellationToken cancellationToken)
    {
        var isValid = expectedContentType switch
        {
            "image/jpeg" => await HasMagicBytesAsync(file, HasJpegMagicBytes, cancellationToken),
            "image/png" => await HasMagicBytesAsync(file, HasPngMagicBytes, cancellationToken),
            "image/webp" => await HasMagicBytesAsync(file, HasWebpMagicBytes, cancellationToken),
            _ => false
        };

        if (!isValid)
            throw ApiException.BadRequest("avatar_magic_bytes_invalid", "Nội dung file không khớp định dạng ảnh đã chọn.");
    }

    private async Task ValidateMagicBytesAsync(
        IFormFile file,
        Func<byte[], int, bool> validator,
        string errorCode,
        CancellationToken cancellationToken)
    {
        if (!await HasMagicBytesAsync(file, validator, cancellationToken))
            throw ApiException.BadRequest(errorCode, "Nội dung file không khớp định dạng đã chọn.");
    }

    private static async Task<bool> HasMagicBytesAsync(
        IFormFile file,
        Func<byte[], int, bool> validator,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[16];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        return validator(header, read);
    }

    private long GetMaxBytesForKind(MediaKind kind)
    {
        return kind switch
        {
            MediaKind.Image => _options.MaxImageBytes,
            MediaKind.Audio => _options.MaxAudioBytes,
            MediaKind.Video => _options.MaxVideoBytes,
            _ => _options.MaxFileBytes
        };
    }

    private static string GetKindDisplayName(MediaKind kind)
    {
        return kind switch
        {
            MediaKind.Image => "ảnh",
            MediaKind.Audio => "audio",
            MediaKind.Video => "video",
            _ => "file"
        };
    }

    private static bool HasJpegMagicBytes(byte[] header, int read)
    {
        return read >= 3 &&
               header[0] == 0xFF &&
               header[1] == 0xD8 &&
               header[2] == 0xFF;
    }

    private static bool HasPngMagicBytes(byte[] header, int read)
    {
        return read >= 8 &&
               header[0] == 0x89 &&
               header[1] == 0x50 &&
               header[2] == 0x4E &&
               header[3] == 0x47 &&
               header[4] == 0x0D &&
               header[5] == 0x0A &&
               header[6] == 0x1A &&
               header[7] == 0x0A;
    }

    private static bool HasWebpMagicBytes(byte[] header, int read)
    {
        return read >= 12 &&
               header[0] == 0x52 &&
               header[1] == 0x49 &&
               header[2] == 0x46 &&
               header[3] == 0x46 &&
               header[8] == 0x57 &&
               header[9] == 0x45 &&
               header[10] == 0x42 &&
               header[11] == 0x50;
    }

    private static bool HasMp3MagicBytes(byte[] header, int read)
    {
        return read >= 3 &&
               ((header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33) ||
                (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0));
    }

    private static bool HasWavMagicBytes(byte[] header, int read)
    {
        return read >= 12 &&
               header[0] == 0x52 &&
               header[1] == 0x49 &&
               header[2] == 0x46 &&
               header[3] == 0x46 &&
               header[8] == 0x57 &&
               header[9] == 0x41 &&
               header[10] == 0x56 &&
               header[11] == 0x45;
    }

    private static bool HasOggMagicBytes(byte[] header, int read)
    {
        return read >= 4 &&
               header[0] == 0x4F &&
               header[1] == 0x67 &&
               header[2] == 0x67 &&
               header[3] == 0x53;
    }

    private static bool HasWebmMagicBytes(byte[] header, int read)
    {
        return read >= 4 &&
               header[0] == 0x1A &&
               header[1] == 0x45 &&
               header[2] == 0xDF &&
               header[3] == 0xA3;
    }

    private static bool HasMp4MagicBytes(byte[] header, int read)
    {
        return read >= 12 &&
               header[4] == 0x66 &&
               header[5] == 0x74 &&
               header[6] == 0x79 &&
               header[7] == 0x70;
    }
}
