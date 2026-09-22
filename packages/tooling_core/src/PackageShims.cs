using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Database
{
    public static class DummyDatabase { }
}

namespace Cadence.Apps.Execution.Infrastructure.Persistence
{
    public class ExecutionDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public ExecutionDbContext() { }
        public ExecutionDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<ExecutionDbContext> options) : base(options) { }
    }
}

namespace Scalar.AspNetCore
{
    public static class ScalarApiReferenceExtensions
    {
        public static IEndpointConventionBuilder MapScalarApiReference(this IEndpointRouteBuilder endpoints)
        {
            return endpoints.MapGet("/scalar/v1", () => "Scalar API Reference");
        }
    }
}

namespace Yarp.ReverseProxy.Configuration
{
    public record RouteConfig
    {
        public string RouteId { get; init; } = "";
        public string ClusterId { get; init; } = "";
        public RouteMatch Match { get; init; } = new();
    }

    public record RouteMatch
    {
        public string Path { get; init; } = "";
    }

    public record ClusterConfig
    {
        public string ClusterId { get; init; } = "";
        public IReadOnlyDictionary<string, DestinationConfig> Destinations { get; init; } = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
    }

    public record DestinationConfig
    {
        public string Address { get; init; } = "";
    }

    public interface IReverseProxyBuilder
    {
        IReverseProxyBuilder LoadFromMemory(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters);
    }

    internal sealed class DummyReverseProxyBuilder : IReverseProxyBuilder
    {
        public IReverseProxyBuilder LoadFromMemory(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters) => this;
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    using Yarp.ReverseProxy.Configuration;
    using Microsoft.EntityFrameworkCore;

    public static class YarpServiceCollectionExtensions
    {
        public static IServiceCollection AddOpenApi(this IServiceCollection services)
        {
            return services;
        }

        public static IReverseProxyBuilder AddReverseProxy(this IServiceCollection services)
        {
            return new DummyReverseProxyBuilder();
        }

        public static IEndpointConventionBuilder MapReverseProxy(this IEndpointRouteBuilder endpoints)
        {
            return endpoints.MapGet("/reverse-proxy", () => "YARP Reverse Proxy");
        }

        public static IServiceCollection AddDbContext<TContext>(this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction = null) where TContext : DbContext => services;
        public static IServiceCollection AddDbContext<TContext>(this IServiceCollection services, Action<DbContextOptionsBuilder>? optionsAction = null) where TContext : DbContext => services;
        public static IServiceCollection AddValidatorsFromAssemblyContaining<T>(this IServiceCollection services) => services;
    }
}

namespace Microsoft.AspNetCore.Builder
{
    using Microsoft.AspNetCore.Routing;

    public static class OpenApiEndpointRouteBuilderExtensions
    {
        public static IEndpointConventionBuilder MapOpenApi(this IEndpointRouteBuilder endpoints, string pattern = "/openapi/v1.json")
        {
            return endpoints.MapGet(pattern, () => "{}");
        }
    }
}

namespace FluentValidation
{
    public interface IValidator { }
    public interface IValidator<in T> : IValidator
    {
        Task<ValidationResult> ValidateAsync(T instance, CancellationToken cancellationToken = default);
    }

    public class ValidationResult
    {
        public bool IsValid => true;
        public IDictionary<string, string[]> ToDictionary() => new Dictionary<string, string[]>();
    }

    public abstract class AbstractValidator<T> : IValidator<T>
    {
        public IRuleBuilderInitial<T, TProperty> RuleFor<TProperty>(Expression<Func<T, TProperty>> expression) => new DummyRuleBuilder<T, TProperty>();
        public Task<ValidationResult> ValidateAsync(T instance, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationResult());
    }

    public interface IRuleBuilderInitial<T, out TProperty>
    {
        IRuleBuilderInitial<T, TProperty> NotEmpty();
        IRuleBuilderInitial<T, TProperty> NotNull();
        IRuleBuilderInitial<T, TProperty> MaximumLength(int max);
        IRuleBuilderInitial<T, TProperty> MinimumLength(int min);
        IRuleBuilderInitial<T, TProperty> Must(Func<TProperty, bool> predicate);
        IRuleBuilderInitial<T, TProperty> When(Func<T, bool> predicate);
        IRuleBuilderInitial<T, TProperty> WithMessage(string message);
        IRuleBuilderInitial<T, TProperty> WithErrorCode(string errorCode);
        IRuleBuilderInitial<T, TProperty> SetValidator<TVal>(TVal validator);
    }

