# Inject

Add one `Injector` component to a scene. `[Provide]` registers a component or a field, property, or parameterless method result. Pass a contract type to `[Provide(typeof(IMyService))]` when consumers should request an interface. `[Inject]` works on fields, writable properties, and methods. Mark an injection optional with `[Inject(Optional = true)]`.

`Injector` retains `Rebuild`, `InjectScene`, `InjectGameObject`, `InjectObject`, `Register`, `Unregister`, `TryResolve`, `Resolve`, `ClearRegistry`, and `ValidateDependencies`. Its policies control duplicate providers, missing dependencies, and logging. Loaded-scene changes trigger a debounced rebuild; unloading a provider scene invalidates its registrations.

Replace `using GameInit.DependencyInjection;` with `using DependencyInjection;` and the old assembly reference with `com.natteens.inject`.
