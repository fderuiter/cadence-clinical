using System;
using System.Collections.Generic;

using Amazon.S3;

using Cadence.Storage;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Choosing the object store from configuration, ported from the Python reference factory
/// (<c>packages/storage/factory.py</c>). Unlike the reference, nothing is guessed: the provider is named, and a
/// missing setting fails at startup.
/// </summary>
public class StorageRegistrationTests
{
    [Theory]
    [InlineData("s3", null)]
    [InlineData("MinIO", "http://minio.test:9000")]
    public void S3AndMinio_RegisterTheS3Store(string provider, string? serviceUrl)
    {
        using var services = Register(new() { ["Storage:Provider"] = provider, ["Storage:BucketName"] = "b", ["Storage:ServiceUrl"] = serviceUrl }, Environments.Production);

        services.GetRequiredService<IStoragePort<byte[]>>().Should().BeOfType<S3BlobStore>();
    }

    [Fact]
    public void Memory_RegistersTheInMemoryStore_InDevelopment()
    {
        using var services = Register(new() { ["Storage:Provider"] = "memory" }, Environments.Development);

        services.GetRequiredService<IStoragePort<byte[]>>().Should().BeOfType<InMemoryBlobStore>();
    }

    [Theory]
    [InlineData("memory", "b", null, null, null, "Development")]
    [InlineData(null, "b", null, null, null, "")]
    [InlineData("gdrive", "b", null, null, null, "")]
    [InlineData("s3", null, null, null, null, "")]
    [InlineData("s3", "b", null, "key-only", null, "")]
    [InlineData("s3", "b", null, null, "secret-only", "")]
    [InlineData("minio", "b", null, null, null, "ServiceUrl")]
    public void Registration_FailsOnAMissingOrUnsafeSetting(string? provider, string? bucket, string? url, string? key, string? secret, string mentions)
    {
        var register = () => Register(
            new()
            {
                ["Storage:Provider"] = provider,
                ["Storage:BucketName"] = bucket,
                ["Storage:ServiceUrl"] = url,
                ["Storage:AccessKeyId"] = key,
                ["Storage:SecretAccessKey"] = secret,
            },
            Environments.Production);

        register.Should().Throw<InvalidOperationException>().WithMessage($"*{mentions}*");
    }

    [Fact]
    public void CreateClient_UsesPathStyleForACustomEndpoint_AndTheRegionEndpointOtherwise()
    {
        using var minio = StorageServiceCollectionExtensions.CreateClient(new S3StorageOptions { BucketName = "b", ServiceUrl = "http://minio.test:9000" });
        using var aws = StorageServiceCollectionExtensions.CreateClient(new S3StorageOptions { BucketName = "b", Region = "eu-west-1" });

        ((AmazonS3Config)minio.Config).ForcePathStyle.Should().BeTrue();
        aws.Config.RegionEndpoint.SystemName.Should().Be("eu-west-1");
        ((AmazonS3Config)aws.Config).ForcePathStyle.Should().BeFalse();
    }

    private static ServiceProvider Register(Dictionary<string, string?> settings, string environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddCadenceStorage(configuration, new Environment(environment));
        return services.BuildServiceProvider();
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = "";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