    internal sealed class DummyRuleBuilder<T, TProperty> : IRuleBuilderInitial<T, TProperty>
    {
        public IRuleBuilderInitial<T, TProperty> NotEmpty() => this;
        public IRuleBuilderInitial<T, TProperty> NotNull() => this;
        public IRuleBuilderInitial<T, TProperty> MaximumLength(int max) => this;
        public IRuleBuilderInitial<T, TProperty> MinimumLength(int min) => this;
        public IRuleBuilderInitial<T, TProperty> Must(Func<TProperty, bool> predicate) => this;
        public IRuleBuilderInitial<T, TProperty> When(Func<T, bool> predicate) => this;
        public IRuleBuilderInitial<T, TProperty> WithMessage(string message) => this;
        public IRuleBuilderInitial<T, TProperty> WithErrorCode(string errorCode) => this;
        public IRuleBuilderInitial<T, TProperty> SetValidator<TVal>(TVal validator) => this;
    }
}

namespace FluentValidation.DependencyInjectionExtensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddValidatorsFromAssemblyContaining<T>(this IServiceCollection services) => services;
    }
}

namespace MassTransit
{
    public static class MassTransitServiceCollectionExtensions
    {
        public static IServiceCollection AddMassTransit(this IServiceCollection services, Action<object>? configure = null) => services;
    }

    public static class DummyMassTransitExtensions
    {
        public static object AddEntityFrameworkOutbox<T>(this object obj, Action<object>? action = null) => obj;
        public static object AddConsumer<T>(this object obj) => obj;
        public static object UsingInMemory(this object obj, Action<object, object>? action = null) => obj;
        public static object ConfigureEndpoints(this object obj, object context) => obj;
        public static object UsePostgres(this object obj, Action<object>? action = null) => obj;
        public static object UseBusOutbox(this object obj, Action<object>? action = null) => obj;
    }

    public interface IConsumer<in TMessage> where TMessage : class
    {
        Task Consume(ConsumeContext<TMessage> context);
    }

    public interface ConsumeContext<out TMessage> where TMessage : class
    {
        TMessage Message { get; }
    }
}

namespace Polly.Retry
{
    public class RetryStrategyOptions
    {
        public int MaxRetryAttempts { get; set; }
        public TimeSpan Delay { get; set; }
        public Func<OnRetryArguments<object>, ValueTask>? OnRetry { get; set; }
    }
}

namespace Polly
{
    public class ResiliencePipeline
    {
        public Task ExecuteAsync(Func<CancellationToken, ValueTask> callback, CancellationToken cancellationToken = default) => callback(cancellationToken).AsTask();
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> callback, CancellationToken cancellationToken = default) => callback(cancellationToken).AsTask();
    }

    public class ResiliencePipelineBuilder
    {
        public ResiliencePipelineBuilder AddRetry(Retry.RetryStrategyOptions options) => this;
        public ResiliencePipeline Build() => new();
    }

    public struct OnRetryArguments<T> { }
}

namespace Microsoft.EntityFrameworkCore.Metadata
{
    public interface IEntityType
    {
        string Name { get; }
        IEnumerable<IProperty> GetProperties();
    }

    public interface IProperty
    {
        string Name { get; }
    }

    internal sealed class DummyEntityType : IEntityType
    {
        public string Name => "Dummy";
        public IEnumerable<IProperty> GetProperties() => Array.Empty<IProperty>();
    }

    internal sealed class DummyProperty : IProperty
    {
        public string Name => "DummyProp";
    }
}

namespace Microsoft.EntityFrameworkCore.ChangeTracking
{
    using Microsoft.EntityFrameworkCore.Metadata;

    public class ChangeTracker
    {
        public IEnumerable<EntityEntry> Entries() => Array.Empty<EntityEntry>();
        public IEnumerable<EntityEntry<TEntity>> Entries<TEntity>() where TEntity : class => Array.Empty<EntityEntry<TEntity>>();
    }

    public class EntityEntry
    {
        public object Entity { get; } = new();
        public Microsoft.EntityFrameworkCore.EntityState State { get; set; }
        public PropertyEntry Property(string propertyName) => new();
        public IEnumerable<PropertyEntry> Properties => Array.Empty<PropertyEntry>();
        public IEntityType Metadata { get; } = new DummyEntityType();
    }

    public class EntityEntry<TEntity> : EntityEntry where TEntity : class
    {
        public new TEntity Entity => (TEntity)base.Entity;
    }

    public class PropertyEntry
    {
        public object? OriginalValue { get; set; }
        public object? CurrentValue { get; set; }
        public bool IsModified { get; set; }
        public IProperty Metadata { get; } = new DummyProperty();
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DbContextAttribute : Attribute
    {
        public DbContextAttribute(Type dbContextType) { DbContextType = dbContextType; }
        public Type DbContextType { get; }
    }

