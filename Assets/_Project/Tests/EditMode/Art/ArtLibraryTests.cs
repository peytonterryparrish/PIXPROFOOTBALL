using NUnit.Framework;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Tests
{
    public class ArtLibraryTests
    {
        static AnimationClip Clip(string n) { var c = new AnimationClip(); c.name = n; return c; }

        [Test]
        public void ClipLookupIsCaseInsensitiveAndVariantsWrap()
        {
            var lib = ScriptableObject.CreateInstance<ArtLibrary>();
            lib.clips.Add(new ArtLibrary.ClipEntry { key = ArtKeys.Sprint, clip = Clip("a") });
            lib.clips.Add(new ArtLibrary.ClipEntry { key = ArtKeys.Sprint, clip = Clip("b") });
            lib.BuildMaps();
            Assert.IsTrue(lib.HasClip("SPRINT"));
            Assert.AreEqual(2, lib.ClipVariantCount(ArtKeys.Sprint));
            Assert.AreEqual("a", lib.ClipVariant(ArtKeys.Sprint, 0).name);
            Assert.AreEqual("b", lib.ClipVariant(ArtKeys.Sprint, 3).name);
            Assert.AreEqual("b", lib.ClipVariant(ArtKeys.Sprint, -1).name);
            Assert.IsNull(lib.Clip(ArtKeys.QbSpike));
            Object.DestroyImmediate(lib);
        }

        [Test]
        public void MissingEntriesReturnNullNotThrow()
        {
            var lib = ScriptableObject.CreateInstance<ArtLibrary>();
            Assert.IsNull(lib.Prefab("nope"));
            Assert.IsNull(lib.Sprite("nope"));
            Assert.IsNull(lib.Audio("nope"));
            Assert.AreEqual(0, lib.AudioVariantCount("nope"));
            Assert.AreEqual(0, lib.ClipVariants("nope").Count);
            Object.DestroyImmediate(lib);
        }

        [Test]
        public void AnimationKeyListIsRich()
        {
            int n = 0;
            foreach (var f in typeof(ArtKeys).GetFields()) if (f.IsLiteral) n++;
            Assert.GreaterOrEqual(n, 60);
        }
    }
}
