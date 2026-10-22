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
        public static float Near = 9f, Mid = 22f, Far = 55f;   // v0.7: LOD0 is a 70 k triangle body: the 1 % lows were close-ups of several LOD0 players under the night lights (v0.6: 11 / 26 / 60)
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
            float e0 = Near * b, e1 = Mid * b, e2 = Far * b;   // edge(i) separates level i from i+1 (v0.9.1: no array per call, this runs for every player every frame)
            if (lv > current)       // moving away: stay at the finer level until 10 percent past the edge
            { for (int i = current; i < lv; i++) if (distance < Edge(i, e0, e1, e2) * 1.10f) return i; }
            else                    // moving closer: stay at the coarser level until 10 percent inside the edge
            { for (int i = current - 1; i >= lv; i--) if (distance > Edge(i, e0, e1, e2) * 0.90f) return i + 1; }
            return lv;
        }

        static float Edge(int i, float e0, float e1, float e2) { return i == 0 ? e0 : i == 1 ? e1 : e2; }

        public static int LevelFor(float distance)
        {
            float b = Mathf.Max(0.1f, Bias);
            if (distance < Near * b) return 0;
            if (distance < Mid * b) return 1;
            if (distance < Far * b) return 2;
            return 3;
        }

        /// <summary>Level changes since start (autotest spike log).</summary>
        public static int Switches;

        public void Apply(int level)
        {
            if (level == _level) return;
            _level = level;
            Switches++;
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

        // ---- v0.9 (GFX-404 / 389 / 405): the level follows the PROJECTED height of the player on screen instead of the distance, so the Classic camera (19-30 m to the players around the ball) shows LOD0 there
        /// <summary>Height of the player in pixels at a 1440 p screen above which each level is used (LOD0 / LOD1 / LOD2; below the last: LOD3). A 1.9 m player is about 3225 / distance px at the 46 degree Classic field of view (LOD0 inside about 11 m, LOD1 to 21 m, LOD2 to 118 m; High adds the 1.1 tier bias).</summary>
        public static float Lod0Px = 400f, Lod1Px = 200f, Lod2Px = 30f;   // v0.9.0 release fallback (player measured 73 avg / 51.5 1 %-low at 300 / 170): LOD0 from 400 px, LOD1 from 200 px. Measured A/B (80 s of live play, 22 players, High, 1440 p, editor; old distance LODs 112 fps): 110 / 62 px = 77 fps, 190 / 115 = 87-94, 240 / 150 = 89-102, 300 / 170 = 106, 400 / 200 = 115. LOD1 for everybody inside 30-50 m is what costs. From 300 px (about 11 m at the Classic framing) every carrier seen by the follow camera and every close-up gets LOD0; from 170 px (about 21 m) the line of scrimmage and the quarterback get LOD1 (they were LOD2 beyond 22 m)
        public const float PlayerHeightM = 1.9f;
        /// <summary>true: projected size (default); false: the old distance thresholds (Near / Mid / Far, autotest A/B).</summary>
        public static bool UseScreenSize = true;
        /// <summary>Hysteresis band around each edge (20 percent of the size).</summary>
        public const float Hysteresis = 0.20f;

        /// <summary>Size of a player in 1440 p pixels for a camera distance (pure; the camera parts are arguments so tests and tools can call it).</summary>
        public static float ScreenPixels(float distance, float verticalFovDegrees, float pixelHeight)
        {
            float d = Mathf.Max(0.5f, distance);
            float tan = Mathf.Tan(Mathf.Clamp(verticalFovDegrees, 5f, 120f) * 0.5f * Mathf.Deg2Rad);
            float frac = PlayerHeightM / (2f * d * tan);
            return frac * 1440f * Mathf.Clamp(pixelHeight, 720f, 2160f) / 1440f * Mathf.Max(0.1f, Bias);
        }

        /// <summary>Level for a projected size with hysteresis relative to the level in use.</summary>
        public static int LevelForPixels(float px, int current)
        {
            // edge(i) separates level i (larger) from i+1
            int lv = px >= Lod0Px ? 0 : px >= Lod1Px ? 1 : px >= Lod2Px ? 2 : 3;
            if (current < 0 || lv == current) return lv;
            if (lv > current) { for (int i = current; i < lv; i++) if (px > Edge(i, Lod0Px, Lod1Px, Lod2Px) * (1f - Hysteresis)) return i; }     // shrinking: keep the finer level until 20 percent below the edge
            else { for (int i = current - 1; i >= lv; i--) if (px < Edge(i, Lod0Px, Lod1Px, Lod2Px) * (1f + Hysteresis)) return i + 1; }  // growing: keep the coarser level until 20 percent above the edge
            return lv;
        }

        public void Update(Vector3 position)
        {
            var cam = ViewCamera;
            if (_photo == null && _groups.Count == 0 && _bodies.Count == 0) return;
            if (cam == null) { if (_level < 0) Apply(0); return; }   // free roam / before the director set the camera: never leave every helmet LOD active at once
            float dist = Vector3.Distance(cam.transform.position, position);
            if (UseScreenSize) Apply(LevelForPixels(ScreenPixels(dist, cam.fieldOfView, cam.pixelHeight), _level));
            else Apply(LevelFor(dist, _level));
        }
    }
}
