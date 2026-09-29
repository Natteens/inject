# Inject

A small scene-aware dependency injector for Unity 6000.3+. Add an `Injector` component, mark providers with `[Provide]`, and mark members with `[Inject]`.

```csharp
using DependencyInjection;

public sealed class HealthConsumer : MonoBehaviour {
    [Inject] private HealthService health;
}
```

The GameInit dependency-injection API is preserved under the `DependencyInjection` namespace: `Injector`, `InjectAttribute`, `ProvideAttribute`, `IDependencyProvider`, and the existing policy enums. It supports class, field, property, and parameterless method providers; fields, properties, and methods can receive dependencies. Scene changes are debounced and stale scene providers are discarded.

The Inspector and creation menu are in the Editor assembly. A basic example is available through Package Manager Samples. See [Documentation](Documentation~/index.md). MIT license.
