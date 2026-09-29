using UnityEngine;

namespace DependencyInjection.Samples {
    public interface ISomeService {
        string Ping();
    }

    public sealed class SomeService : MonoBehaviour, ISomeService {
        public string Ping() => "pong";
    }

    public sealed class FieldProvider : MonoBehaviour {
        [Provide(typeof(ISomeService))]
        [SerializeField] SomeService service;
    }

    public sealed class PropertyProvider : MonoBehaviour {
        [SerializeField] SomeService service;

        [Provide(typeof(ISomeService))]
        public SomeService Service => service;
    }

    [Provide(typeof(ISomeService))]
    public sealed class ComponentProvider : MonoBehaviour, ISomeService {
        public string Ping() => "component-pong";
    }

    public sealed class LegacyMethodProvider : MonoBehaviour, IDependencyProvider {
        [SerializeField] SomeService service;

        [Provide]
        ISomeService ProvideSomeService() => service;
    }

    public sealed class Consumer : MonoBehaviour {
        [Inject] ISomeService required;
        [Inject(Optional = true)] ISomeService optional;
        [Inject] public ISomeService Injected { get; private set; }

        ISomeService constructed;

        [Inject]
        void Construct(ISomeService service) {
            constructed = service;
        }

        void Start() {
            Debug.Log($"[Consumer] required={(required != null)} optional={(optional != null)} property={(Injected != null)} method={(constructed != null)}");
        }
    }
}
