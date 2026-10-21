using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Simplified copies of the skinned player body meshes (torso, legs, arms, hands, shoes) built offline by PlayerMeshLodBuilder (vertex clustering, bone weights kept), picked by
    /// distance at runtime. The hands and shoes alone are 11,000 vertices per player and cost 3 ms of GPU time for 22 players at 2560x1440 (micro triangles).
    /// </summary>
    public class PlayerMeshLodTable : ScriptableObject
    {
        public const string ResourcesName = "PixMeshLods";
        public const string EditorPath = "Assets/_Licensed/Resources/PixMeshLods.asset";

        [Serializable] public class Entry { public string key; public Mesh lod1, lod2; }
        public List<Entry> entries = new List<Entry>();
        Dictionary<string, Entry> _map;
        static PlayerMeshLodTable _inst; static bool _tried;

        public static string KeyFor(Mesh m) { return m == null ? "" : m.name + "#" + m.vertexCount; }

        public static PlayerMeshLodTable Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerMeshLodTable>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<PlayerMeshLodTable>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        public Entry Find(Mesh m)
        {
            if (_map == null) { _map = new Dictionary<string, Entry>(); foreach (var e in entries) if (e != null && !string.IsNullOrEmpty(e.key)) _map[e.key] = e; }
            Entry r; return m != null && _map.TryGetValue(KeyFor(m), out r) ? r : null;
        }
    }
}
