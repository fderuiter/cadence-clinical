#pragma warning disable CS0436
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
        public static DummyOpenTelemetryBuilder AddOpenTelemetry(this IServiceCollection services) => new DummyOpenTelemetryBuilder();
    }

    public class DummyOpenTelemetryBuilder
    {
        public DummyOpenTelemetryBuilder WithTracing(Action<OpenTelemetry.Trace.TracerProviderBuilder> configure) => this;
        public DummyOpenTelemetryBuilder WithMetrics(Action<OpenTelemetry.Metrics.MeterProviderBuilder> configure) => this;
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

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class FactAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TheoryAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class InlineDataAttribute : Attribute
    {
        public InlineDataAttribute(params object?[] data) { }
    }
}

namespace FluentAssertions
{
    public static class AssertionExtensions
    {
        public static ObjectAssertions Should(this object? actual) => new(actual);
        public static BooleanAssertions Should(this bool actual) => new(actual);
        public static StringAssertions Should(this string? actual) => new(actual);
    }

    public class ObjectAssertions
    {
        public ObjectAssertions(object? value) { Value = value; }
        public object? Value { get; }
        public void BeNull() { }
        public void NotBeNull() { }
        public void Be(object? expected) { }
        public void BeEquivalentTo(object? expected, object? options = null) { }
    }

    public class BooleanAssertions
    {
        public BooleanAssertions(bool value) { Value = value; }
        public bool Value { get; }
        public void BeTrue() { }
        public void BeFalse() { }
    }

    public class StringAssertions
    {
        public StringAssertions(string? value) { Value = value; }
        public string? Value { get; }
        public void Contain(string expected) { }
        public void Be(string? expected) { }
        public void NotBeNullOrEmpty() { }
    }
}

namespace Spectre.Console
{
    public interface IPrompt<T> { }

    public class FigletText
    {
        public FigletText(string text) { }
        public FigletText Color(Color color) => this;
    }

    public class Color
    {
        public static Color Blue => new();
        public static Color Green => new();
        public static Color Red => new();
        public static Color Yellow => new();
        public static Color Cyan1 => new();
    }

    public class SelectionPrompt<T> : IPrompt<T>
    {
        public SelectionPrompt<T> Title(string title) => this;
        public SelectionPrompt<T> AddChoices(params T[] choices) => this;
        public SelectionPrompt<T> AddChoices(IEnumerable<T> choices) => this;
    }

    public static class AnsiConsole
    {
        public static void MarkupLine(string message) { }
        public static void Write(object obj) { }
        public static T Prompt<T>(IPrompt<T> prompt) => default!;
    }
}

namespace Spectre.Console.Cli
{
    public class CommandApp
    {
        public void Configure(Action<object> config) { }
        public int Run(string[] args) => 0;
    }
}

namespace OpenTelemetry.Trace
{
    public class TracerProviderBuilder
    {
        public TracerProviderBuilder AddAspNetCoreInstrumentation() => this;
        public TracerProviderBuilder AddHttpClientInstrumentation() => this;
    }
}

namespace OpenTelemetry.Metrics
{
    public class MeterProviderBuilder
    {
        public MeterProviderBuilder AddAspNetCoreInstrumentation() => this;
        public MeterProviderBuilder AddHttpClientInstrumentation() => this;
    }
}

namespace Microsoft.EntityFrameworkCore.Metadata
{
    public interface IKey
    {
        IReadOnlyList<IProperty> Properties => Array.Empty<IProperty>();
    }

    public interface IEntityType
    {
        string Name { get; }
        Type ClrType => typeof(object);
        IKey? FindPrimaryKey() => null;
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
    using System.Linq;
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

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;

        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default)
            => Task.FromResult(source.ToList());

