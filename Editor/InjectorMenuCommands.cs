#if UNITY_EDITOR
using DependencyInjection;
using UnityEditor;
using UnityEngine;

namespace DependencyInjection.Editor
{
    /// <summary>
    /// Comandos de menu para criação de elementos relacionados à injeção de dependência
    /// </summary>
    public static class InjectorMenuCommands
    {
        private const string MENU_PATH = "GameObject/Dependency Injection/Create Injector";
        private const string DEFAULT_INJECTOR_NAME = "Injector";

        /// <summary>
        /// Cria um GameObject com componente Injector através do menu de contexto
        /// </summary>
        [MenuItem(MENU_PATH)]
        private static void CreateInjector(MenuCommand command)
        {
            var injectorObject = CreateInjectorGameObject(command.context as GameObject);
            RegisterUndoAndSelect(injectorObject);
        }

        /// <summary>
        /// Cria e configura o GameObject para o injector
        /// </summary>
        private static GameObject CreateInjectorGameObject(GameObject parent)
        {
            var obj = new GameObject(DEFAULT_INJECTOR_NAME);
            GameObjectUtility.SetParentAndAlign(obj, parent);
            obj.AddComponent<Injector>();
            return obj;
        }

        /// <summary>
        /// Registra a operação no sistema de Undo e seleciona o objeto criado
        /// </summary>
        private static void RegisterUndoAndSelect(GameObject obj)
        {
            Undo.RegisterCreatedObjectUndo(obj, "Create Injector");
            Selection.activeObject = obj;
        }
    }
}
#endif