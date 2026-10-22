using NUnit.Framework;
using PixFootball.EditorTools;

namespace PixFootball.Tests
{
    /// <summary>The v0.6 import rules (Docs/ArtPipeline.md section 9): texture role by file name and resolution budgets by folder.</summary>
    public class ImportRuleTests
    {
        [TestCase("Assets/_Licensed/PIX/V6/Textures/PIX_V6_Uniform_Normal.png", PixImportPresets.TexKind.Normal)]
        [TestCase("Assets/_Licensed/Art/Hero/T_Helmet_N.png", PixImportPresets.TexKind.Normal)]
        [TestCase("Assets/_Licensed/Art/Hero/T_Helmet_M.png", PixImportPresets.TexKind.Mask)]
        [TestCase("Assets/_Licensed/PIX/V6/Textures/PIX_Uniform_Regions.png", PixImportPresets.TexKind.Mask)]
        [TestCase("Assets/_Licensed/PIX/V6/Textures/helmet_maskMap.png", PixImportPresets.TexKind.Mask)]
        [TestCase("Assets/_Licensed/Art/Gear/T_Gloves_BC.png", PixImportPresets.TexKind.BaseColor)]
        [TestCase("Assets/_Licensed/Art/Hero/Body_Base_color.png", PixImportPresets.TexKind.BaseColor)]
        [TestCase("Assets/_Licensed/Art/Props/crate.png", PixImportPresets.TexKind.Other)]
        public void ClassifyReadsTheFileName(string path, PixImportPresets.TexKind expected)
        {
            Assert.AreEqual(expected, PixImportPresets.Classify(path));
        }

        [Test]
        public void BudgetsFollowTheFolders()
        {
            Assert.AreEqual(4096, PixImportPresets.MaxSizeFor("Assets/_Licensed/Art/Hero/T_Body_BC.png", 8192));
            Assert.AreEqual(2048, PixImportPresets.MaxSizeFor("Assets/_Licensed/Art/Gear/T_Pads_BC.png", 4096));
            Assert.AreEqual(2048, PixImportPresets.MaxSizeFor("Assets/_Licensed/Art/Uniform/T_Jersey_BC.png", 4096));
            Assert.AreEqual(1024, PixImportPresets.MaxSizeFor("Assets/_Licensed/Art/Props/T_Cone_BC.png", 4096));
            Assert.AreEqual(512, PixImportPresets.MaxSizeFor("Assets/_Licensed/PIX/V6/Textures/PIX_FabricWeave_Normal.png", 4096));
            Assert.AreEqual(1024, PixImportPresets.MaxSizeFor("Assets/_Licensed/PIX/V6/Textures/PIX_TurfMicro.png", 4096));
            Assert.AreEqual(512, PixImportPresets.MaxSizeFor("Assets/_Licensed/Art/Props/T_Cone_BC.png", 512), "never raises a smaller texture");
        }
    }
}