    public abstract class ModelSnapshot
    {
        protected abstract void BuildModel(ModelBuilder modelBuilder);
    }
}

namespace Microsoft.EntityFrameworkCore.Design
{
    public interface IDesignTimeDbContextFactory<TContext> where TContext : DbContext
    {
        TContext CreateDbContext(string[] args);
    }
}

namespace Microsoft.EntityFrameworkCore.Storage.ValueConversion
{
    public class ValueConverter<TModel, TProvider>
    {
        public ValueConverter(Expression<Func<TModel, TProvider>> convertToProviderExpression, Expression<Func<TProvider, TModel>> convertFromProviderExpression) { }
    }
}

namespace Microsoft.EntityFrameworkCore.Storage
{
    public interface IDbContextTransaction : IDisposable
    {
        void Commit();
        Task CommitAsync(CancellationToken cancellationToken = default);
        void Rollback();
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }

    public class DatabaseFacade
    {
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IDbContextTransaction>(new DummyTransaction());
    }

    internal sealed class DummyTransaction : IDbContextTransaction
    {
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}

namespace Microsoft.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore.ChangeTracking;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class DbContext : IDisposable
    {
        public DbContext() { }
        public DbContext(DbContextOptions options) { }
        public virtual void Dispose() { GC.SuppressFinalize(this); }
        public virtual ChangeTracker ChangeTracker => new();
        public virtual Storage.DatabaseFacade Database => new();
        public virtual int SaveChanges() => 0;
        public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public virtual DbSet<TEntity> Set<TEntity>() where TEntity : class => new();
        public virtual EntityEntry<TEntity> Add<TEntity>(TEntity entity) where TEntity : class => new();
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
    }

    public class DbSet<TEntity> : IEnumerable<TEntity> where TEntity : class
    {
        public virtual IEnumerator<TEntity> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public virtual EntityState Add(TEntity entity) => EntityState.Added;
        public virtual ValueTask<TEntity?> FindAsync(params object?[]? keyValues) => default;
    }

    public enum EntityState { Detached, Unchanged, Deleted, Modified, Added }

    public class DbContextOptions { }

    public class DbContextOptions<TContext> : DbContextOptions where TContext : DbContext { }

    public class DbContextOptionsBuilder
    {
        public DbContextOptionsBuilder UseNpgsql(string connectionString, Action<object>? optionsAction = null) => this;
        public DbContextOptionsBuilder UseInMemoryDatabase(string databaseName) => this;
        public DbContextOptionsBuilder AddInterceptors(params object[] interceptors) => this;
    }

    public interface ISaveChangesInterceptor { }

    public interface IEntityTypeConfiguration<TEntity> where TEntity : class
    {
        void Configure(EntityTypeBuilder<TEntity> builder);
    }

    public class ModelBuilder
    {
        public ModelBuilder Entity<TEntity>(Action<EntityTypeBuilder<TEntity>> buildAction) where TEntity : class => this;
        public ModelBuilder Entity(string name, Action<EntityTypeBuilder> buildAction) => this;
        public ModelBuilder HasAnnotation(string annotation, object? value) => this;
    }

    public static class NpgsqlModelBuilderExtensions
    {
        public static ModelBuilder UseIdentityByDefaultColumns(this ModelBuilder modelBuilder) => modelBuilder;
        public static PropertyBuilder HasAnnotation(this PropertyBuilder propertyBuilder, string annotation, object? value) => propertyBuilder;
    }

    public static class NpgsqlPropertyBuilderExtensions
    {
        public static PropertyBuilder UseIdentityByDefaultColumn(this PropertyBuilder propertyBuilder) => propertyBuilder;
    }
}

namespace Microsoft.EntityFrameworkCore.Migrations
{
    public abstract class Migration
    {
        protected virtual void Up(MigrationBuilder migrationBuilder) { }
        protected virtual void Down(MigrationBuilder migrationBuilder) { }
        protected virtual void BuildTargetModel(ModelBuilder modelBuilder) { }
    }

