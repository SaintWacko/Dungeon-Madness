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
        public static readonly Texture2D EnterDungeon = ContentFinder<Texture2D>.Get("UI/Commands/EnterCave");
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

    // ---- Entering: right-click a dungeon tile with a caravan selected. Dungeons are not world objects, so the
    // vanilla menu (built from world objects under the mouse) never asks them; these add the option by tile.

    [HarmonyPatch(typeof(FloatMenuMakerWorld), nameof(FloatMenuMakerWorld.ChoicesAtFor), new[] { typeof(Vector2), typeof(Caravan) })]
    public static class Patch_FloatMenuMakerWorld_ChoicesAtFor_Mouse
    {
        public static void Postfix(Vector2 mousePos, Caravan caravan, List<FloatMenuOption> __result)
        {
            if (caravan == null || !caravan.IsPlayerControlled) return;
            // From the given position, not the live mouse: an open menu revalidates against where it was opened
            PlanetTile tile = GenWorld.TileAt(mousePos);
            if (tile.Valid) __result.AddRange(CaravanArrivalAction_EnterDungeon.GetFloatMenuOptions(caravan, tile));
        }
    }

    // Vanilla calls this overload only from CaravanExitMapUtility, to carry out a destination picked while forming a
    // caravan; with a dungeon tile as destination the new caravan travels there and opens the party dialog
    [HarmonyPatch(typeof(FloatMenuMakerWorld), nameof(FloatMenuMakerWorld.ChoicesAtFor), new[] { typeof(PlanetTile), typeof(Caravan) })]
    public static class Patch_FloatMenuMakerWorld_ChoicesAtFor_Tile
    {
        public static void Postfix(PlanetTile tile, Caravan caravan, List<FloatMenuOption> __result)
        {
            if (caravan == null || !caravan.IsPlayerControlled || !tile.Valid) return;
            __result.AddRange(CaravanArrivalAction_EnterDungeon.GetFloatMenuOptions(caravan, tile));
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class Patch_Caravan_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Caravan __instance)
        {
            foreach (var g in gizmos) yield return g;
            if (!__instance.IsPlayerControlled || Find.WorldSelector.SingleSelectedObject != __instance) yield break;
            var comp = DungeonWorldComponent.Instance;
            if (comp == null || !comp.TryGetLevel(__instance.Tile, out int level) || comp.IsBlocked(__instance.Tile)) yield break;
            var report = DungeonEntry.CanEnterAt(__instance.Tile, out _);
            var cmd = new Command_Action
            {
                defaultLabel = "TUBAD_EnterDungeonLevel".Translate(level),
                defaultDesc = "TUBAD_EnterDungeon_Desc".Translate(DungeonsMod.Settings.maxPartySize),
                icon = DungeonTextures.EnterDungeon,
                action = () => CaravanArrivalAction_EnterDungeon.OpenDialog(__instance),
            };
            if (__instance.pather.Moving) cmd.Disable("TUBAD_CaravanMoving".Translate());
            else if (!report.Accepted) cmd.Disable(report.Reason);
            yield return cmd;
        }
    }

    // Tooltip on the pawn rows the enter dialog has ticked and locked. The row is drawn deep inside vanilla's
    // transferable widget, so the count/checkbox call is the one place that has both the row's rect and its
    // transferable. Costs a HashSet lookup per row only while that dialog has locked something.
    [HarmonyPatch(typeof(TransferableUIUtility), nameof(TransferableUIUtility.DoCountAdjustInterface))]
    public static class Patch_TransferableUIUtility_DoCountAdjustInterface
    {
        public static void Postfix(Rect rect, Transferable trad)
        {
            if (Dialog_EnterDungeon.Locked.Count == 0 || !(trad is TransferableOneWay tr) || !Dialog_EnterDungeon.Locked.Contains(tr)) return;
            TooltipHandler.TipRegion(rect, "TUBAD_Enter_LockedTip".Translate());
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
