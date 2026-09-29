using NUnit.Framework;
using UnityEngine;

namespace DependencyInjection.Tests {
    public sealed class InjectorContractTests {
        sealed class Consumer {
            [Inject] public IInjectorMultiSceneTestService Field;
            [Inject] public IInjectorMultiSceneTestService Property { get; private set; }
            public IInjectorMultiSceneTestService MethodValue;

            [Inject]
            void SetService(IInjectorMultiSceneTestService service) => MethodValue = service;
        }

        [Test]
        public void ExplicitContractInjectsIntoPlainObjectAndMetadataIsCached() {
            var service = new InjectorMultiSceneTestService();
            var providerObject = new GameObject("Provider");
            var injectorObject = new GameObject("Injector");
            try {
                providerObject.AddComponent<InjectorMultiSceneTestProvider>().Service = service;
                var injector = injectorObject.AddComponent<Injector>();
                injector.Rebuild();
                var consumer = new Consumer();
                injector.InjectObject(consumer);

                Assert.AreSame(service, consumer.Field);
                Assert.AreSame(service, consumer.Property);
                Assert.AreSame(service, consumer.MethodValue);
                Assert.AreSame(InjectionMetadata.Get(typeof(Consumer)), InjectionMetadata.Get(typeof(Consumer)));
            }
            finally {
                Object.DestroyImmediate(injectorObject);
                Object.DestroyImmediate(providerObject);
            }
        }
    }
}
