using System.IO;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;
namespace PixFootball.EditorTools
{
    /// <summary>QA-1054: writes Logs/cycle_speeds.txt for every locomotion / carrier cycle key of the library: root-motion speed (clip.averageSpeed) next to the measured stride speed (planted foot) and the ratio the foot matching has to absorb.</summary>
    public static class CycleSpeeds
    {
        static readonly string[] Keys =
        {
            ArtKeys.Idle, ArtKeys.Walk, ArtKeys.Jog, ArtKeys.Run, ArtKeys.Sprint, ArtKeys.Backpedal, ArtKeys.StrafeLeft, ArtKeys.StrafeRight, ArtKeys.ShuffleLeft, ArtKeys.ShuffleRight,
            ArtKeys.BcJog, ArtKeys.BcRun, ArtKeys.BcSprint, ArtKeys.BcCircle, ArtKeys.SprintAlt, ArtKeys.CbShuffle, ArtKeys.CbSprint, ArtKeys.OlPocketShuffle, ArtKeys.OlPocketStep
        };

        public static void Run()
        {
            BatchWatchdog.Arm(20);
            var lib = ArtLibrary.Load(); if (lib == null) return;
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var idle = lib.Clip(ArtKeys.Idle);
            var idlePose = idle != null ? rig.PoseAt(idle, 0.5f) : default(AnimRig.Frame);
            var sb = new StringBuilder("key,clip,length,loop,rootSpeed,strideSpeed,ratio,travelAngle,flag\n");
            int bad = 0;
            foreach (string k in Keys)
                foreach (var c in lib.ClipVariants(k))
                {
                    var r = AnimAudit.Analyse(rig, c, idlePose);
                    float ratio = r.stanceSpeed > 0.3f ? r.speed / r.stanceSpeed : 0f;
                    string flag = ratio > 1.35f || (ratio > 0f && ratio < 0.75f) ? "SKATE" : "";
                    if (flag != "") bad++;
                    sb.AppendLine(k + "," + c.name + "," + r.length.ToString("0.00") + "," + (c.isLooping ? 1 : 0) + "," + r.speed.ToString("0.00") + "," + r.stanceSpeed.ToString("0.00") + "," + ratio.ToString("0.00") + "," + r.travelAngle.ToString("0") + "," + flag);
                }
            rig.Dispose();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/cycle_speeds.txt", sb.ToString());
            Debug.Log("[CycleSpeeds] written Logs/cycle_speeds.txt, clips whose root speed disagrees with the stride speed: " + bad);
        }
    }
}
