using System;

using Amazon;
using Amazon.Runtime;
using Amazon.S3;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cadence.Storage;

/// <summary>Settings for <see cref="S3BlobStore"/>, bound from the <c>Storage</c> configuration section.</summary>
public sealed record S3StorageOptions
{
    /// <summary>The bucket every object goes in. Required.</summary>
    public string BucketName { get; init; } = "";

    /// <summary>The AWS region (default <c>us-east-1</c>, which MinIO also accepts).</summary>
    public string Region { get; init; } = "us-east-1";

    /// <summary>The endpoint of an S3-compatible server such as MinIO; empty for Amazon S3.</summary>
    public string? ServiceUrl { get; init; }

    /// <summary>
    /// Path-style addressing (<c>host/bucket/key</c>). MinIO needs it; it defaults to on whenever
    /// <see cref="ServiceUrl"/> is set.
    /// </summary>
    public bool? ForcePathStyle { get; init; }

    /// <summary>Server-side encryption for writes (default <c>AES256</c>); empty turns it off.</summary>
    public string? ServerSideEncryption { get; init; } = "AES256";

    /// <summary>
    /// An access key. Leave it and <see cref="SecretAccessKey"/> empty to use the AWS default credential chain
    /// (environment, profile, instance or task role). There is no default key.
    /// </summary>
    public string? AccessKeyId { get; init; }

    /// <summary>The secret for <see cref="AccessKeyId"/>.</summary>
    public string? SecretAccessKey { get; init; }
}

/// <summary>Registers the object store a service uses (the C# port of the Python storage factory).</summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>The configuration section the store reads (<c>Storage__Provider</c>, <c>Storage__BucketName</c>, ...).</summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Registers <see cref="IStoragePort{T}"/> for <c>byte[]</c> from <c>Storage:Provider</c>: <c>s3</c> or
    /// <c>minio</c> (an <see cref="S3BlobStore"/>), or <c>memory</c> (an <see cref="InMemoryBlobStore"/>, Development
    /// only). Fails at registration when the provider is missing or unknown, the bucket is missing, only half of an
    /// access key pair is set, or the in-memory store is asked for outside Development: a service never falls back to
    /// a store it was not configured for.
    /// </summary>
    public static IServiceCollection AddCadenceStorage(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection(SectionName);
        string provider = (section["Provider"] ?? "").Trim().ToLowerInvariant();
        switch (provider)
        {
            case "memory":
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException("Storage:Provider 'memory' keeps objects in process memory and is allowed only in Development.");
                }

                services.TryAddSingleton<IStoragePort<byte[]>, InMemoryBlobStore>();
                return services;
            case "s3":
            case "minio":
                var options = section.Get<S3StorageOptions>() ?? new S3StorageOptions();
                Validate(options, provider);
                services.TryAddSingleton(options);
                services.TryAddSingleton<IAmazonS3>(_ => CreateClient(options));
                services.TryAddSingleton<IStoragePort<byte[]>>(sp =>
                    new S3BlobStore(sp.GetRequiredService<IAmazonS3>(), options));
                return services;
            default:
                throw new InvalidOperationException(
                    provider.Length == 0
                        ? "Storage:Provider is not set; use 's3', 'minio' or (Development only) 'memory'."
                        : $"Storage:Provider '{provider}' is unknown; use 's3', 'minio' or (Development only) 'memory'.");
        }
    }

    /// <summary>Builds the S3 client for the options, with explicit credentials or the AWS default chain.</summary>
    public static AmazonS3Client CreateClient(S3StorageOptions options)
    {
        var config = CreateConfig(options);
        return string.IsNullOrEmpty(options.AccessKeyId)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), config);
    }

    /// <summary>
    /// The client settings for the options: the region endpoint for Amazon S3, or the custom endpoint with path-style
    /// addressing for MinIO; Signature Version 4 always, since the SDK otherwise presigns URLs with the deprecated
    /// Signature Version 2 in <c>us-east-1</c> and on custom endpoints.
    /// </summary>
    public static AmazonS3Config CreateConfig(S3StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The SDK reads this process-wide switch, not the client setting, when it presigns a URL.
        AWSConfigsS3.UseSignatureVersion4 = true;
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle ?? !string.IsNullOrWhiteSpace(options.ServiceUrl),
            SignatureVersion = "4",
        };
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
        }

        return config;
    }

    private static void Validate(S3StorageOptions options, string provider)
    {
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            throw new InvalidOperationException("Storage:BucketName is required.");
        }

        if (string.IsNullOrEmpty(options.AccessKeyId) != string.IsNullOrEmpty(options.SecretAccessKey))
        {
            throw new InvalidOperationException("Storage:AccessKeyId and Storage:SecretAccessKey are set together or not at all.");
        }

        if (provider == "minio" && string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            throw new InvalidOperationException("Storage:Provider 'minio' needs Storage:ServiceUrl.");
        }
    }
}
