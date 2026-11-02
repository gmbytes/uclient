using System;
using System.Linq;
using System.Reflection;
using Client.Bootstrap;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

namespace Client.Editor.PlayMode
{
    /// <summary>Composes the checked-in scene for Editor Play; Player trust and slot startup remain native-owned.</summary>
    [InitializeOnLoad]
    static class EditorBootstrapStartup
    {
        const string BootstrapScene = "Assets/Scenes/Bootstrap.unity";

        static EditorBootstrapStartup()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isPlaying || scene.path != BootstrapScene) return;
            var bootstrap = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ClientBootstrap>(true)).SingleOrDefault();
            if (bootstrap == null || bootstrap.GetComponent<NativeClientHost>() != null) return;

            // Disable before Start: this scene has no baked native identity or coordinator.
            bootstrap.enabled = false;
            var framework = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BaseComponent>(true)).Single();
            framework.EditorResourceMode = true;
            if (framework.GetComponent<EditorResourceComponent>() == null) framework.gameObject.AddComponent<EditorResourceComponent>();
            if (framework.GetComponent<ResourceComponent>() == null) framework.gameObject.AddComponent<ResourceComponent>();
            if (framework.GetComponent<ObjectPoolComponent>() == null) framework.gameObject.AddComponent<ObjectPoolComponent>();
            var ui = framework.GetComponent<UIComponent>() ?? framework.gameObject.AddComponent<UIComponent>();
            // A newly added GF UIComponent has a null group array; serialized size zero alone
            // does not materialize it. Groups are subsequently created by the shared UIService.
            typeof(UIComponent).GetField("m_UIGroups", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ui, Array.CreateInstance(typeof(UIComponent).GetNestedType("UIGroup", BindingFlags.NonPublic), 0));

            new EditorClientHost(framework, scene);
        }
    }
}
