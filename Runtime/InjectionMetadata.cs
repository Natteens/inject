using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DependencyInjection {
    public sealed class InjectionMetadata {
        public struct InjectField {
            public FieldInfo Field;
            public bool Optional;
        }

        public struct InjectProperty {
            public PropertyInfo Property;
            public bool Optional;
        }

        public struct InjectMethod {
            public MethodInfo Method;
            public Type[] Parameters;
            public bool Optional;
        }

        public struct ProvideField {
            public FieldInfo Field;
            public ProvideAttribute Attribute;
        }

        public struct ProvideProperty {
            public PropertyInfo Property;
            public ProvideAttribute Attribute;
        }

        public InjectField[] InjectableFields;
        public InjectProperty[] InjectableProperties;
        public InjectMethod[] InjectableMethods;

        public ProvideField[] ProviderFields;
        public ProvideProperty[] ProviderProperties;
        public MethodInfo[] ProviderMethods;
        public ProvideAttribute ClassProvide;

        public bool HasInjectables;
        public bool HasProviders;

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Dictionary<Type, InjectionMetadata> Cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            ClearCache();
        }

        public static InjectionMetadata Get(Type type) {
            if (Cache.TryGetValue(type, out var cached)) {
                return cached;
            }

            var metadata = Build(type);
            Cache[type] = metadata;
            return metadata;
        }

        public static void ClearCache() {
            Cache.Clear();
        }

        static InjectionMetadata Build(Type type) {
            var metadata = new InjectionMetadata();

            var injectFields = new List<InjectField>();
            var injectProps = new List<InjectProperty>();
            var injectMethods = new List<InjectMethod>();
            var provideFields = new List<ProvideField>();
            var provideProps = new List<ProvideProperty>();
            var provideMethods = new List<MethodInfo>();

            var fields = type.GetFields(Flags);
            for (int i = 0; i < fields.Length; i++) {
                var field = fields[i];
                var inject = field.GetCustomAttribute<InjectAttribute>();
                if (inject != null) {
                    injectFields.Add(new InjectField { Field = field, Optional = inject.Optional });
                }

                var provide = field.GetCustomAttribute<ProvideAttribute>();
                if (provide != null) {
                    provideFields.Add(new ProvideField { Field = field, Attribute = provide });
                }
            }

            var properties = type.GetProperties(Flags);
            for (int i = 0; i < properties.Length; i++) {
                var property = properties[i];
                var inject = property.GetCustomAttribute<InjectAttribute>();
                if (inject != null && property.CanWrite) {
                    injectProps.Add(new InjectProperty { Property = property, Optional = inject.Optional });
                }

                var provide = property.GetCustomAttribute<ProvideAttribute>();
                if (provide != null && property.CanRead) {
                    provideProps.Add(new ProvideProperty { Property = property, Attribute = provide });
                }
            }

            var methods = type.GetMethods(Flags);
            for (int i = 0; i < methods.Length; i++) {
                var method = methods[i];
                var inject = method.GetCustomAttribute<InjectAttribute>();
                if (inject != null) {
                    var parameters = method.GetParameters();
                    var paramTypes = new Type[parameters.Length];
                    for (int p = 0; p < parameters.Length; p++) {
                        paramTypes[p] = parameters[p].ParameterType;
                    }

                    injectMethods.Add(new InjectMethod { Method = method, Parameters = paramTypes, Optional = inject.Optional });
                }

                if (Attribute.IsDefined(method, typeof(ProvideAttribute)) && method.ReturnType != typeof(void)) {
                    provideMethods.Add(method);
                }
            }

            metadata.InjectableFields = injectFields.ToArray();
            metadata.InjectableProperties = injectProps.ToArray();
            metadata.InjectableMethods = injectMethods.ToArray();
            metadata.ProviderFields = provideFields.ToArray();
            metadata.ProviderProperties = provideProps.ToArray();
            metadata.ProviderMethods = provideMethods.ToArray();
            metadata.ClassProvide = type.GetCustomAttribute<ProvideAttribute>(false);

            metadata.HasInjectables = metadata.InjectableFields.Length > 0
                || metadata.InjectableProperties.Length > 0
                || metadata.InjectableMethods.Length > 0;
            metadata.HasProviders = metadata.ProviderFields.Length > 0
                || metadata.ProviderProperties.Length > 0
                || metadata.ProviderMethods.Length > 0
                || metadata.ClassProvide != null;

            return metadata;
        }
    }
}
