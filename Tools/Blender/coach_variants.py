"""Coach variant definitions (object names inside Coach_Final.blend; {n} = two digit coach head number)."""

SOURCE_BLEND = r"_AssetSource\DraftFever\3D\Characters\Coaches\Blender\Coach_Final.blend"

# part -> (object name in the blend, material slot name used by the Unity builder)
VARIANTS = {
    # A: team polo + cap + headset-over-cap (+ clipboard prop added in Unity)
    "A": dict(head=1, skin="skin_a",
              parts=[("Coach_Polo_Shirt", "top"), ("Coach_Casual_Pants", "pants"), ("Coach_Shoes", "shoes"),
                     ("Coach_{n}_Cap", "cap"), ("Coach_{n}_Hair_Straight_Short_With_Cap", "hair"),
                     ("Coach_{n}_HeadSet_With_Cap", "headset"), ("Eyebrows_Coach_{n}", "hair"), ("Coach_Watch", "watch")]),
    # B: team bomber jacket + visor + headset + goatee (+ tablet prop)
    "B": dict(head=3, skin="skin_b",
              parts=[("Coach_Bomber_Jacket", "top"), ("Coach_Jeans_Pants", "pants"), ("Coach_Shoes", "shoes"),
                     ("Coach_{n}_Visor_With_Hair", "cap"), ("Coach_{n}_Hair_Short_Straight_Visor", "hair"),
                     ("Coach_{n}_HeadSet", "headset"), ("Eyebrows_Coach_{n}", "hair"), ("Coach_{n}_Goatee", "hair")]),
    # C: team puffer jacket + cap + headset + full beard
    "C": dict(head=5, skin="skin_c",
              parts=[("Coach_Puffer", "top"), ("Coach_Track_Pants", "pants"), ("Coach_Shoes", "shoes"),
                     ("Coach_{n}_Cap", "cap"), ("Coach_{n}_Hair_Curly_Short_With_Cap", "hair"),
                     ("Coach_{n}_HeadSet_With_Cap", "headset"), ("Eyebrows_Coach_{n}", "hair"), ("Coach_{n}_Full_Beard", "hair"),
                     ("Coach_{n}_Usual_Glasses", "glasses")]),
}