        public static Task<TSource?> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default)
            => Task.FromResult(source.FirstOrDefault());

        public static Task<TSource?> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(source.FirstOrDefault(predicate.Compile()));
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public virtual IEnumerator<TEntity> GetEnumerator() => ((IEnumerable<TEntity>)Array.Empty<TEntity>()).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public virtual EntityState Add(TEntity entity) => EntityState.Added;
        public virtual ValueTask<TEntity?> FindAsync(params object?[]? keyValues) => default;
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this, typeof(IQueryable<TEntity>));
        public IQueryProvider Provider => new DummyQueryProvider<TEntity>();
    }

    internal class DummyQueryProvider<TEntity> : IQueryProvider where TEntity : class
    {
        public IQueryable CreateQuery(Expression expression) => new DbSet<TEntity>();
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => (IQueryable<TElement>)(object)new DbSet<TEntity>();
        public object? Execute(Expression expression) => null;
        public TResult Execute<TResult>(Expression expression) => default!;
    }

    public enum EntityState { Detached, Unchanged, Deleted, Modified, Added }
    public enum DeleteBehavior { Cascade, Restrict, SetNull, ClientSetNull, ClientCascade, NoAction, ClientNoAction }

    public static class ModelBuilderExtensions
    {
        public static ModelBuilder ApplyConfiguration<TEntity>(this ModelBuilder modelBuilder, IEntityTypeConfiguration<TEntity> configuration) where TEntity : class => modelBuilder;
    }

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
        public ReferenceNavigationBuilder HasOne(string? relatedTypeName = null, string? navigationName = null) => new();
        public ReferenceNavigationBuilder HasOne<TRelated>(string? navigationName = null) => new();
        public ReferenceNavigationBuilder HasOne<TRelated>(Expression<Func<object, TRelated?>>? navigationExpression = null) => new();
        public ReferenceNavigationBuilder HasMany(string? relatedTypeName = null, string? navigationName = null) => new();
        public EntityTypeBuilder WithOne(string? navigationName = null) => this;
        public EntityTypeBuilder WithMany(string? navigationName = null) => this;
        public EntityTypeBuilder HasForeignKey(params string[] foreignKeyPropertyNames) => this;
        public EntityTypeBuilder OnDelete(DeleteBehavior deleteBehavior) => this;
    }

    public class ReferenceNavigationBuilder
    {
        public ReferenceNavigationBuilder WithMany() => this;
        public ReferenceNavigationBuilder WithMany(string navigationName) => this;
        public ReferenceNavigationBuilder WithMany(object navigationExpression) => this;
        public ReferenceNavigationBuilder WithOne() => this;
        public ReferenceNavigationBuilder WithOne(string navigationName) => this;
        public ReferenceNavigationBuilder WithOne(object navigationExpression) => this;
        public ReferenceNavigationBuilder HasForeignKey(params string[] foreignKeyPropertyNames) => this;
        public ReferenceNavigationBuilder HasForeignKey(object foreignKeyExpression) => this;
        public ReferenceNavigationBuilder HasPrincipalKey(params string[] keyPropertyNames) => this;
        public ReferenceNavigationBuilder HasPrincipalKey(object keyExpression) => this;
        public ReferenceNavigationBuilder HasConstraintName(string name) => this;
        public ReferenceNavigationBuilder OnDelete(DeleteBehavior deleteBehavior) => this;
        public ReferenceNavigationBuilder IsRequired(bool required = true) => this;
    }

    public class ReferenceNavigationBuilder<TEntity, TRelated> : ReferenceNavigationBuilder where TEntity : class where TRelated : class
    {
        public ReferenceNavigationBuilder<TEntity, TRelated> WithMany(Expression<Func<TRelated, IEnumerable<TEntity>?>>? navigationExpression = null) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> WithMany(Expression<Func<TRelated, object?>>? navigationExpression) => this;
        public new ReferenceNavigationBuilder<TEntity, TRelated> WithMany(string navigationName) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> WithOne(Expression<Func<TRelated, TEntity?>>? navigationExpression = null) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> WithOne(Expression<Func<TRelated, object?>>? navigationExpression) => this;
        public new ReferenceNavigationBuilder<TEntity, TRelated> WithOne(string navigationName) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> HasForeignKey(Expression<Func<TEntity, object?>> foreignKeyExpression) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> HasForeignKey(Expression<Func<TRelated, object?>> foreignKeyExpression) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> HasForeignKey<TDependent>(Expression<Func<TDependent, object?>> foreignKeyExpression) => this;
        public new ReferenceNavigationBuilder<TEntity, TRelated> HasForeignKey(params string[] foreignKeyPropertyNames) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> HasPrincipalKey(Expression<Func<TEntity, object?>> keyExpression) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> HasPrincipalKey(Expression<Func<TRelated, object?>> keyExpression) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> HasPrincipalKey<TPrincipal>(Expression<Func<TPrincipal, object?>> keyExpression) => this;
        public new ReferenceNavigationBuilder<TEntity, TRelated> HasPrincipalKey(params string[] keyPropertyNames) => this;
        public new ReferenceNavigationBuilder<TEntity, TRelated> HasConstraintName(string name) => this;
        public new ReferenceNavigationBuilder<TEntity, TRelated> OnDelete(DeleteBehavior deleteBehavior) => this;
        public new ReferenceNavigationBuilder<TEntity, TRelated> IsRequired(bool required = true) => this;
    }

    public class EntityTypeBuilder<TEntity> : EntityTypeBuilder where TEntity : class
    {
        public PropertyBuilder Property<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression) => new();
        public EntityTypeBuilder<TEntity> ComplexProperty<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression, Action<object>? action = null) => this;
        public PropertyBuilder PrimitiveCollection<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression) => new();
        public EntityTypeBuilder<TEntity> HasIndex(Expression<Func<TEntity, object?>> indexExpression) => this;
        public EntityTypeBuilder<TEntity> HasKey(Expression<Func<TEntity, object?>> keyExpression) => this;
        public ReferenceNavigationBuilder<TEntity, TRelated> HasOne<TRelated>(Expression<Func<TEntity, TRelated?>>? navigationExpression = null) where TRelated : class => new();
        public ReferenceNavigationBuilder<TEntity, TRelated> HasMany<TRelated>(Expression<Func<TEntity, IEnumerable<TRelated>?>>? navigationExpression = null) where TRelated : class => new();
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
