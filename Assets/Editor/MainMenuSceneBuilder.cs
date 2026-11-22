// MainMenuSceneBuilder.cs —— 编辑器侧场景装配工具。
// 菜单：工具/重建主菜单场景、工具/打开主菜单场景。
// 生成的场景只放一个「MainMenu」空物体（挂 MainMenuController）+ EventSystem，
// 其余 UI（影片、金属条、按钮列、选项面板）全部由 MainMenuController 在运行时构建。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Ra2md.Menu;

namespace Ra2md.EditorTools
{
    public static class MainMenuSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";

        [MenuItem("工具/打开主菜单场景")]
        public static void OpenScene()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                RebuildScene();
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        [MenuItem("工具/重建主菜单场景")]
        public static void RebuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RebuildCore();
        }

        /// <summary>批处理入口：不弹任何对话框，直接重建并保存场景。</summary>
        public static void RebuildFromBatch()
        {
            RebuildCore();
        }

        private static void RebuildCore()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // UGUI 的按钮点击必须有 EventSystem，否则整个界面点不动
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<EventSystem>();
                eventSystemGo.AddComponent<StandaloneInputModule>();
            }

            var root = new GameObject("MainMenu");
            root.AddComponent<MainMenuController>();

            string dir = System.IO.Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("[菜单] 已生成场景 " + ScenePath);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
        }
    }
}
