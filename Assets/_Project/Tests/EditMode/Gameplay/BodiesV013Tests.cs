using NUnit.Framework;
using PixFootball.EditorTools;
using UnityEditor;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.13.0 BODIES-2: the under-cloth weight rule, the cloth map import settings and the uniform material strength (live assets; skipped when the licensed art is absent).</summary>
    public class BodiesV013Tests
    {
        const string Uni = "Assets/_Licensed/PhotorealPlayer/Textures/Uniform/";

        [Test]
        public void UnderClothAlphaIsZeroOnTheSurfaceFullDeepInsideAndFadesWithTheShoulderDistance()
        {
            Assert.AreEqual(0f, PhotoUnderCloth.CoverAlpha(0.0f, 0.05f), 1e-6f, "skin at the cloth surface keeps its own weights");
            Assert.AreEqual(0f, PhotoUnderCloth.CoverAlpha(-0.01f, 0.05f), 1e-6f, "skin outside the cloth keeps its own weights");
            Assert.AreEqual(1f, PhotoUnderCloth.CoverAlpha(0.03f, 0.10f), 1e-6f, "deep under the cloth near the shoulder follows the cloth");
            Assert.AreEqual(0f, PhotoUnderCloth.CoverAlpha(0.03f, 0.40f), 1e-6f, "forearm skin is never touched");
            float prev = -1f;
            for (float d = 0f; d <= 0.03f; d += 0.002f) { float a = PhotoUnderCloth.CoverAlpha(d, 0.1f); Assert.GreaterOrEqual(a, prev - 1e-6f); prev = a; }
        }

        [Test]
        public void ClothMapsAreNormalMapsAndTheGreyIsSrgb()
        {
            foreach (string n in new[] { "jersey_N", "pants_N", "armsleeve_N" })
            {
                var ti = AssetImporter.GetAtPath(Uni + n + ".png") as TextureImporter; if (ti == null) Assert.Ignore("licensed art absent");
                Assert.AreEqual(TextureImporterType.NormalMap, ti.textureType, n);
            }
            foreach (string n in new[] { "jersey_Gray", "pants_Gray", "armsleeve_Gray" })
            {
                var ti = AssetImporter.GetAtPath(Uni + n + ".png") as TextureImporter; if (ti == null) Assert.Ignore("licensed art absent");
                Assert.IsTrue(ti.sRGBTexture, n);
            }
        }

        [Test]
        public void StageImportKeepsTheLiveImporterSettings()
        {
            // the promotion copies the stage PNGs over the live ones, so the stage import (name based: _N normal map, _Gray sRGB, others linear) must match the live meta files
            Assert.IsTrue(PhotoBodiesStage.ClothMaps.Length >= 7);
            foreach (string m in PhotoBodiesStage.ClothMaps)
            {
                var ti = AssetImporter.GetAtPath(Uni + m + ".png") as TextureImporter; if (ti == null) Assert.Ignore("licensed art absent");
                Assert.AreEqual(m.EndsWith("_N"), ti.textureType == TextureImporterType.NormalMap, m);
                Assert.AreEqual(m.EndsWith("_Gray"), ti.sRGBTexture, m);
            }
        }
    }
}
