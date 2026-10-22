using System;
using System.Collections.Generic;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// A live portrait of our photoreal player prefab: a hidden mini stage (player, two spot lights, a local fixed-exposure volume) far from the playfield and a camera that renders it into a
    /// RenderTexture with a transparent background. Used by the player card and the menu hero; destroy it with <see cref="Dispose"/>.
    /// </summary>
    public sealed class GlassPortraitStage : MonoBehaviour
    {
        public RenderTexture Target { get; private set; }
        public Camera Cam { get; private set; }
        public SimAgentView View { get; private set; }
        Transform _turn;
        float _yaw = 200f;
        /// <summary>Degrees per second of the slow turntable.</summary>
        public float TurnSpeed = 14f;

        static int _count;

        public static GlassPortraitStage Create(TeamColors colors, int width = 640, int height = 800, PlayerSize size = PlayerSize.Medium, float yaw = 200f)
        {
            var go = new GameObject("GlassPortraitStage") { hideFlags = HideFlags.DontSave };
            go.transform.position = new Vector3(4000f + _count++ * 40f, -300f, 4000f);
            var st = go.AddComponent<GlassPortraitStage>();
            st.Build(colors, width, height, size, yaw);
            return st;
        }

        void Build(TeamColors colors, int width, int height, PlayerSize size, float yaw)
        {
            _yaw = yaw;
            var pivot = new GameObject("Turntable").transform; pivot.SetParent(transform, false); _turn = pivot;
            View = SimAgentView.Create(pivot, size, colors, "PortraitPlayer");
            View.transform.localPosition = Vector3.zero; View.transform.localRotation = Quaternion.identity;
            pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            // lights: a warm key from the front-left, a cool rim from behind (the menu stage recipe), limited range so the real scene is never lit
            MakeLight("Key", new Vector3(-1.8f, 2.6f, 3.2f), new Vector3(1.8f, -1.4f, -3.2f), 62000f, new Color(1f, 0.94f, 0.86f), 52f);
            MakeLight("Rim", new Vector3(2.4f, 2.4f, -2.6f), new Vector3(-2.4f, -1.2f, 2.6f), 52000f, new Color(0.62f, 0.74f, 1f), 55f);
            MakeLight("Fill", new Vector3(2.2f, 1.2f, 3.0f), new Vector3(-2.2f, -0.2f, -3.0f), 16000f, new Color(0.85f, 0.88f, 1f), 70f);

            // a local volume at the stage: fixed exposure and no bloom surprises, so the portrait looks the same in the menu, the stadium and the match scene
            var vgo = new GameObject("StageVolume"); vgo.transform.SetParent(transform, false);
            var col = vgo.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(20f, 20f, 20f);
            var vol = vgo.AddComponent<Volume>(); vol.isGlobal = false; vol.priority = 500f; vol.blendDistance = 0f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var exp = prof.Add<Exposure>(); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(8.6f);
            var tm = prof.Add<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            vol.sharedProfile = prof;

            Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "GlassPortrait", antiAliasing = 1, hideFlags = HideFlags.DontSave };
            Target.Create();
            var cgo = new GameObject("PortraitCam"); cgo.transform.SetParent(transform, false);
            Cam = cgo.AddComponent<Camera>();
            var hd = cgo.AddComponent<HDAdditionalCameraData>();
            hd.volumeAnchorOverride = vgo.transform;
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = new Color(0f, 0f, 0f, 0f);
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            Cam.fieldOfView = 24f; Cam.nearClipPlane = 0.1f; Cam.farClipPlane = 30f; Cam.allowHDR = false;
            Cam.targetTexture = Target;
            Cam.transform.localPosition = new Vector3(0f, 1.25f, 5.6f); Cam.transform.LookAt(transform.position + new Vector3(0f, 1.1f, 0f));
            Cam.depth = -50f;
            Cam.enabled = Application.isPlaying;   // in play mode it renders every frame; edit-mode tools call Render()
        }

        void MakeLight(string name, Vector3 pos, Vector3 dir, float candela, Color col, float angle)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.transform.localPosition = pos; go.transform.localRotation = Quaternion.LookRotation(dir);
            var l = go.AddComponent<Light>(); l.type = LightType.Spot; l.color = col; l.spotAngle = angle; l.range = 14f; l.shadows = LightShadows.None;
            var hd = go.AddComponent<HDAdditionalLightData>();
            hd.SetIntensity(candela, LightUnit.Candela);
            hd.SetSpotAngle(angle, 70f);
        }

        void Update()
        {
            if (!Application.isPlaying || _turn == null) return;
            _yaw += TurnSpeed * Time.unscaledDeltaTime;
            _turn.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Renders one frame now (edit-mode tools, where cameras do not run by themselves).</summary>
        public void RenderNow() { if (Cam != null) { Cam.Render(); } }

        public void Dispose()
        {
            if (Target != null) { Target.Release(); if (Application.isPlaying) Destroy(Target); else DestroyImmediate(Target); }
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        void OnDestroy() { if (Target != null) Target.Release(); }
    }
}

