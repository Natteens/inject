using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DependencyInjection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DependencyInjection.Tests {
    public interface IInjectorMultiSceneTestService { }

    public sealed class InjectorMultiSceneTestService : IInjectorMultiSceneTestService { }

    public sealed class InjectorMultiSceneTestProvider : MonoBehaviour {
        [Provide] public IInjectorMultiSceneTestService Service;
    }

    public sealed class InjectorMultiSceneTestConsumer : MonoBehaviour {
        [Inject(Optional = true)] public IInjectorMultiSceneTestService FieldService;
        [Inject(Optional = true)] public IInjectorMultiSceneTestService PropertyService { get; private set; }

        public IInjectorMultiSceneTestService MethodService { get; private set; }
        public int MethodInjectionCount { get; private set; }

        [Inject(Optional = true)]
        void InjectService(IInjectorMultiSceneTestService service) {
            MethodService = service;
            MethodInjectionCount++;
        }
    }

    public sealed class InjectorMultiSceneTests {
        static readonly MethodInfo HandleSceneLoaded = typeof(Injector).GetMethod(
            "HandleSceneLoaded",
            BindingFlags.Instance | BindingFlags.NonPublic);

        readonly List<GameObject> objects = new();
        readonly List<Scene> scenes = new();
        int sceneIndex;

        [UnityTearDown]
        public IEnumerator TearDown() {
            for (int i = 0; i < objects.Count; i++) {
                if (objects[i] != null) {
                    Object.Destroy(objects[i]);
                }
            }

            yield return null;

            for (int i = 0; i < scenes.Count; i++) {
                var scene = scenes[i];
                if (!scene.IsValid() || !scene.isLoaded) {
                    continue;
                }

                var operation = SceneManager.UnloadSceneAsync(scene);
                while (operation != null && !operation.isDone) {
                    yield return null;
                }
            }
        }

        [UnityTest]
        public IEnumerator SceneLoadEvents_DebounceAndReinjectConsumersAcrossLoadedScenes() {
            var consumerScene = CreateScene("Consumer");
            var providerScene = CreateScene("Provider");
            var consumer = CreateInScene<InjectorMultiSceneTestConsumer>("Consumer", consumerScene);
            var injector = CreateInjector();

            Assert.IsNull(consumer.FieldService);
            Assert.IsNull(consumer.PropertyService);
            Assert.Zero(consumer.MethodInjectionCount);

            var service = new InjectorMultiSceneTestService();
            var provider = CreateInScene<InjectorMultiSceneTestProvider>("Provider", providerScene);
            provider.Service = service;

            NotifySceneLoaded(injector, providerScene);
            NotifySceneLoaded(injector, providerScene);
            NotifySceneLoaded(injector, providerScene);

            Assert.IsNull(consumer.FieldService, "Automatic scene injection must be deferred.");

            yield return null;
            yield return null;

            Assert.AreSame(service, consumer.FieldService);
            Assert.AreSame(service, consumer.PropertyService);
            Assert.AreSame(service, consumer.MethodService);
            Assert.AreEqual(1, consumer.MethodInjectionCount, "Debounced scene events must cause one global rebuild.");
            Assert.IsTrue(injector.TryResolve<IInjectorMultiSceneTestService>(out var resolved));
            Assert.AreSame(service, resolved);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator GlobalRebuild_UpdatesExistingMembersAndCallsMethodInjectionAgain() {
            var consumerScene = CreateScene("Consumer");
            var providerScene = CreateScene("Provider");
            var firstService = new InjectorMultiSceneTestService();
            var provider = CreateInScene<InjectorMultiSceneTestProvider>("Provider", providerScene);
            provider.Service = firstService;
            var consumer = CreateInScene<InjectorMultiSceneTestConsumer>("Consumer", consumerScene);
            var injector = CreateInjector();

            Assert.AreSame(firstService, consumer.FieldService);
            Assert.AreEqual(1, consumer.MethodInjectionCount);

            var replacement = new InjectorMultiSceneTestService();
            provider.Service = replacement;
            injector.RebuildAndInjectAllLoadedObjects();

            Assert.AreSame(replacement, consumer.FieldService);
            Assert.AreSame(replacement, consumer.PropertyService);
            Assert.AreSame(replacement, consumer.MethodService);
            Assert.AreEqual(2, consumer.MethodInjectionCount);

            provider.Service = null;
            injector.RebuildAndInjectAllLoadedObjects();

            Assert.IsNull(consumer.FieldService);
            Assert.IsNull(consumer.PropertyService);
            Assert.AreEqual(2, consumer.MethodInjectionCount, "Optional method injection must wait until all arguments resolve.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneUnload_RebuildsRegistryAndClearsStaleMembers() {
            var consumerScene = CreateScene("Consumer");
            var providerScene = CreateScene("Provider");
            var service = new InjectorMultiSceneTestService();
            var provider = CreateInScene<InjectorMultiSceneTestProvider>("Provider", providerScene);
            provider.Service = service;
            var consumer = CreateInScene<InjectorMultiSceneTestConsumer>("Consumer", consumerScene);
            var injector = CreateInjector();

            Assert.AreSame(service, consumer.FieldService);
            Assert.IsTrue(injector.TryResolve<IInjectorMultiSceneTestService>(out _));

            var operation = SceneManager.UnloadSceneAsync(providerScene);
            while (operation != null && !operation.isDone) {
                yield return null;
            }

            yield return null;
            yield return null;

            Assert.IsFalse(injector.TryResolve<IInjectorMultiSceneTestService>(out _));
            Assert.IsNull(consumer.FieldService);
            Assert.IsNull(consumer.PropertyService);
        }

        Scene CreateScene(string role) {
            var scene = SceneManager.CreateScene($"InjectorTests_{role}_{sceneIndex++}");
            scenes.Add(scene);
            return scene;
        }

        T CreateInScene<T>(string name, Scene scene) where T : Component {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject.AddComponent<T>();
        }

        Injector CreateInjector() {
            var gameObject = new GameObject("Injector");
            objects.Add(gameObject);
            return gameObject.AddComponent<Injector>();
        }

        static void NotifySceneLoaded(Injector injector, Scene scene) {
            Assert.IsNotNull(HandleSceneLoaded);
            HandleSceneLoaded.Invoke(injector, new object[] { scene, LoadSceneMode.Additive });
        }
    }
}
