using System.Reflection;
using Spectre.Console.Cli;

namespace Baun.Cli;

/// <summary>
/// A minimal dependency container for Spectre.Console.Cli, so commands can receive services in their constructors.
/// </summary>
internal sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly Dictionary<Type, List<Func<TypeResolver, object>>> factories = [];

    public void Register(Type service, Type implementation) =>
        Add(service, resolver => resolver.Create(implementation));

    public void RegisterInstance(Type service, object implementation) =>
        Add(service, _ => implementation);

    public void RegisterLazy(Type service, Func<object> factory)
    {
        var instance = new Lazy<object>(factory);
        Add(service, _ => instance.Value);
    }

    public ITypeResolver Build() => new TypeResolver(factories);

    private void Add(Type service, Func<TypeResolver, object> factory)
    {
        if (!factories.TryGetValue(service, out var list))
        {
            factories[service] = list = [];
        }

        list.Add(factory);
    }

    private sealed class TypeResolver(Dictionary<Type, List<Func<TypeResolver, object>>> factories) : ITypeResolver
    {
        public object? Resolve(Type? type)
        {
            if (type is null)
            {
                return null;
            }

            // The last registration wins for a single service.
            if (factories.TryGetValue(type, out var registered))
            {
                return registered[^1](this);
            }

            // All registrations, for IEnumerable<T>.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var elementType = type.GetGenericArguments()[0];
                var instances = factories.TryGetValue(elementType, out var all) ? all.Select(factory => factory(this)).ToArray() : [];
                var array = Array.CreateInstance(elementType, instances.Length);
                Array.Copy(instances, array, instances.Length);
                return array;
            }

            return type.IsAbstract || type.IsInterface ? null : Create(type);
        }

        public object Create(Type type)
        {
            ConstructorInfo constructor = type.GetConstructors()
                .OrderByDescending(constructor => constructor.GetParameters().Length)
                .First();

            object[] arguments = constructor.GetParameters()
                .Select(parameter => Resolve(parameter.ParameterType) ?? throw new InvalidOperationException($"Cannot create {type.Name}: no service registered for {parameter.ParameterType.Name}."))
                .ToArray();

            return constructor.Invoke(arguments);
        }
    }
}
