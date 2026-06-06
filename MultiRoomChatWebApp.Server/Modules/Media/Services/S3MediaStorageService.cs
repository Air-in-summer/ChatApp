using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <summary>
/// Luu tru media qua S3-compatible object storage, local/dev dung MinIO.
/// </summary>
public sealed class S3MediaStorageService : IMediaStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly MediaStorageOptions _options;
    private readonly ILogger<S3MediaStorageService> _logger;

    public S3MediaStorageService(
        IAmazonS3 s3Client,
        IOptions<MediaStorageOptions> options,
        ILogger<S3MediaStorageService> logger)
    {
        _s3Client = s3Client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnsureBucketsAsync(CancellationToken cancellationToken)
    {
        ValidateRequiredOptions();

        await EnsureBucketExistsAsync(_options.PublicBucket, cancellationToken);
        await EnsureBucketExistsAsync(_options.PrivateBucket, cancellationToken);
        await ApplyPublicReadPolicyAsync(_options.PublicBucket, cancellationToken);
        await EnsurePrivateBucketHasNoPublicPolicyAsync(_options.PrivateBucket, cancellationToken);

        _logger.LogInformation(
            "Media storage ready. PublicBucket={PublicBucket}; PrivateBucket={PrivateBucket}; LocalDataPath={LocalDataPath}",
            _options.PublicBucket,
            _options.PrivateBucket,
            _options.LocalDataPath);
    }

    /// <inheritdoc />
    public async Task<StoredMediaObject> PutObjectAsync(
        string bucketName,
        string storageKey,
        Stream content,
        string contentType,
        long? sizeBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
            throw new ArgumentException("Bucket name is required.", nameof(bucketName));

        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("Storage key is required.", nameof(storageKey));

        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type is required.", nameof(contentType));

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = storageKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };

        if (sizeBytes.HasValue)
        {
            request.Headers.ContentLength = sizeBytes.Value;
        }

        await _s3Client.PutObjectAsync(request, cancellationToken);

        return new StoredMediaObject
        {
            BucketName = bucketName,
            StorageKey = storageKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            PublicUrl = string.Equals(bucketName, _options.PublicBucket, StringComparison.Ordinal)
                ? BuildPublicObjectUrl(bucketName, storageKey)
                : null
        };
    }

    /// <inheritdoc />
    public async Task DeleteObjectAsync(string bucketName, string storageKey, CancellationToken cancellationToken)
    {
        await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = storageKey
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StoredMediaDownload> GetObjectAsync(
        string bucketName,
        string storageKey,
        CancellationToken cancellationToken)
    {
        var response = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucketName,
            Key = storageKey
        }, cancellationToken);

        return new StoredMediaDownload
        {
            Content = response.ResponseStream,
            ContentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType,
            SizeBytes = response.Headers.ContentLength >= 0 ? response.Headers.ContentLength : null
        };
    }

    /// <inheritdoc />
    public string CreatePresignedGetUrl(string bucketName, string storageKey, TimeSpan? ttl = null)
    {
        var expiresIn = ttl ?? TimeSpan.FromMinutes(Math.Max(1, _options.SignedUrlMinutes));
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = storageKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiresIn)
        };

        return _s3Client.GetPreSignedURL(request);
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken)
    {
        var response = await _s3Client.ListBucketsAsync(cancellationToken);
        var buckets = response.Buckets ?? [];
        if (buckets.Any(bucket => string.Equals(bucket.BucketName, bucketName, StringComparison.Ordinal)))
        {
            return;
        }

        await _s3Client.PutBucketAsync(new PutBucketRequest
        {
            BucketName = bucketName
        }, cancellationToken);

        _logger.LogInformation("Da tao media bucket {BucketName}.", bucketName);
    }

    private async Task ApplyPublicReadPolicyAsync(string bucketName, CancellationToken cancellationToken)
    {
        var policy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new Dictionary<string, string> { ["AWS"] = "*" },
                    Action = new[] { "s3:GetObject" },
                    Resource = new[] { $"arn:aws:s3:::{bucketName}/*" }
                }
            }
        });

        await _s3Client.PutBucketPolicyAsync(new PutBucketPolicyRequest
        {
            BucketName = bucketName,
            Policy = policy
        }, cancellationToken);
    }

    private async Task EnsurePrivateBucketHasNoPublicPolicyAsync(string bucketName, CancellationToken cancellationToken)
    {
        try
        {
            await _s3Client.DeleteBucketPolicyAsync(new DeleteBucketPolicyRequest
            {
                BucketName = bucketName
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Bucket private chua co policy la trang thai dung.
        }
    }

    private string BuildPublicObjectUrl(string bucketName, string storageKey)
    {
        var endpoint = string.IsNullOrWhiteSpace(_options.PublicEndpoint)
            ? _options.Endpoint
            : _options.PublicEndpoint;

        var escapedKey = string.Join(
            '/',
            storageKey.Split('/').Select(Uri.EscapeDataString));

        return $"{endpoint.TrimEnd('/')}/{bucketName}/{escapedKey}";
    }

    private void ValidateRequiredOptions()
    {
        if (!string.Equals(_options.Provider, "S3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MediaStorage:Provider phai la S3 trong phase M1.");

        if (string.IsNullOrWhiteSpace(_options.Endpoint))
            throw new InvalidOperationException("Thieu cau hinh MediaStorage:Endpoint.");

        if (string.IsNullOrWhiteSpace(_options.AccessKey))
            throw new InvalidOperationException("Thieu cau hinh MediaStorage:AccessKey.");

        if (string.IsNullOrWhiteSpace(_options.SecretKey))
            throw new InvalidOperationException("Thieu cau hinh MediaStorage:SecretKey.");

        if (string.IsNullOrWhiteSpace(_options.PublicBucket) || string.IsNullOrWhiteSpace(_options.PrivateBucket))
            throw new InvalidOperationException("Thieu cau hinh media bucket.");
    }
}
