using System;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.1 Free Roam: spawns the controllable player, the camera rig and stadium audio, polls input and handles pause / camera / lighting.
    /// Lives in the Stadium_Pro scene.
    /// </summary>
    public class FreeRoamGame : MonoBehaviour
    {
        public static FreeRoamGame Instance { get; private set; }
        /// <summary>When non-null the autotest drives the player instead of devices.</summary>
        public static InputFrame? ScriptedInput;

        public Camera MainCamera;
        public LightingController Lighting;
        public Vector3 SpawnPosition = new Vector3(0f, 0f, -30f);
        public float SpawnYaw = 0f;

        public PlayerController Player { get; private set; }
        public TeamInfo Team { get; private set; }
        public event Action<TeamInfo> TeamChanged;

        public void SetTeam(TeamInfo t)
        {
            if (t == null) return;
            Team = t; TeamCatalog.SavedTeamId = t.Id; PlayerPrefs.Save();
            if (Player != null && Player.Avatar != null) Player.Avatar.SetColors(t.Colors);
            if (TeamChanged != null) TeamChanged(t);
        }
        public CameraRig Cameras { get; private set; }
        public StadiumAudio Audio { get; private set; }
        public event Action<bool> PauseChanged;
        public bool Paused { get { return GameSession.Paused; } }

        void Awake()
        {
            Instance = this;
            GameSession.SetPaused(false);
        }

        void Start()
        {
            if (MainCamera == null) MainCamera = Camera.main;
            if (Lighting != null) Lighting.Apply(PixSettings.Lighting);
            Team = TeamCatalog.ById(TeamCatalog.SavedTeamId);
            Player = PlayerController.Spawn(transform, SpawnPosition, SpawnYaw, Team != null ? Team.Colors : TeamColors.Home);
            Cameras = CameraRig.Create(transform, MainCamera, Player);
            Audio = gameObject.AddComponent<StadiumAudio>();
            Audio.Init(Player);
        }

        void OnDestroy() { if (Instance == this) Instance = null; GameSession.SetPaused(false); }

        public void SetPaused(bool p)
        {
            if (GameSession.Paused == p) return;
            GameSession.SetPaused(p);
            if (PauseChanged != null) PauseChanged(p);
        }

        void Update()
        {
            if (Player == null) return;
            bool fp = Cameras != null && Cameras.Mode == CameraMode.FirstPerson;
            InputFrame f = ScriptedInput.HasValue ? ScriptedInput.Value : PixInput.Instance.Poll(Time.unscaledDeltaTime, !fp);

            if (f.Pause) SetPaused(!Paused);
            if (Paused) return;

            if (f.CycleCamera && Cameras != null) { Cameras.Cycle(); PixSettings.DefaultCamera = Cameras.Mode; }
            if (f.ToggleLighting && Lighting != null) Lighting.Toggle();
            if (f.ResetPlayer) Player.ResetTo(SpawnPosition, SpawnYaw);
            Player.Tick(f, Time.deltaTime);
            if (Audio != null) Audio.OnPlayerMove(Player.CurrentMove);
        }
    }
}
