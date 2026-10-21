using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Core;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Renders every play's diagram to PNG atlas pages in Docs/Playbook (git-ignored) so routes and blocks can be reviewed by eye.</summary>
    public static class PlaybookAtlas
    {
        public const string OutDir = "Docs/Playbook";

        [MenuItem("PIX Football/Tools/Render Playbook Atlas")]
        public static void Render()
        {
            Directory.CreateDirectory(OutDir);
            foreach (string f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);
            Playbook book = DefaultPlaybooks.Create();
            var notes = new List<string>();
            int pagesTotal = 0;
            foreach (PlaySide side in new[] { PlaySide.Offense, PlaySide.Defense, PlaySide.Special })
            {
                foreach (string fam in book.Formations.Where(f => f.Side == side).Select(f => f.Name))
                {
                    List<Play> plays = book.PlaysFor(fam);
                    if (plays.Count == 0) continue;
                    List<string> pn;
                    List<byte[]> pages = PlayCardPainter.Atlas(book, plays, out pn);
                    for (int i = 0; i < pages.Count; i++)
                    {
                        string name = side.ToString().ToLowerInvariant() + "_" + Slug(fam) + "_" + (i + 1).ToString("00") + ".png";
                        File.WriteAllBytes(Path.Combine(OutDir, name), pages[i]);
                        notes.Add(name + "  " + pn[i]);
                        pagesTotal++;
                    }
                }
            }
            File.WriteAllLines(Path.Combine(OutDir, "index.txt"), notes.ToArray());
            Debug.Log("[PIX] playbook atlas: " + book.PlayCount + " plays on " + pagesTotal + " pages -> " + OutDir);
        }

        static string Slug(string s) { return s.ToLowerInvariant().Replace(' ', '_').Replace('-', '_'); }
    }
}
