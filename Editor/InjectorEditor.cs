using DependencyInjection;
using UnityEditor;
using UnityEngine;

namespace DependencyInjection.Editor {
    [CustomEditor(typeof(Injector))]
    public class InjectorEditor : UnityEditor.Editor {
        bool showRegistered = true;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            var injector = (Injector) target;

            EditorGUILayout.Space();

            if (GUILayout.Button("Validate Dependencies")) {
                injector.ValidateDependencies();
            }

            if (GUILayout.Button("Rebuild And Inject")) {
                injector.RebuildAndInjectAllLoadedObjects();
            }

            if (GUILayout.Button("Clear Registry")) {
                injector.ClearRegistry();
            }

            if (GUILayout.Button("Clear Injectable Fields")) {
                injector.ClearDependencies();
                EditorUtility.SetDirty(injector);
            }

            if (GUILayout.Button("Print Registered Dependencies")) {
                PrintRegistered(injector);
            }

            if (!Application.isPlaying) {
                return;
            }

            EditorGUILayout.Space();
            showRegistered = EditorGUILayout.Foldout(showRegistered, $"Registered Dependencies ({injector.Registry.Count})", true);
            if (showRegistered) {
                EditorGUI.indentLevel++;
                foreach (var pair in injector.Registry) {
                    var entry = pair.Value;
                    EditorGUILayout.LabelField(entry.Contract.Name, InstanceLabel(entry));
                }

                EditorGUI.indentLevel--;
            }
        }

        static string InstanceLabel(Injector.DependencyEntry entry) {
            var instance = entry.Instance is Object unity && unity != null ? unity.name : entry.Instance?.ToString() ?? "<null>";
            var owner = entry.Owner != null ? entry.Owner.name : "<none>";
            var scene = entry.Scene.IsValid() ? entry.Scene.name : "<none>";
            return $"{instance} | owner: {owner} | scene: {scene}";
        }

        static void PrintRegistered(Injector injector) {
            if (!Application.isPlaying) {
                Debug.Log("[Injector] Registry is only populated in Play Mode.");
                return;
            }

            foreach (var pair in injector.Registry) {
                var entry = pair.Value;
                Debug.Log($"[Injector] {entry.Contract.Name} -> {InstanceLabel(entry)}");
            }
        }
    }
}
