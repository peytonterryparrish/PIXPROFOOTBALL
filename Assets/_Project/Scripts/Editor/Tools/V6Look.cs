using System.Collections.Generic;
using System.IO;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>Scene-build helpers of the v0.6 look: swaps the stadium onto PIX/Lit (weather aware turf, markings and surfaces) and adds the reflection probes.</summary>
    public static class V6Look
    {
        const string WxDir = V6ArtBuilder.MatDir + "/WX";

        /// <summary>Materials whose wetness / snow response is reduced (under the roof, vertical boards, glass).</summary>
        static void Tune(string name, Material m)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("glass") || n.Contains("window")) { m.SetFloat("_PixWetAmount", 0.3f); m.SetFloat("_PixSnowAmount", 0f); }
            else if (n.Contains("concrete") || n.Contains("stadium")) { m.SetFloat("_PixWetAmount", 0.8f); m.SetFloat("_PixSnowAmount", 0.55f); }
            else if (n.Contains("banner") || n.Contains("board") || n.Contains("ad")) { m.SetFloat("_PixWetAmount", 0.5f); m.SetFloat("_PixSnowAmount", 0.15f); }
        }

        public static int UpgradeStadium(GameObject stadium)
        {
            var assets = AssetDatabase.LoadAssetAtPath<PixLookAssets>(V6ArtBuilder.LookAssetsPath);
            var pix = Shader.Find("PIX/Lit");
            if (assets == null || pix == null) { Debug.LogWarning("[V6] look assets / PIX/Lit missing - stadium not upgraded (run PIX Football/V6/Build All V6 Art)"); return 0; }
            MaterialFactory.EnsureFolder(WxDir);
            var map = new Dictionary<Material, Material>();
            int swapped = 0;
            foreach (var r in stadium.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (m == null) continue;
                    Material to;
                    if (!map.TryGetValue(m, out to))
                    {
                        to = null;
                        if (m.name == "PIX_FieldGrass") to = assets.turfMaterial;
                        else if (m.name == "PIX_FieldMarkings") to = assets.markingsMaterial;
                        else if (m.shader != null && m.shader.name == "HDRP/Lit" && !m.name.StartsWith("PIX_Uniform"))
                        {
                            string path = WxDir + "/WX_" + Sanitize(m.name) + "_" + (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m)) ?? "x").Substring(0, 6) + ".mat";
                            to = V6ArtBuilder.CloneToPix(m, path, 0);
                            Tune(m.name, to);
                            EditorUtility.SetDirty(to);
                        }
                        map[m] = to;
                    }
                    if (to != null && to != m) { mats[i] = to; dirty = true; swapped++; }
                }
                if (dirty) r.sharedMaterials = mats;
            }
            Debug.Log("[V6] stadium materials moved to PIX/Lit: " + swapped + " slots, " + map.Count + " materials");
            return swapped;
        }

        static string Sanitize(string s) { foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }

        /// <summary>One real-time reflection probe over the pitch (floodlights, stands and the wet turf reflect in it) plus one high above for the sky.</summary>
        public static void AddReflectionProbes()
        {
            var root = new GameObject("ReflectionProbes");
            MakeProbe(root.transform, "Probe_Pitch", new Vector3(0f, 6f, 0f), new Vector3(150f, 44f, 190f), ProbeSettings.RealtimeMode.OnDemand);
        }

        static void MakeProbe(Transform parent, string name, Vector3 pos, Vector3 size, ProbeSettings.RealtimeMode rt)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var rp = go.AddComponent<ReflectionProbe>();
            var hd = go.AddComponent<HDAdditionalReflectionData>();
            hd.influenceVolume.shape = InfluenceShape.Box;
            hd.influenceVolume.boxSize = size;
            hd.influenceVolume.boxBlendDistancePositive = new Vector3(14f, 6f, 14f);
            hd.influenceVolume.boxBlendDistanceNegative = new Vector3(14f, 6f, 14f);
            hd.mode = ProbeSettings.Mode.Realtime;
            hd.realtimeMode = rt;
            rp.mode = ReflectionProbeMode.Realtime;
            rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            rp.nearClipPlane = 0.3f; rp.farClipPlane = 400f;
        }
    }
}
