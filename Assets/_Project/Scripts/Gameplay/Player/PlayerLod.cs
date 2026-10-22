using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.4 performance: the match player carries a 26,000 vertex helmet shell (plus face mask and visor) that was always drawn at full detail, 22 times, into every shadow cascade.
    /// The authored LOD01 / LOD02 meshes of the gear (8,000 / 4,000 vertices) are picked by the distance to the camera instead.
    /// </summary>
    public sealed class PlayerLod
    {
        /// <summary>The gear groups (helmet shell, face mask, visor): each holds its LOD renderers from the most to the least detailed.</summary>
        readonly List<Renderer[]> _groups = new List<Renderer[]>();
        readonly List<bool> _skip = new List<bool>();
        readonly List<SkinnedMeshRenderer> _bodies = new List<SkinnedMeshRenderer>();
        readonly List<Mesh[]> _bodyMeshes = new List<Mesh[]>();
        readonly List<Renderer> _farHidden = new List<Renderer>();
        int _level = -1;
        public static Camera ViewCamera;
        /// <summary>Distances (metres) at which the level steps down: below [0] full detail (LOD0), below [1] LOD1, below [2] LOD2, beyond LOD3 (v0.5.x: four Blender-decimated body levels, silhouette preserved).</summary>
        public static float Near = 11f, Mid = 26f, Far = 60f;
        /// <summary>1 = default; the quality tiers push the thresholds closer (0.5) or further (2).</summary>
        public static float Bias = 1f;

        public int Level { get { return _level; } }

        public PlayerLod(Transform headBone)
        {
            if (headBone == null) return;
            var holder = headBone.Find("Helmet");
            if (holder == null) return;
            foreach (Transform gear in holder)
            {
                var rs = gear.GetComponentsInChildren<Renderer>(true).OrderBy(r => r.name).ToArray();
                if (rs.Length > 0) { _groups.Add(rs); _skip.Add(gear.name == "Visor"); }
            }
        }

        public bool HasLods { get { if (_photo != null || _bodies.Count > 0) return true; foreach (var g in _groups) if (g.Length > 1) return true; return false; } }

        PixFootball.Art.PhotoPlayer _photo;

        /// <summary>v0.6: the photoreal body switches all its parts (body, head, hair, helmet gear) itself; this level of detail only decides the level and applies it with hysteresis.</summary>
        public void AttachPhoto(PixFootball.Art.PhotoPlayer photo) { _photo = photo; }

        /// <summary>A renderer that is not visible from far away at all (the eyes behind the face mask): switched off at the lowest level.</summary>
        public void AddFarHidden(Renderer r) { if (r != null) _farHidden.Add(r); }

        /// <summary>Registers a skinned body part that has simplified meshes in the PlayerMeshLodTable.</summary>
        public void AddSkinned(SkinnedMeshRenderer smr)
        {
            var t = PixFootball.Art.PlayerMeshLodTable.Load();
            var e = t != null && smr != null ? t.Find(smr.sharedMesh) : null;
            if (e == null || e.lod1 == null || e.lod2 == null) return;
            _bodies.Add(smr); _bodyMeshes.Add(new[] { smr.sharedMesh, e.lod1, e.lod2, e.lod3 != null ? e.lod3 : e.lod2 });
        }

        /// <summary>Level for a distance with ±10 percent hysteresis around the thresholds relative to the level in use (no popping while a player stands on a threshold).</summary>
        public static int LevelFor(float distance, int current)
        {
            int lv = LevelFor(distance);
            if (current < 0 || lv == current) return lv;
            float b = Mathf.Max(0.1f, Bias);
            float[] edge = { Near * b, Mid * b, Far * b };       // edge[i] separates level i from i+1
            if (lv > current)       // moving away: stay at the finer level until 10 percent past the edge
            { for (int i = current; i < lv; i++) if (distance < edge[i] * 1.10f) return i; }
            else                    // moving closer: stay at the coarser level until 10 percent inside the edge
            { for (int i = current - 1; i >= lv; i--) if (distance > edge[i] * 0.90f) return i + 1; }
            return lv;
        }

        public static int LevelFor(float distance)
        {
            float b = Mathf.Max(0.1f, Bias);
            if (distance < Near * b) return 0;
            if (distance < Mid * b) return 1;
            if (distance < Far * b) return 2;
            return 3;
        }

        public void Apply(int level)
        {
            if (level == _level) return;
            _level = level;
            if (_photo != null) { _photo.SetLevel(level); return; }
            for (int i = 0; i < _bodies.Count; i++) { if (_bodies[i] != null) _bodies[i].sharedMesh = _bodyMeshes[i][Mathf.Clamp(level, 0, 3)]; }
            foreach (var r in _farHidden) if (r != null) r.enabled = level < 2;
            for (int gi = 0; gi < _groups.Count; gi++)
            {
                if (_skip[gi]) continue;   // the visor is hidden by the prefab: leave its state alone
                var g = _groups[gi];
                int pick = Mathf.Min(level, g.Length - 1);
                for (int i = 0; i < g.Length; i++) g[i].gameObject.SetActive(i == pick);
            }
        }

        public void Update(Vector3 position)
        {
            var cam = ViewCamera;
            if (_photo == null && _groups.Count == 0 && _bodies.Count == 0) return;
            if (cam == null) { if (_level < 0) Apply(0); return; }   // free roam / before the director set the camera: never leave every helmet LOD active at once
            Apply(LevelFor(Vector3.Distance(cam.transform.position, position), _level));
        }
    }
}