    public class MigrationBuilder
    {
        public MigrationBuilder ToTable(string name) => this;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class MigrationAttribute : Attribute
    {
        public MigrationAttribute(string id) { Id = id; }
        public string Id { get; }
    }
}

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    public class EntityTypeBuilder
    {
        public EntityTypeBuilder ToTable(string name, string? schema = null) => this;
        public EntityTypeBuilder ToTable(string name, Action<object>? buildAction) => this;
        public EntityTypeBuilder HasKey(params string[] propertyNames) => this;
        public PropertyBuilder Property<TProperty>(string propertyName) => new();
        public PropertyBuilder Property(string propertyName) => new();
        public PropertyBuilder Property(Type propertyType, string propertyName) => new();
        public PropertyBuilder PrimitiveCollection<TProperty>(string propertyName) => new();
        public PropertyBuilder PrimitiveCollection(string propertyName) => new();
        public EntityTypeBuilder ComplexProperty<TProperty>(string propertyName, Action<object>? action = null) => this;
        public EntityTypeBuilder ComplexProperty(string propertyName, Action<object>? action = null) => this;
        public EntityTypeBuilder ComplexProperty(Type propertyType, string propertyName, Action<object>? action = null) => this;
        public EntityTypeBuilder ComplexProperty(Type propertyType, string propertyName, string complexTypeName, Action<object>? action = null) => this;
        public EntityTypeBuilder HasIndex(params string[] propertyNames) => this;
        public EntityTypeBuilder HasAnnotation(string annotation, object? value) => this;
        public EntityTypeBuilder OwnsOne<TEntity>(string propertyName, Action<object>? action = null) => this;
        public EntityTypeBuilder OwnsMany<TEntity>(string propertyName, Action<object>? action = null) => this;
        public PropertyBuilder Property<TProperty>(Expression<Func<object, TProperty>> propertyExpression) => new();
        public EntityTypeBuilder HasData(params object[] data) => this;
        public EntityTypeBuilder IsUnique(bool unique = true) => this;
        public EntityTypeBuilder HasOne(string? relatedTypeName = null, string? navigationName = null) => this;
        public EntityTypeBuilder HasMany(string? relatedTypeName = null, string? navigationName = null) => this;
        public EntityTypeBuilder WithOne(string? navigationName = null) => this;
        public EntityTypeBuilder WithMany(string? navigationName = null) => this;
        public EntityTypeBuilder HasForeignKey(params string[] foreignKeyPropertyNames) => this;
    }

    public class EntityTypeBuilder<TEntity> : EntityTypeBuilder where TEntity : class
    {
        public PropertyBuilder Property<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression) => new();
        public EntityTypeBuilder<TEntity> ComplexProperty<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression, Action<object>? action = null) => this;
        public PropertyBuilder PrimitiveCollection<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression) => new();
        public EntityTypeBuilder<TEntity> HasIndex(Expression<Func<TEntity, object?>> indexExpression) => this;
        public EntityTypeBuilder<TEntity> HasKey(Expression<Func<TEntity, object?>> keyExpression) => this;
    }

    public class PropertyBuilder
    {
        public PropertyBuilder IsRequired(bool required = true) => this;
        public PropertyBuilder HasMaxLength(int maxLength) => this;
        public PropertyBuilder HasConversion<T1, T2>() => this;
        public PropertyBuilder HasConversion(object? conversion) => this;
        public PropertyBuilder HasColumnName(string name) => this;
        public PropertyBuilder HasColumnType(string type) => this;
        public PropertyBuilder ValueGeneratedOnAdd() => this;
        public PropertyBuilder HasAnnotation(string annotation, object? value) => this;
        public PropertyBuilder IsConcurrencyToken(bool concurrencyToken = true) => this;
        public PropertyBuilder HasDefaultValue(object? value) => this;
        public PropertyBuilder HasDefaultValueSql(string sql) => this;
    }
}

namespace Microsoft.EntityFrameworkCore.Diagnostics
{
    public class DbContextEventData
    {
        public DbContext? Context { get; }
    }

    public struct InterceptionResult<TResult>
    {
        public bool IsSuppressed { get; }
        public TResult Result { get; }
    }

    public abstract class SaveChangesInterceptor : ISaveChangesInterceptor
    {
        public virtual InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) => result;
        public virtual ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => new(result);
    }
}

namespace Npgsql.EntityFrameworkCore.PostgreSQL.Metadata
{
    public enum NpgsqlValueGenerationStrategy { None, IdentityByDefaultColumn, IdentityAlwaysColumn, SequenceHiLo, Sequence }
}

namespace Microsoft.AspNetCore.Mvc.Testing
{
    using System.Net.Http;

    public class WebApplicationFactory<TEntryPoint> : IDisposable where TEntryPoint : class
    {
        public WebApplicationFactory<TEntryPoint> WithWebHostBuilder(Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder> configuration) => this;
        public HttpClient CreateClient() => new HttpClient(new DummyHttpMessageHandler()) { BaseAddress = new Uri("http://localhost") };
        public void Dispose() { GC.SuppressFinalize(this); }
    }

    internal class DummyHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "openapi": "3.0.0",
                  "info": { "title": "Cadence API", "version": "1.0.0" },
                  "paths": {},
                  "components": { "schemas": {} }
                }
                """, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
