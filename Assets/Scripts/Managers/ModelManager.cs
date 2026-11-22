// ModelManager.cs —— 模型管理器：后续 FBX / 预制体单位建筑的放置与阴影统一归口。
//
// 为什么单列：体素（VXL）与 SHP 都是 2D 光栅图；FBX 是真 3D，放法、遮挡、阴影口径不同。
// 等距视角下模型直接摆在地图格上，靠 Unity 原生阴影，不再走软件深度缓冲。
//
// 现状：3D 相机与世界坐标尚未接，这里先做注册表 + 阴影开关口径；PlaceModel 留位，
// 不伪造摆放结果。接 3D 世界时只改本类，不动 SHP/VXL 两条 2D 链路。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ra2md.Managers
{
    public sealed class ModelManager : MonoBehaviour
    {
        private static ModelManager _instance;

        private readonly Dictionary<int, GameObject> _placed = new Dictionary<int, GameObject>();
        private int _nextId;

        public static ModelManager Instance
        {
            get
            {
                if (_instance == null) Ensure();
                return _instance;
            }
        }

        public static ModelManager Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Ra2md.ModelManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ModelManager>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// 把预制体放到地图格上。返回实例；3D 世界接通前先注册并开好阴影。
        /// </summary>
        public GameObject PlaceModel(GameObject prefab, int cellX, int cellY, int level)
        {
            if (prefab == null) return null;

            var instance = Object.Instantiate(prefab, transform, false);
            EnableShadows(instance);

            var id = ++_nextId;
            _placed[id] = instance;
            Core.BootLog.Log($"[模型] 放置 FBX 实例 #{id} 于 ({cellX},{cellY}) level={level}");
            return instance;
        }

        public void RemoveModel(GameObject instance)
        {
            if (instance == null) return;
            foreach (var pair in _placed)
            {
                if (pair.Value == instance)
                {
                    _placed.Remove(pair.Key);
                    break;
                }
            }
            Object.Destroy(instance);
        }

        /// <summary>模型默认投影 + 接收阴影；材质/灯光后续按阵营与剧场再调。</summary>
        private static void EnableShadows(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
                renderers[i].receiveShadows = true;
            }
        }
    }
}
