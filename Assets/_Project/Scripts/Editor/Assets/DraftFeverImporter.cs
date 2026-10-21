using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PixFootball.EditorTools;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Reproducible import of the user's Draft Fever assets into Assets/_Licensed/DraftFever (git-ignored).
    /// Stage 1 (outside Unity, one-off): ExtractArchives + UnpackPackages + CopyRaw.
    /// Stage 2 (inside Unity, after the files are on disk and imported): PostImport.
    /// Source tree: _AssetSource/DraftFever (extracted copy of the Draft Fever Bowl project folders).
    /// </summary>
    public static class DraftFeverImporter
    {
        const string Src = "_AssetSource/DraftFever";
        const string Dest = "Assets/_Licensed/DraftFever";

        // (destination sub folder, package path relative to _AssetSource/DraftFever/3D)
        static readonly string[][] Packages =
        {
            new[] { "MainPlayer", "Characters/Players/Mid_Size_Player/PC_Mid_Size_Main_Player/Delivery/Unity_Packages/Unity_HDRP_Main_Player_Setup.unitypackage" },
            new[] { "PlayerMaterials", "Characters/Players/Unity/Materials/Arms_Jersey_Materials.unitypackage" },
            new[] { "PlayerMaterials", "Characters/Players/Unity/Materials/Head_Skin_Material.unitypackage" },
            new[] { "HelmetShader", "Characters/Players/Helmet/Shader_Material/New_Helmet_Shader_Material.unitypackage" },
            new[] { "Stadium_Regular", "Props/Stadium_Regular/Regular_Stadium.unitypackage" },
            new[] { "Stadium_College", "Props/Stadium_College/College_Stadium.unitypackage" },
            new[] { "StadiumKit", "Props/stadiumSwapPackage/DraftFeverAssetImplementation.unitypackage" },
            new[] { "Shaders", "Unity_Packages/Interior_Maping_Shader.unitypackage" },
            new[] { "HairSystem", "Characters/Players/Hair/Hair_Assets_Packages/Hair_System_Updated_30-06-25.unitypackage" },
        };

        /// <summary>
        /// Unity packages are unpacked by hand (GUIDs and .meta preserved, scripts/asmdefs/dlls skipped, GUID de-duplicated across packages)
        /// instead of AssetDatabase.ImportPackage: the old Draft Fever C# would otherwise collide with ours and break compilation.
        /// </summary>
        [MenuItem("PIX Football/Assets/Stage1/Unpack Unity Packages")]
        public static void UnpackPackages()
        {
            var seen = new HashSet<string>();
            foreach (var e in Packages)
            {
                var log = new List<string>();
                string pkg = Path.Combine(Src, "3D", e[1]);
                int n = UnityPackageUnpacker.Extract(pkg, Path.GetFullPath("Assets"), "_Licensed/DraftFever/Packages/" + e[0], seen, log);
                Debug.Log("[DraftFeverImporter] " + e[1] + " -> " + e[0] + ": " + n + " assets");
            }
            // strand hair assets/alembic need the Unity Hair package (not installed); cards/materials are kept
            foreach (string f in Directory.GetFiles(Dest + "/Packages/HairSystem", "*.*", SearchOption.AllDirectories))
                if (f.EndsWith(".abc") || f.EndsWith(".asset") || f.EndsWith(".abc.meta") || f.EndsWith(".asset.meta")) File.Delete(f);
            AssetDatabase.Refresh();
        }

        static int Run(string exe, string args)
        {
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
            p.WaitForExit();
            return p.ExitCode;
        }

        static readonly string[] SkipFiles =
        {
            "*.spp", "*.blend", "*.blend1", "*.psd", "*.zpr", "*.ztl", "*.mb", "*.ma", "*.als", "*.asd", "*.aep", "*.mp4", "*.mov", "*.swatches",
            "*.log", "*.xlsx", "*.sbsar", "*.sbs", "*.assbin", "*.abc", "*.rar", "*.zip", "*.7z", "*.pur", "*.cs", "*.asmdef", "*.dll"
        };

        static void Robo(string src, string dst, params string[] skipDirs)
        {
            string args = "\"" + Path.GetFullPath(src) + "\" \"" + Path.GetFullPath(dst) + "\" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 /XF " + string.Join(" ", SkipFiles);
            if (skipDirs.Length > 0) args += " /XD " + string.Join(" ", skipDirs.Select(d => "\"" + d + "\""));
            int rc = Run("robocopy", args);
            Debug.Log("[DraftFeverImporter] robocopy " + src + " -> " + dst + " rc=" + rc);
        }

        /// <summary>Raw (non-package) assets. Animations come from the .rar/.zip extractions described in Docs/AssetInventory.md.</summary>
        [MenuItem("PIX Football/Assets/Stage1/Copy Raw Assets")]
        public static void CopyRaw()
        {
            string P = Src + "/3D/Characters/Players";
            Robo(P + "/All sizes", Dest + "/Characters/Bodies", "Old Files");
            Robo(P + "/Textures", Dest + "/Characters/SkinTextures");
            Robo(P + "/Mid_Size_Player/PC_Mid_Size_Main_Player/Delivery/Mesh", Dest + "/Characters/MainPlayerDelivery/Mesh");
            Robo(P + "/Mid_Size_Player/PC_Mid_Size_Main_Player/Delivery/Textures", Dest + "/Characters/MainPlayerDelivery/Textures");
            Robo(P + "/Facial Rig/Export", Dest + "/Characters/FacialRig");
            Robo(P + "/Heads/Rodrigo Heads/Export", Dest + "/Characters/Heads/Rodrigo");
            Robo(P + "/Heads/Victors Heads/LODS", Dest + "/Characters/Heads/Victor_LODS");
            Robo(P + "/Customization System", Dest + "/Characters/Customization");
            Robo(P + "/Accessories", Dest + "/Characters/Accessories", "Source", "source");
            Robo(Src + "/3D/Props/New Helmets", Dest + "/Helmets/New");
            Robo(Src + "/3D/Props/Updated Helmet/delivery", Dest + "/Helmets/Updated");
            Robo(Src + "/3D/Props/StadiumProps", Dest + "/Props/StadiumProps", "source", "Source");
            Robo(Src + "/3D/Props/field", Dest + "/Props/Field", "grassVariationTexture_WIP", "WeatherAffectedTextures");
            Robo(Src + "/3D/Props/Stadium_Regular/Seat Positions", Dest + "/Props/SeatPositions/Regular");
            Robo(Src + "/3D/Props/Stadium_College/Seat Positions", Dest + "/Props/SeatPositions/College");
            Robo(Src + "/3D/Characters/Crowd_to_Unity", Dest + "/Characters/Crowd", "Painter", "Zbrush");
            Robo(Src + "/3D/Characters/Referee/Textures", Dest + "/Characters/Referee/Textures");
            Robo(Src + "/3D/Characters/Coaches/Acessories", Dest + "/Characters/Coaches/Accessories");
            Robo(Src + "/UI - NEW/IN-GAME", Dest + "/UI/InGame");
            Robo(Src + "/UI/Atlases", Dest + "/UI/Atlases");
            Robo(Src + "/Assets/Rank Badges", Dest + "/UI/RankBadges");
            Robo(Src + "/Assets/Team Logos/Logo - PRO TEAMS", Dest + "/UI/Logos/Pro");
            Robo(Src + "/Assets/Team Logos/Logo - COLLEGE TEAMS", Dest + "/UI/Logos/College");
            Robo(Src + "/Fonts", Dest + "/Fonts");
            Robo(Src + "/Sound/SFX", Dest + "/Audio/SFX", "Client SFX Project");
            Robo(Src + "/Sound/DFB Sounds - Client Assets", Dest + "/Audio/SFX/ClientAssets");
            Robo(Src + "/Sound/Ambience", Dest + "/Audio/Ambience");
            Robo(Src + "/Sound/Draft Fever Bowl OST", Dest + "/Audio/Music/DraftFeverBowlOST", "Stems");
            Robo(Src + "/Sound/Draft House OST", Dest + "/Audio/Music/DraftHouseOST", "Stems");
            Robo(Src + "/Sound/Peyton Parrish Music", Dest + "/Audio/Music/PeytonParrish");
            AssetDatabase.Refresh();
        }

        /// <summary>Everything that needs the imported assets: avatars, materials, prefabs, ArtLibrary.</summary>
        [MenuItem("PIX Football/Assets/Stage2/Post Import (all)")]
        public static void PostImport()
        {
            CharacterSetup.ConfigureBodies();
            AnimationImportSettings.ApplyAllBatch();
            Debug.Log("[DraftFeverImporter] pink materials repaired: " + MaterialFactory.FixPinkMaterials());
            PlayerPrefabBuilder.BuildAll();
            StadiumPrefabBuilder.BuildStadiums();
            ArtLibraryBuilder.Build();
        }

        public static void FinalPass()
        {
            PostImportPrefabs();
            ExtraPrefabBuilder.BuildAll();
            AssetDatabase.Refresh();
            ArtLibraryBuilder.Build();
            AssetDump.DumpClips();
        }

        /// <summary>Prefabs + library only (animations already configured).</summary>
        public static void PostImportPrefabs()
        {
            CharacterSetup.ConfigureBodies();
            Debug.Log("[DraftFeverImporter] pink materials repaired: " + MaterialFactory.FixPinkMaterials());
            PlayerPrefabBuilder.BuildAll();
            StadiumPrefabBuilder.BuildStadiums();
        }
    }
}
