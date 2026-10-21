using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Editor;

namespace PixFootball.EditorTools
{
    /// <summary>Builds Assets/_Project/Input/PixControls.inputactions (Xbox-first, keyboard+mouse fallback) and its C# wrapper.</summary>
    public static class InputMapBuilder
    {
        public const string AssetPath = "Assets/_Project/Input/PixControls.inputactions";
        public const string WrapperPath = "Assets/_Project/Scripts/Gameplay/Input/PixControls.cs";
        const string Pad = "Gamepad", Kb = "KeyboardMouse";

        public static readonly string[] ExpectedActions =
        {
            "Move", "Look", "Sprint", "Hurdle", "Spin", "Dive", "Truck", "JukeLeft", "JukeRight", "StiffArmLeft", "StiffArmRight",
            "Protect", "Celebrate", "CycleCamera", "Pause", "ToggleLighting", "ResetPlayer"
        };

        static InputAction Btn(InputActionMap map, string name, string pad, string kb, string pad2 = null, string kb2 = null)
        {
            var a = map.AddAction(name, InputActionType.Button);
            if (pad != null) a.AddBinding(pad, groups: Pad);
            if (pad2 != null) a.AddBinding(pad2, groups: Pad);
            if (kb != null) a.AddBinding(kb, groups: Kb);
            if (kb2 != null) a.AddBinding(kb2, groups: Kb);
            return a;
        }

        public static InputActionAsset Create()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "PixControls";
            asset.AddControlScheme(Pad).WithRequiredDevice("<Gamepad>");
            asset.AddControlScheme(Kb).WithRequiredDevice("<Keyboard>").WithOptionalDevice("<Mouse>");
            var map = asset.AddActionMap("Gameplay");

            var move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddBinding("<Gamepad>/leftStick", groups: Pad, processors: "stickDeadzone(min=0.12,max=0.95)");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w", Kb).With("Down", "<Keyboard>/s", Kb).With("Left", "<Keyboard>/a", Kb).With("Right", "<Keyboard>/d", Kb);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow", Kb).With("Down", "<Keyboard>/downArrow", Kb).With("Left", "<Keyboard>/leftArrow", Kb).With("Right", "<Keyboard>/rightArrow", Kb);

            var look = map.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            look.AddBinding("<Gamepad>/rightStick", groups: Pad, processors: "stickDeadzone(min=0.15,max=0.95)");
            look.AddBinding("<Mouse>/delta", groups: Kb, processors: "scaleVector2(x=0.05,y=0.05)");

            Btn(map, "Sprint", "<Gamepad>/rightTrigger", "<Keyboard>/leftShift");
            Btn(map, "Hurdle", "<Gamepad>/buttonSouth", "<Keyboard>/space");
            Btn(map, "Spin", "<Gamepad>/buttonEast", "<Keyboard>/f");
            Btn(map, "Dive", "<Gamepad>/buttonWest", "<Keyboard>/c");
            Btn(map, "Truck", "<Gamepad>/buttonNorth", "<Keyboard>/r");
            Btn(map, "JukeLeft", "<Gamepad>/dpad/left", "<Keyboard>/q");
            Btn(map, "JukeRight", "<Gamepad>/dpad/right", "<Keyboard>/e");
            Btn(map, "StiffArmLeft", "<Gamepad>/leftShoulder", "<Keyboard>/z");
            Btn(map, "StiffArmRight", "<Gamepad>/rightShoulder", "<Keyboard>/x");
            Btn(map, "Protect", "<Gamepad>/leftTrigger", "<Keyboard>/v");
            Btn(map, "Celebrate", "<Gamepad>/dpad/up", "<Keyboard>/g");
            Btn(map, "CycleCamera", "<Gamepad>/select", "<Keyboard>/tab");
            Btn(map, "Pause", "<Gamepad>/start", "<Keyboard>/escape");
            Btn(map, "ToggleLighting", "<Gamepad>/dpad/down", "<Keyboard>/l");
            Btn(map, "ResetPlayer", "<Gamepad>/leftStickPress", "<Keyboard>/backspace");
            return asset;
        }

        [MenuItem("PIX Football/Build/Generate Input Map")]
        public static void Generate()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(WrapperPath));
            var asset = Create();
            File.WriteAllText(AssetPath, asset.ToJson());
            string code = InputActionCodeGenerator.GenerateWrapperCode(asset, new InputActionCodeGenerator.Options
            {
                className = "PixControls",
                namespaceName = "PixFootball.Gameplay",
                sourceAssetPath = AssetPath,
            });
            File.WriteAllText(WrapperPath, code);
            Object.DestroyImmediate(asset);
            AssetDatabase.Refresh();
            Debug.Log("[PIX] Input map + wrapper generated");
        }
    }
}
