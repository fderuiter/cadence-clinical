using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// Implemented by a <see cref="DbContext"/> whose model maps encrypted columns through
/// <see cref="EncryptedStringConverter"/>. The converters capture the context's encryptor when the model is built.
/// @req:PRD-SYS-053
/// </summary>
public interface IFieldEncryptingDbContext
{
    /// <summary>The encryptor the context's model was built with.</summary>
    IFieldEncryptor FieldEncryptor { get; }
}

/// <summary>
/// EF Core caches one model per context type. Because encrypted-column converters capture an encryptor, the cache
/// key also includes the encryptor's key id: two contexts configured with different keys (for example, in tests) get
/// separate models instead of silently sharing the first key. Register it in <c>OnConfiguring</c> with
/// <c>optionsBuilder.ReplaceService&lt;IModelCacheKeyFactory, FieldEncryptionModelCacheKeyFactory&gt;()</c>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class FieldEncryptionModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime) =>
        context is IFieldEncryptingDbContext encrypting
            ? (context.GetType(), encrypting.FieldEncryptor.GetType(), encrypting.FieldEncryptor.KeyId, designTime)
            : (context.GetType(), designTime);
}
