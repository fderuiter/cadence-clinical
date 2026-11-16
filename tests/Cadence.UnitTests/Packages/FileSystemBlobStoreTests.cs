using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Cadence.Storage;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The filesystem object store: atomic writes, a SHA-256 checked on every read, keys that cannot leave the storage
/// root, and no URLs.
/// </summary>
public sealed class FileSystemBlobStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("cadence-fs-store-");

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>An object written with its metadata reads back with the SHA-256 the write returned.</summary>
    [Fact]
    public async Task PutThenGet_RoundTripsTheBytesAndHash()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        byte[] data = Encoding.UTF8.GetBytes("signed consent form");
        string expected = Convert.ToHexStringLower(SHA256.HashData(data));

        string sha256 = await store.PutObjectAsync("studies/s1/consent.pdf", data, "application/pdf", new Dictionary<string, string> { ["site"] = "001" }, expected.ToUpperInvariant(), ct);
        var (read, readSha) = await store.GetObjectAsync("studies/s1/consent.pdf", ct);

        sha256.Should().Be(expected);
        read.Should().Equal(data);
        readSha.Should().Be(expected);
        (await store.ExistsAsync("studies/s1/consent.pdf", ct)).Should().BeTrue();
        File.Exists(Path.Combine(_root.FullName, "objects", "studies", "s1", "consent.pdf")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(_root.FullName, "meta", "studies", "s1", "consent.pdf.json"), ct))
            .Should().Contain($"\"sha256\":\"{expected}\"").And.Contain("\"contentType\":\"application/pdf\"").And.Contain("\"site\":\"001\"");
    }

    /// <summary>Writing a key again replaces the object and its hash, and leaves no temporary files behind.</summary>
    [Fact]
    public async Task Put_Overwrite_ReplacesTheObject_AndLeavesNoTemporaryFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        await store.PutObjectAsync("a/b.txt", [1, 2, 3], ct: ct);

        await store.PutObjectAsync("a/b.txt", [4, 5], ct: ct);

        (await store.GetObjectAsync("a/b.txt", ct)).Data.Should().Equal(4, 5);
        _root.EnumerateFiles("*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>A write whose bytes do not match the caller's hash is refused and stores nothing.</summary>
    [Fact]
    public async Task Put_WrongExpectedHash_IsRefusedAndStoresNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();

        var put = () => store.PutObjectAsync("x.bin", [1], expectedSha256: new string('0', 64), ct: ct);

        await put.Should().ThrowAsync<StorageIntegrityException>();
        (await store.ExistsAsync("x.bin", ct)).Should().BeFalse();
    }

    /// <summary>Bytes changed on disk after the write fail the read instead of being returned.</summary>
    [Fact]
    public async Task Get_TamperedObject_FailsTheIntegrityCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        await store.PutObjectAsync("record.json", Encoding.UTF8.GetBytes("{\"value\":120}"), ct: ct);
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "objects", "record.json"), "{\"value\":190}", ct);

        var get = () => store.GetObjectAsync("record.json", ct);

        await get.Should().ThrowAsync<StorageIntegrityException>().WithMessage("*was stored with*");
    }

    /// <summary>An object with no readable recorded hash cannot be verified, so it is not returned.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{\"contentType\":\"text/plain\"}")]
    public async Task Get_MissingOrUnreadableHash_FailsTheIntegrityCheck(string? metadata)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        await store.PutObjectAsync("note.txt", [7], ct: ct);
        string metadataPath = Path.Combine(_root.FullName, "meta", "note.txt.json");
        if (metadata is null)
        {
            File.Delete(metadataPath);
        }
        else
        {
            await File.WriteAllTextAsync(metadataPath, metadata, ct);
        }

        var get = () => store.GetObjectAsync("note.txt", ct);

        await get.Should().ThrowAsync<StorageIntegrityException>().WithMessage("*no readable recorded SHA256*");
    }

    /// <summary>Deleting removes the object and its record; reading or deleting a missing key is not-found.</summary>
    [Fact]
    public async Task Delete_RemovesTheObject_AndMissingKeysAreNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        await store.PutObjectAsync("gone.txt", [1], ct: ct);

        await store.DeleteObjectAsync("gone.txt", ct);

        (await store.ExistsAsync("gone.txt", ct)).Should().BeFalse();
        File.Exists(Path.Combine(_root.FullName, "meta", "gone.txt.json")).Should().BeFalse();
        await store.Invoking(s => s.GetObjectAsync("gone.txt", ct)).Should().ThrowAsync<StorageObjectNotFoundException>();
        await store.Invoking(s => s.DeleteObjectAsync("gone.txt", ct)).Should().ThrowAsync<StorageObjectNotFoundException>();
    }

    /// <summary>Keys that are empty, absolute, contain empty, dot or dot-dot segments, backslashes or control characters are refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("/etc/passwd")]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("a/./b")]
    [InlineData("a//b")]
    [InlineData("a/")]
    [InlineData("..\\outside.txt")]
    [InlineData("a\u0000b")]
    [InlineData("a\nb")]
    public async Task InvalidKeys_AreRefused_AndNothingIsWrittenOutsideTheRoot(string key)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();

        FileSystemBlobStore.IsValidKey(key).Should().BeFalse();
        await store.Invoking(s => s.PutObjectAsync(key, [1], ct: ct)).Should().ThrowAsync<ArgumentException>();
        await store.Invoking(s => s.GetObjectAsync(key, ct)).Should().ThrowAsync<ArgumentException>();
        await store.Invoking(s => s.ExistsAsync(key, ct)).Should().ThrowAsync<ArgumentException>();
        await store.Invoking(s => s.DeleteObjectAsync(key, ct)).Should().ThrowAsync<ArgumentException>();
        File.Exists(Path.Combine(_root.Parent!.FullName, "outside.txt")).Should().BeFalse();
    }

    /// <summary>A key longer than the limit is refused.</summary>
    [Fact]
    public void IsValidKey_RefusesOverlongKeys()
    {
        FileSystemBlobStore.IsValidKey(new string('a', FileSystemBlobStore.MaxKeyLength)).Should().BeTrue();
        FileSystemBlobStore.IsValidKey(new string('a', FileSystemBlobStore.MaxKeyLength + 1)).Should().BeFalse();
    }

    /// <summary>A key cannot be both an object and a folder of other objects.</summary>
    [Fact]
    public async Task Put_KeyThatIsAFolderOrUnderAnObject_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        await store.PutObjectAsync("folder/child.txt", [1], ct: ct);
        await store.PutObjectAsync("file.txt", [1], ct: ct);

        await store.Invoking(s => s.PutObjectAsync("folder", [2], ct: ct)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*folder of other objects*");
        await store.Invoking(s => s.PutObjectAsync("file.txt/child.txt", [2], ct: ct)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*already an object*");
        (await store.GetObjectAsync("file.txt", ct)).Data.Should().Equal(1);
    }

    /// <summary>A directory has no URLs, so presigned and multipart operations say which provider to use.</summary>
    [Fact]
    public async Task UrlsAndMultipart_AreNotSupported()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();

        var calls = new Func<Task>[]
        {
            () => store.GeneratePresignedGetUrlAsync("a", ct: ct),
            () => store.GeneratePresignedPutUrlAsync("a", ct: ct),
            () => store.CreateMultipartUploadAsync("a", ct: ct),
            () => store.GeneratePresignedMultipartUrlsAsync("a", "u", [1], ct: ct),
            () => store.CompleteMultipartUploadAsync("a", "u", [], ct),
            () => store.AbortMultipartUploadAsync("a", "u", ct),
        };

        foreach (var call in calls)
        {
            await call.Should().ThrowAsync<NotSupportedException>().WithMessage("*'s3' or 'minio'*");
        }
    }

    /// <summary>The store needs an absolute root.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("relative/storage")]
    public void Constructor_RequiresAnAbsoluteRoot(string root)
    {
        var create = () => new FileSystemBlobStore(new FileSystemStorageOptions { RootPath = root });

        create.Should().Throw<ArgumentException>().WithMessage("*absolute*");
    }

    /// <summary>The <c>filesystem</c> provider registers the store under an absolute root in any environment, and refuses a missing or relative one.</summary>
    [Fact]
    public void Registration_FileSystemProvider_NeedsAnAbsoluteRoot()
    {
        using var services = Register(new() { ["Storage:Provider"] = "filesystem", ["Storage:RootPath"] = _root.FullName });

        services.GetRequiredService<IStoragePort<byte[]>>().Should().BeOfType<FileSystemBlobStore>();
        foreach (string? root in new[] { null, "relative/storage" })
        {
            var register = () => Register(new() { ["Storage:Provider"] = "filesystem", ["Storage:RootPath"] = root });
            register.Should().Throw<InvalidOperationException>().WithMessage("*Storage:RootPath*absolute*");
        }
    }

    private FileSystemBlobStore Store() => new(new FileSystemStorageOptions { RootPath = _root.FullName });

    private static ServiceProvider Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddCadenceStorage(configuration, new ProductionEnvironment()).BuildServiceProvider();
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
