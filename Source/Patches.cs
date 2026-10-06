using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    [StaticConstructorOnStartup]
    public static class DungeonTextures
    {
        public static readonly Texture2D ShowDungeons = ContentFinder<Texture2D>.Get("UI/TUBAD_ShowDungeons");
    }

    // World map toggle, beside the vanilla ones in the bottom right
    [HarmonyPatch(typeof(PlaySettings), "DoWorldViewControls")]
    public static class Patch_PlaySettings_DoWorldViewControls
    {
        public static void Postfix(WidgetRow row)
        {
            var s = DungeonsMod.Settings;
            bool was = s.showIcons;
            row.ToggleableIcon(ref s.showIcons, DungeonTextures.ShowDungeons, "TUBAD_ToggleIcons".Translate(), SoundDefOf.Mouseover_ButtonToggle);
            if (was != s.showIcons) DungeonsMod.Instance.WriteSettings();
        }
    }

    // "Dungeon: level N" in the selected tile's inspect text
    [HarmonyPatch(typeof(WorldInspectPane), "TileInspectString", MethodType.Getter)]
    public static class Patch_WorldInspectPane_TileInspectString
    {
        public static void Postfix(ref string __result)
        {
            var comp = DungeonWorldComponent.Instance;
            PlanetTile tile = Find.WorldSelector.SelectedTile;
            if (comp == null || !comp.TryGetLevel(tile, out int level) || comp.IsBlocked(tile)) return;
            __result += "\n" + "TUBAD_TileDungeon".Translate(level);
        }
    }
}
