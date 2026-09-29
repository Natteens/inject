using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DependencyInjection {
    [DefaultExecutionOrder(-1000)]
    public class Injector : MonoBehaviour {
        public sealed class DependencyEntry {
            public Type Contract;
            public object Instance;
            public Object Owner;
            public Scene Scene;
        }

        [SerializeField] bool autoInjectOnAwake = true;
        [SerializeField] bool autoInjectLoadedScenes = true;
        [SerializeField] bool injectExistingLoadedScenesOnEnable = true;
        [SerializeField] bool logOptionalMisses;
        [SerializeField] DuplicateProviderPolicy duplicateProviderPolicy = DuplicateProviderPolicy.Replace;
        [SerializeField] MissingDependencyPolicy missingDependencyPolicy = MissingDependencyPolicy.Error;
        [SerializeField] InjectorLogLevel logLevel = InjectorLogLevel.Warnings;

        readonly Dictionary<Type, DependencyEntry> registry = new();
        bool rebuildQueued;
        Coroutine rebuildCoroutine;
        int sceneChangeVersion;

        public IReadOnlyDictionary<Type, DependencyEntry> Registry => registry;

        void Awake() {
            if (autoInjectOnAwake) {
                Rebuild();
            }
        }

        void OnEnable() {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;

            if (injectExistingLoadedScenesOnEnable && !autoInjectOnAwake) {
                Rebuild();
            }
        }

        void OnDisable() {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            CancelQueuedRebuild();
        }

        public void Rebuild() {
            CancelQueuedRebuild();
            RebuildAllLoadedObjects();
        }

        void RebuildAllLoadedObjects() {
            registry.Clear();
            var monoBehaviours = FindMonoBehaviours();
            LogVerbose($"[Injector] Rebuild started. Loaded scenes: {CountLoadedScenes()}.");
            RegisterProviders(monoBehaviours);
            int injected = InjectAll(monoBehaviours, true);
            LogVerbose($"[Injector] Providers registered: {registry.Count}.");
            LogVerbose($"[Injector] Injectables injected: {injected}.");
        }

        public void RebuildAndInjectAllLoadedObjects() {
            Rebuild();
        }

        void HandleSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (!autoInjectLoadedScenes) {
                return;
            }

            QueueGlobalRebuild($"[Injector] Rebuild scheduled after scene load: {scene.name}.");
        }

        void HandleSceneUnloaded(Scene scene) {
            QueueGlobalRebuild($"[Injector] Rebuild scheduled after scene unload: {scene.name}.");
        }

        void QueueGlobalRebuild(string logMessage) {
            sceneChangeVersion++;
            LogVerbose(logMessage);

            if (rebuildQueued) {
                return;
            }

            rebuildQueued = true;
            rebuildCoroutine = StartCoroutine(RebuildAfterSceneChanges(sceneChangeVersion));
        }

        IEnumerator RebuildAfterSceneChanges(int queuedVersion) {
            while (true) {
                yield return null;
                if (queuedVersion == sceneChangeVersion) {
                    break;
                }

                queuedVersion = sceneChangeVersion;
            }

            rebuildQueued = false;
            rebuildCoroutine = null;
            RebuildAllLoadedObjects();
        }

        void CancelQueuedRebuild() {
            if (rebuildCoroutine != null) {
                StopCoroutine(rebuildCoroutine);
            }

            rebuildQueued = false;
            rebuildCoroutine = null;
        }

        public void InjectScene(Scene scene) {
            if (!scene.IsValid() || !scene.isLoaded) {
                return;
            }

            var roots = scene.GetRootGameObjects();
            var collected = new List<MonoBehaviour>();
            for (int i = 0; i < roots.Length; i++) {
                collected.AddRange(roots[i].GetComponentsInChildren<MonoBehaviour>(true));
            }

            var array = collected.ToArray();
            RegisterProviders(array);
            InjectAll(array, false);
            LogVerbose($"[Injector] Injected scene '{scene.name}' ({array.Length} components).");
        }

        public void InjectGameObject(GameObject root, bool includeChildren = true) {
            if (root == null) {
                return;
            }

            var array = includeChildren
                ? root.GetComponentsInChildren<MonoBehaviour>(true)
                : root.GetComponents<MonoBehaviour>();
            RegisterProviders(array);
            InjectAll(array, false);
        }

        public void InjectObject(object target) {
            if (target == null) {
                return;
            }

            Inject(target);
        }

        void RegisterProviders(MonoBehaviour[] monoBehaviours) {
            for (int i = 0; i < monoBehaviours.Length; i++) {
                var mono = monoBehaviours[i];
                if (mono == null) {
                    continue;
                }

                var metadata = InjectionMetadata.Get(mono.GetType());
                if (!metadata.HasProviders) {
                    continue;
                }

                RegisterFromProvider(mono, metadata);
            }
        }

        void RegisterFromProvider(MonoBehaviour mono, InjectionMetadata metadata) {
            var type = mono.GetType();

            if (metadata.ClassProvide != null) {
                RegisterClassProvider(mono, type, metadata.ClassProvide);
            }

            var providerFields = metadata.ProviderFields;
            for (int i = 0; i < providerFields.Length; i++) {
                var field = providerFields[i].Field;
                var attr = providerFields[i].Attribute;
                var value = field.GetValue(mono);
                if (value == null || (value is Object unityValue && unityValue == null)) {
                    LogWarning($"[Injector] Provider field '{field.Name}' on '{type.Name}' is null and was not registered.");
                    continue;
                }

                var contract = attr.ContractType ?? field.FieldType;
                if (!ValidateContract(contract, value, type, field.Name)) {
                    continue;
                }

                Register(contract, value, mono);
            }

            var providerProps = metadata.ProviderProperties;
            for (int i = 0; i < providerProps.Length; i++) {
                var property = providerProps[i].Property;
                var attr = providerProps[i].Attribute;
                var value = property.GetValue(mono);
                if (value == null || (value is Object unityValue && unityValue == null)) {
                    LogWarning($"[Injector] Provider property '{property.Name}' on '{type.Name}' is null and was not registered.");
                    continue;
                }

                var contract = attr.ContractType ?? property.PropertyType;
                if (!ValidateContract(contract, value, type, property.Name)) {
                    continue;
                }

                Register(contract, value, mono);
            }

            var providerMethods = metadata.ProviderMethods;
            for (int i = 0; i < providerMethods.Length; i++) {
                var method = providerMethods[i];
                var attr = method.GetCustomAttribute<ProvideAttribute>();
                var value = method.Invoke(mono, null);
                if (value == null || (value is Object unityValue && unityValue == null)) {
                    LogWarning($"[Injector] Provider method '{method.Name}' on '{type.Name}' returned null and was not registered.");
                    continue;
                }

                var contract = attr != null && attr.ContractType != null ? attr.ContractType : method.ReturnType;
                if (!ValidateContract(contract, value, type, method.Name)) {
                    continue;
                }

                Register(contract, value, mono);
            }
        }

        void RegisterClassProvider(MonoBehaviour mono, Type type, ProvideAttribute attr) {
            if (attr.ContractType != null) {
                if (ValidateContract(attr.ContractType, mono, type, type.Name)) {
                    Register(attr.ContractType, mono, mono);
                }

                return;
            }

            Register(type, mono, mono);

            if (attr.RegisterInterfaces) {
                var interfaces = type.GetInterfaces();
                for (int i = 0; i < interfaces.Length; i++) {
                    if (interfaces[i] == typeof(IDependencyProvider)) {
                        continue;
                    }

                    Register(interfaces[i], mono, mono);
                }
            }
        }

        bool ValidateContract(Type contract, object value, Type ownerType, string memberName) {
            if (contract.IsInstanceOfType(value)) {
                return true;
            }

            LogError($"[Injector] '{ownerType.Name}.{memberName}' provides '{value.GetType().Name}' which is not assignable to contract '{contract.Name}'.");
            return false;
        }

        public void Register<TContract>(TContract instance, Object owner = null) {
            Register(typeof(TContract), instance, owner);
        }

        public void Register(Type contract, object instance, Object owner = null) {
            if (instance == null || (instance is Object unityInstance && unityInstance == null)) {
                LogWarning($"[Injector] Attempted to register null instance for contract '{contract.Name}'.");
                return;
            }

            var scene = default(Scene);
            if (owner is Component component && component != null) {
                scene = component.gameObject.scene;
            }

            if (registry.TryGetValue(contract, out var existing)) {
                switch (duplicateProviderPolicy) {
                    case DuplicateProviderPolicy.KeepFirst:
                        LogWarning($"[Injector] Duplicate provider for '{contract.Name}' ignored (KeepFirst).");
                        return;
                    case DuplicateProviderPolicy.Error:
                        LogError($"[Injector] Duplicate provider for '{contract.Name}'. Existing owner '{NameOf(existing.Owner)}', new owner '{NameOf(owner)}'.");
                        return;
                    default:
                        LogWarning($"[Injector] Duplicate provider for '{contract.Name}' replaced (Replace).");
                        break;
                }
            }

            registry[contract] = new DependencyEntry {
                Contract = contract,
                Instance = instance,
                Owner = owner,
                Scene = scene
            };
        }

        public void Unregister(Type contract, object instance = null) {
            if (!registry.TryGetValue(contract, out var entry)) {
                return;
            }

            if (instance != null && !ReferenceEquals(entry.Instance, instance)) {
                return;
            }

            registry.Remove(contract);
        }

        public void ClearRegistry() {
            registry.Clear();
        }

        public bool TryResolve<T>(out T value) {
            if (TryResolve(typeof(T), out var resolved) && resolved is T typed) {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        public bool TryResolve(Type type, out object value) {
            if (registry.TryGetValue(type, out var entry)) {
                if (entry.Instance is Object unityInstance && unityInstance == null) {
                    registry.Remove(type);
                    value = null;
                    return false;
                }

                value = entry.Instance;
                return true;
            }

            value = null;
            return false;
        }

        public T Resolve<T>() {
            return (T) Resolve(typeof(T));
        }

        public object Resolve(Type type) {
            if (TryResolve(type, out var value)) {
                return value;
            }

            throw new InvalidOperationException($"[Injector] No provider registered for '{type.Name}'.");
        }

        int InjectAll(MonoBehaviour[] monoBehaviours, bool overwriteExisting) {
            int injected = 0;
            for (int i = 0; i < monoBehaviours.Length; i++) {
                var mono = monoBehaviours[i];
                if (mono == null) {
                    continue;
                }

                var metadata = InjectionMetadata.Get(mono.GetType());
                if (!metadata.HasInjectables) {
                    continue;
                }

                InjectWithMetadata(mono, metadata, overwriteExisting);
                injected++;
            }

            return injected;
        }

        void Inject(object instance) {
            var metadata = InjectionMetadata.Get(instance.GetType());
            InjectWithMetadata(instance, metadata, false);
        }

        void InjectWithMetadata(object instance, InjectionMetadata metadata, bool overwriteExisting) {
            var type = instance.GetType();

            var fields = metadata.InjectableFields;
            for (int i = 0; i < fields.Length; i++) {
                var field = fields[i].Field;
                if (!overwriteExisting) {
                    var current = field.GetValue(instance);
                    if (current != null && !(current is Object cu && cu == null)) {
                        LogVerbose($"[Injector] Field '{field.Name}' on '{type.Name}' already set, skipped.");
                        continue;
                    }
                }

                if (TryResolve(field.FieldType, out var resolved)) {
                    field.SetValue(instance, resolved);
                } else {
                    if (overwriteExisting) {
                        field.SetValue(instance, DefaultValue(field.FieldType));
                    }

                    HandleMissing(field.FieldType, type, field.Name, fields[i].Optional);
                }
            }

            var properties = metadata.InjectableProperties;
            for (int i = 0; i < properties.Length; i++) {
                var property = properties[i].Property;
                if (TryResolve(property.PropertyType, out var resolved)) {
                    property.SetValue(instance, resolved);
                } else {
                    if (overwriteExisting) {
                        property.SetValue(instance, DefaultValue(property.PropertyType));
                    }

                    HandleMissing(property.PropertyType, type, property.Name, properties[i].Optional);
                }
            }

            var methods = metadata.InjectableMethods;
            for (int i = 0; i < methods.Length; i++) {
                var method = methods[i];
                var parameters = method.Parameters;
                var args = new object[parameters.Length];
                bool resolvedAll = true;
                for (int p = 0; p < parameters.Length; p++) {
                    if (TryResolve(parameters[p], out var arg)) {
                        args[p] = arg;
                    } else {
                        resolvedAll = false;
                        HandleMissing(parameters[p], type, method.Method.Name, method.Optional);
                    }
                }

                if (resolvedAll) {
                    method.Method.Invoke(instance, args);
                }
            }
        }

        void HandleMissing(Type contract, Type ownerType, string memberName, bool optional) {
            if (optional) {
                if (logOptionalMisses) {
                    LogWarning($"[Injector] Optional dependency '{contract.Name}' for '{ownerType.Name}.{memberName}' not found.");
                }

                return;
            }

            switch (missingDependencyPolicy) {
                case MissingDependencyPolicy.Error:
                    throw new InvalidOperationException($"[Injector] Failed to resolve '{contract.Name}' for '{ownerType.Name}.{memberName}'.");
                case MissingDependencyPolicy.Warning:
                    LogWarning($"[Injector] Failed to resolve '{contract.Name}' for '{ownerType.Name}.{memberName}', left null.");
                    break;
            }
        }

        public void ValidateDependencies() {
            var monoBehaviours = FindMonoBehaviours();
            var provided = new HashSet<Type>();
            var errors = new List<string>();

            for (int i = 0; i < monoBehaviours.Length; i++) {
                var mono = monoBehaviours[i];
                if (mono == null) {
                    continue;
                }

                var metadata = InjectionMetadata.Get(mono.GetType());
                if (!metadata.HasProviders) {
                    continue;
                }

                CollectProvidedContracts(mono, metadata, provided, errors);
            }

            for (int i = 0; i < monoBehaviours.Length; i++) {
                var mono = monoBehaviours[i];
                if (mono == null) {
                    continue;
                }

                var metadata = InjectionMetadata.Get(mono.GetType());
                if (!metadata.HasInjectables) {
                    continue;
                }

                var type = mono.GetType();
                var fields = metadata.InjectableFields;
                for (int f = 0; f < fields.Length; f++) {
                    var field = fields[f].Field;
                    if (fields[f].Optional) {
                        continue;
                    }

                    var value = field.GetValue(mono);
                    bool hasValue = value != null && !(value is Object u && u == null);
                    if (!hasValue && !provided.Contains(field.FieldType)) {
                        errors.Add($"{type.Name} is missing '{field.FieldType.Name}' on '{mono.gameObject.name}'.");
                    }
                }

                var properties = metadata.InjectableProperties;
                for (int pr = 0; pr < properties.Length; pr++) {
                    if (properties[pr].Optional) {
                        continue;
                    }

                    var property = properties[pr].Property;
                    if (!provided.Contains(property.PropertyType)) {
                        errors.Add($"{type.Name} is missing '{property.PropertyType.Name}' (property '{property.Name}') on '{mono.gameObject.name}'.");
                    }
                }

                var methods = metadata.InjectableMethods;
                for (int m = 0; m < methods.Length; m++) {
                    if (methods[m].Optional) {
                        continue;
                    }

                    var parameters = methods[m].Parameters;
                    foreach (var t in parameters)
                    {
                        if (!provided.Contains(t)) {
                            errors.Add($"{type.Name}.{methods[m].Method.Name} is missing '{t.Name}' on '{mono.gameObject.name}'.");
                        }
                    }
                }
            }

            if (errors.Count == 0) {
                Debug.Log("[Injector] All dependencies are valid.");
            } else {
                Debug.LogError($"[Injector] {errors.Count} validation issue(s):");
                for (int i = 0; i < errors.Count; i++) {
                    Debug.LogError(errors[i]);
                }
            }
        }

        void CollectProvidedContracts(MonoBehaviour mono, InjectionMetadata metadata, HashSet<Type> provided, List<string> errors) {
            var type = mono.GetType();

            if (metadata.ClassProvide != null) {
                if (metadata.ClassProvide.ContractType != null) {
                    AddProvided(metadata.ClassProvide.ContractType, provided);
                } else {
                    AddProvided(type, provided);
                    if (metadata.ClassProvide.RegisterInterfaces) {
                        var interfaces = type.GetInterfaces();
                        for (int i = 0; i < interfaces.Length; i++) {
                            if (interfaces[i] != typeof(IDependencyProvider)) {
                                AddProvided(interfaces[i], provided);
                            }
                        }
                    }
                }
            }

            var providerFields = metadata.ProviderFields;
            for (int i = 0; i < providerFields.Length; i++) {
                var field = providerFields[i].Field;
                var attr = providerFields[i].Attribute;
                var value = field.GetValue(mono);
                bool hasValue = value != null && !(value is Object u && u == null);
                if (!hasValue) {
                    errors.Add($"Provider field '{type.Name}.{field.Name}' is null.");
                    continue;
                }

                var contract = attr.ContractType ?? field.FieldType;
                if (attr.ContractType != null && !contract.IsInstanceOfType(value)) {
                    errors.Add($"Provider field '{type.Name}.{field.Name}' value not assignable to '{contract.Name}'.");
                }

                AddProvided(contract, provided);
            }

            var providerProps = metadata.ProviderProperties;
            for (int i = 0; i < providerProps.Length; i++) {
                var property = providerProps[i].Property;
                var attr = providerProps[i].Attribute;
                AddProvided(attr.ContractType ?? property.PropertyType, provided);
            }

            var providerMethods = metadata.ProviderMethods;
            for (int i = 0; i < providerMethods.Length; i++) {
                var method = providerMethods[i];
                var attr = method.GetCustomAttribute<ProvideAttribute>();
                AddProvided(attr != null && attr.ContractType != null ? attr.ContractType : method.ReturnType, provided);
            }
        }

        void AddProvided(Type contract, HashSet<Type> provided) {
            provided.Add(contract);
        }

        public void ClearDependencies() {
            var monoBehaviours = FindMonoBehaviours();
            foreach (var mono in monoBehaviours)
            {
                if (mono == null) {
                    continue;
                }

                var metadata = InjectionMetadata.Get(mono.GetType());
                var fields = metadata.InjectableFields;
                for (int f = 0; f < fields.Length; f++) {
                    fields[f].Field.SetValue(mono, null);
                }
            }

            Debug.Log("[Injector] All injectable fields cleared.");
        }

        static MonoBehaviour[] FindMonoBehaviours() {
#if UNITY_6000_5_OR_NEWER
            return FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
#else
            return FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
        }

        static int CountLoadedScenes() {
            int loadedScenes = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                if (SceneManager.GetSceneAt(i).isLoaded) {
                    loadedScenes++;
                }
            }

            return loadedScenes;
        }

        static object DefaultValue(Type type) {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        static string NameOf(Object obj) {
            return obj == null ? "<none>" : obj.name;
        }

        void LogVerbose(string message) {
            if (logLevel >= InjectorLogLevel.Verbose) {
                Debug.Log(message);
            }
        }

        void LogWarning(string message) {
            if (logLevel >= InjectorLogLevel.Warnings) {
                Debug.LogWarning(message);
            }
        }

        void LogError(string message) {
            if (logLevel >= InjectorLogLevel.Errors) {
                Debug.LogError(message);
            }
        }
    }
}
