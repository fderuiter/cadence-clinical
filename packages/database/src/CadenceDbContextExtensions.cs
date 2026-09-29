using System;
using System.Linq;
using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Database;

/// <summary>
/// Extension methods for registering EF Core <see cref="DbContext"/> instances with compiled model optimization.
/// </summary>
public static class CadenceDbContextExtensions
{
    /// <summary>
    /// Registers the specified <see cref="DbContext"/> with pre-compiled EF Core models bound via <c>UseModel</c> when available.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsAction">Optional configuration action for <see cref="DbContextOptionsBuilder"/>.</param>
    /// <param name="contextLifetime">The lifetime with which to register the DbContext service in the container.</param>
    /// <param name="optionsLifetime">The lifetime with which to register the DbContextOptions service in the container.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCadenceDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? optionsAction = null,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
        where TContext : DbContext
    {
        return services.AddCadenceDbContext<TContext>(
            optionsAction != null ? (_, options) => optionsAction(options) : null,
            contextLifetime,
            optionsLifetime);
    }

    /// <summary>
    /// Registers the specified <see cref="DbContext"/> with pre-compiled EF Core models bound via <c>UseModel</c> when available,
    /// accepting an <see cref="IServiceProvider"/>-aware options configuration delegate.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsAction">Optional configuration action with service provider for <see cref="DbContextOptionsBuilder"/>.</param>
    /// <param name="contextLifetime">The lifetime with which to register the DbContext service in the container.</param>
    /// <param name="optionsLifetime">The lifetime with which to register the DbContextOptions service in the container.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCadenceDbContext<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
        where TContext : DbContext
    {
        IModel? compiledModel = GetCompiledModel<TContext>();

        return services.AddDbContext<TContext>((sp, options) =>
        {
            if (compiledModel != null)
            {
                options.UseModel(compiledModel);
            }

            optionsAction?.Invoke(sp, options);
        }, contextLifetime, optionsLifetime);
    }

    /// <summary>
    /// Searches the assembly of <typeparamref name="TContext"/> for a pre-compiled model root class
    /// (e.g. <c>&lt;DbContextName&gt;Model</c>) and returns its static <c>Instance</c> property value if found.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <returns>The compiled <see cref="IModel"/> instance, or null if not found.</returns>
    public static IModel? GetCompiledModel<TContext>() where TContext : DbContext
    {
        Type contextType = typeof(TContext);
        Assembly assembly = contextType.Assembly;
        string modelClassName = contextType.Name + "Model";

        try
        {
            Type[] candidateTypes = assembly.GetTypes();
            foreach (Type type in candidateTypes)
            {
                if (type.Name == modelClassName)
                {
                    PropertyInfo? prop = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null && typeof(IModel).IsAssignableFrom(prop.PropertyType) && prop.GetValue(null) is IModel model)
                    {
                        return model;
                    }
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            foreach (Type? type in ex.Types)
            {
                if (type != null && type.Name == modelClassName)
                {
                    PropertyInfo? prop = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null && typeof(IModel).IsAssignableFrom(prop.PropertyType) && prop.GetValue(null) is IModel model)
                    {
                        return model;
                    }
                }
            }
        }

        return null;
    }
}
