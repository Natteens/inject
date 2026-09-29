using System;
using UnityEngine;

namespace DependencyInjection {
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class InjectAttribute : PropertyAttribute {
        public bool Optional { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class ProvideAttribute : PropertyAttribute {
        public Type ContractType { get; }
        public bool RegisterInterfaces { get; set; }

        public ProvideAttribute() { }

        public ProvideAttribute(Type contractType) {
            ContractType = contractType;
        }
    }

    public interface IDependencyProvider { }

    public enum DuplicateProviderPolicy {
        Replace,
        KeepFirst,
        Error
    }

    public enum MissingDependencyPolicy {
        Error,
        Warning,
        Ignore
    }

    public enum InjectorLogLevel {
        Silent,
        Errors,
        Warnings,
        Verbose
    }
}
