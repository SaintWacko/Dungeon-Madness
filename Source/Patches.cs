using System.Collections.Generic;
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
        public static readonly Texture2D FindDungeons = ContentFinder<Texture2D>.Get("UI/TUBAD_FindDungeons");
        public static readonly Texture2D WorldIcon = ContentFinder<Texture2D>.Get("World/TUBAD_Dungeon");
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
            if (Current.ProgramState == ProgramState.Playing
                && row.ButtonIcon(DungeonTextures.FindDungeons, "TUBAD_Finder_Open".Translate()))
            {
                var open = Find.WindowStack.WindowOfType<Dialog_DungeonFinder>();
                if (open != null) open.Close();
                else Find.WindowStack.Add(new Dialog_DungeonFinder());
            }
        }
    }

    // ---- Vanilla world search: dungeons are listed as "Dungeon (level N)"

    public class DungeonSearchElement : WorldSearchElement
    {
        public int level;
        public string label;
    }

    [HarmonyPatch(typeof(Dialog_WorldSearch), "InitializeSearchSet")]
    public static class Patch_Dialog_WorldSearch_InitializeSearchSet
    {
        public static void Postfix(List<WorldSearchElement> ___searchSet)
        {
            var comp = DungeonWorldComponent.Instance;
            if (comp == null) return;
            var blocked = comp.BlockedSurfaceTiles();
            var surface = Find.WorldGrid.Surface;
            for (int i = 0; i < comp.Count; i++)
            {
                int id = comp.GetTile(i);
                if (blocked.Contains(id)) continue;
                int level = comp.GetLevel(i);
                ___searchSet.Add(new DungeonSearchElement
                {
                    tile = new PlanetTile(id, surface),
                    level = level,
                    label = "TUBAD_SearchLabel".Translate(level),
                });
            }
        }
    }

    // DisplayLabel is not virtual; without this the element has no label and the dialog skips it
    [HarmonyPatch(typeof(WorldSearchElement), nameof(WorldSearchElement.DisplayLabel), MethodType.Getter)]
    public static class Patch_WorldSearchElement_DisplayLabel
    {
        public static void Postfix(WorldSearchElement __instance, ref string __result)
        {
            if (__instance is DungeonSearchElement d) __result = d.label;
        }
    }

    [HarmonyPatch(typeof(Dialog_WorldSearch), "ElementMatch")]
    public static class Patch_Dialog_WorldSearch_ElementMatch
    {
        public static void Postfix(Dialog_WorldSearch __instance, WorldSearchElement element, ref bool __result)
        {
            if (__result || !(element is DungeonSearchElement d)) return;
            string text = __instance.CommonSearchWidget?.filter?.Text;
            if (text.NullOrEmpty()) return;
            // "dungeon", "level 12" or "dungeon (level 12" all match by substring
            __result = d.label.IndexOf(text, System.StringComparison.InvariantCultureIgnoreCase) >= 0;
        }
    }

    [HarmonyPatch(typeof(Dialog_WorldSearch), "DoIcon")]
    public static class Patch_Dialog_WorldSearch_DoIcon
    {
        public static void Postfix(WorldSearchElement element, Rect iconRect)
        {
            if (!(element is DungeonSearchElement d)) return;
            GUI.color = DungeonUtility.LevelColor(d.level, DungeonWorldComponent.Instance?.MaxLevel ?? 1);
            GUI.DrawTexture(iconRect.ContractedBy(2f), DungeonTextures.WorldIcon);
            GUI.color = Color.white;
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
