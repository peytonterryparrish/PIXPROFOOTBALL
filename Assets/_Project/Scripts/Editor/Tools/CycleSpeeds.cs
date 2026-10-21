using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace PixFootball.EditorTools
{
    public static class CycleSpeeds
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            foreach (var n in new[] { "JogCycleJR", "JogCycleLeon_1", "RunCycleJR_1", "RunCycleJR_2", "RunCycleLeon_1", "RunCycleLeon_2", "SprintCycleJR_1", "SprintCycleJR_2", "SprintCycleLeon_1", "SprintCycleLeon_2", "QuarterbackShuffleBkwdLhand", "QuarterbackShuffleLeftLhand", "QuarterbackShuffleRightLhand", "Player_Standing_Idle" })
            {
                var c = AnimatorControllerBuilder.FindClipByName(n);
                sb.AppendLine(n + " " + (c != null ? "len=" + c.length.ToString("0.00") + " avg=" + c.averageSpeed + " |v|=" + c.averageSpeed.magnitude.ToString("0.00") + " loop=" + c.isLooping : "MISSING"));
            }
            File.WriteAllText("Logs/cycle_speeds.txt", sb.ToString());
        }
    }
}
