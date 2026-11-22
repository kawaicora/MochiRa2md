// MainMenuAutoSetup.cs —— 脚本重编译后自动补齐主菜单场景缺的运行时设施。
// 目前只做一件事：若当前打开的就是 Assets/Scenes/MainMenu.unity 且缺 EventSystem，
// 自动补上（UGUI 按钮没有 EventSystem 完全点不动，这是上一版「按钮没反应」的根因）。
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Ra2md.EditorTools
{
    [InitializeOnLoad]
    public static class MainMenuAutoSetup
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";

        static MainMenuAutoSetup()
        {
            EditorApplication.delayCall += TryFixActiveScene;
        }

        private static void TryFixActiveScene()
        {
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path)) return;
            if (Path.GetFullPath(active.path).Replace('\\', '/')
                != Path.GetFullPath(ScenePath).Replace('\\', '/')) return;

            if (Object.FindObjectOfType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            EditorSceneManager.MarkSceneDirty(active);
            Debug.Log("[菜单] 当前主菜单场景缺少 EventSystem，已自动补齐（请 Ctrl+S 保存场景）");
        }
    }
}
