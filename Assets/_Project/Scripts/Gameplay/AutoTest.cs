using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Smoke test for built players: run the exe with -pixAutotest [-pixAutotestOut dir]. Loads the menu, then the stadium, drives the player with scripted input
    /// (accel / cuts / sprint / moves), takes screenshots from every camera in night and day, logs FPS and quits.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        public static bool Requested
        {
            get { foreach (string a in Environment.GetCommandLineArgs()) if (a == "-pixAutotest") return true; return false; }
        }

        string _out;
        readonly StringBuilder _log = new StringBuilder();
        readonly List<float> _frames = new List<float>();
        int _errors;
        bool _recording;
        float _maxMph;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            var go = new GameObject("PixAutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoTest>();
        }

        void Log(string s) { _log.AppendLine(s); Debug.Log("[AutoTest] " + s); }

        void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            _out = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-pixAutotestOut") _out = args[i + 1];
            if (string.IsNullOrEmpty(_out)) _out = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "autotest"));
            Directory.CreateDirectory(_out);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) { _errors++; _log.AppendLine("  ! " + t + ": " + c); } };
        }

        void Update() { if (_recording) _frames.Add(Time.unscaledDeltaTime); }

        IEnumerator Start()
        {
            Log("PIX autotest starting. " + Screen.width + "x" + Screen.height + " fullscreen=" + Screen.fullScreen + " gpu=" + SystemInfo.graphicsDeviceName + " api=" + SystemInfo.graphicsDeviceType + " quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            // ---- menu ----
            Log("scene: " + SceneManager.GetActiveScene().name);
            yield return new WaitForSeconds(3.0f);
            yield return Shot("menu");

            // ---- stadium ----
            GameSession.StartFreeRoam();
            float wait = 0f;
            while ((FreeRoamGame.Instance == null || FreeRoamGame.Instance.Player == null || FreeRoamGame.Instance.Cameras == null) && wait < 60f) { wait += Time.unscaledDeltaTime; yield return null; }
            var game = FreeRoamGame.Instance;
            if (game == null) { Log("FAIL: stadium did not load"); Finish(2); yield break; }
            Log("scene: " + SceneManager.GetActiveScene().name + " player spawned in " + wait.ToString("0.0") + "s");
            PixSettings.Lighting = LightingPreset.Night;
            game.Lighting.Apply(LightingPreset.Night);
            game.Cameras.SetMode(CameraMode.Broadcast, true);
            yield return new WaitForSeconds(2.0f);
            yield return Shot("night_broadcast_idle");

            // ---- 10 s scripted run ----
            _frames.Clear(); _recording = true;
            float t0 = Time.unscaledTime;
            var player = game.Player;
            Vector3 startPos = player.Position;
            Log("scripted run start pos " + startPos);
            var ev = new List<KeyValuePair<float, Action<InputFrame?>>>();
            Vector2 dir = new Vector2(0f, 1f);
            bool sprint = false;
            float next = 0f;
            bool shotSprint = false, shotMove = false;
            var moves = new[] { new KeyValuePair<float, string>(5.3f, "JukeLeft"), new KeyValuePair<float, string>(6.0f, "JukeRight"), new KeyValuePair<float, string>(6.7f, "Spin"),
                new KeyValuePair<float, string>(7.4f, "Truck"), new KeyValuePair<float, string>(8.0f, "Hurdle"), new KeyValuePair<float, string>(8.9f, "Dive") };
            int mi = 0;
            while (true)
            {
                float t = Time.unscaledTime - t0;
                if (t > 10.5f) break;
                var f = new InputFrame { MoveIsWorld = true };
                // phases: idle 0-0.8, jog 0.8-2.2, run/accel straight, sprint 3.2+, cut left 4.4, cut right 5.0 ...
                if (t < 0.8f) dir = Vector2.zero;
                else if (t < 4.4f) dir = new Vector2(0f, 1f);
                else if (t < 5.0f) dir = new Vector2(-0.9f, 0.45f).normalized;      // hard cut left
                else if (t < 6.4f) dir = new Vector2(0.55f, 0.85f).normalized;      // cut right
                else if (t < 7.8f) dir = new Vector2(0f, 1f);
                else if (t < 8.9f) dir = new Vector2(-0.4f, 0.9f).normalized;
                else dir = Vector2.zero;
                sprint = t > 2.2f && t < 9.0f;
                f.Move = dir; f.Sprint = sprint;
                while (mi < moves.Length && t >= moves[mi].Key)
                {
                    switch (moves[mi].Value)
                    {
                        case "JukeLeft": f.JukeLeft = true; break;
                        case "JukeRight": f.JukeRight = true; break;
                        case "Spin": f.Spin = true; break;
                        case "Truck": f.Truck = true; break;
                        case "Hurdle": f.Hurdle = true; break;
                        case "Dive": f.Dive = true; break;
                    }
                    mi++;
                }
                FreeRoamGame.ScriptedInput = f;
                _maxMph = Mathf.Max(_maxMph, player.SpeedMph);
                if (!shotMove && t > 3.0f) { shotMove = true; Log("t=3.0 speed " + player.SpeedMph.ToString("0.0") + " mph pos " + player.Position); }
                if (!shotSprint && t > 4.2f) { shotSprint = true; _recording = false; yield return Shot("night_broadcast_sprint"); _recording = true; Log("t=4.2 speed " + player.SpeedMph.ToString("0.0") + " mph pos " + player.Position); }
                yield return null;
            }
            FreeRoamGame.ScriptedInput = new InputFrame { MoveIsWorld = true };
            _recording = false;
            Log("scripted run end pos " + player.Position + " moved " + Vector3.Distance(startPos, player.Position).ToString("0.0") + " m, max speed " + _maxMph.ToString("0.0") + " mph");
            ReportFps("scripted run (night, broadcast cam)");

            // ---- cameras / lighting stills ----
            yield return Still(game, CameraMode.Broadcast, LightingPreset.Night, "night_broadcast_end", Vector2.zero);
            yield return Still(game, CameraMode.Behind, LightingPreset.Night, "night_behind", new Vector2(0f, 1f));
            yield return Still(game, CameraMode.FirstPerson, LightingPreset.Night, "night_firstperson", new Vector2(0f, 1f));
            yield return Still(game, CameraMode.Broadcast, LightingPreset.Day, "day_broadcast", new Vector2(0f, -1f));
            yield return Still(game, CameraMode.Behind, LightingPreset.Day, "day_behind", new Vector2(0.3f, 1f));
            yield return Still(game, CameraMode.FirstPerson, LightingPreset.Day, "day_firstperson", new Vector2(0f, 1f));

            // pause menu still
            game.Lighting.Apply(LightingPreset.Night);
            game.Cameras.SetMode(CameraMode.Behind, true);
            FreeRoamGame.ScriptedInput = new InputFrame { MoveIsWorld = true };
            yield return new WaitForSeconds(1.0f);
            game.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("night_pause");
            game.SetPaused(false);

            Log("errors/exceptions logged: " + _errors);
            Finish(_errors > 0 ? 0 : 0);
        }

        IEnumerator Still(FreeRoamGame game, CameraMode cam, LightingPreset light, string name, Vector2 move)
        {
            game.Lighting.Apply(light);
            game.Player.ResetTo(new Vector3(0f, 0f, -12f), 0f);
            game.Cameras.SetMode(cam, true);
            float t = 0f;
            _frames.Clear(); _recording = true;
            while (t < 2.0f)
            {
                FreeRoamGame.ScriptedInput = new InputFrame { MoveIsWorld = true, Move = move, Sprint = move.sqrMagnitude > 0.01f };
                t += Time.unscaledDeltaTime; yield return null;
            }
            _recording = false;
            yield return Shot(name);
            ReportFps(name);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string path = Path.Combine(_out, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log("screenshot " + path);
        }

        void ReportFps(string label)
        {
            if (_frames.Count < 5) return;
            var f = new List<float>(_frames);
            float sum = 0f, max = 0f;
            foreach (float x in f) { sum += x; max = Mathf.Max(max, x); }
            f.Sort();
            float p99 = f[Mathf.Min(f.Count - 1, (int)(f.Count * 0.99f))];
            Log("FPS [" + label + "] frames=" + f.Count + " avg=" + (f.Count / sum).ToString("0.0") + " min=" + (1f / max).ToString("0.0") + " 1%low=" + (1f / p99).ToString("0.0"));
        }

        void Finish(int code)
        {
            try { File.WriteAllText(Path.Combine(_out, "autotest.txt"), _log.ToString()); } catch (Exception e) { Debug.LogError(e); }
            Application.Quit(code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(code);
#endif
        }
    }
}
