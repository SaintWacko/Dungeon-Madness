using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    /// <summary>
    /// Lists dungeons by level range and distance, hiding any that a ticked pawn has already entered. Stays open
    /// over the world map and highlights the matching icons.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Dialog_DungeonFinder : Window
    {
        private static readonly Material HighlightMat = MaterialPool.MatFrom("World/SelectedTile", ShaderDatabase.WorldOverlayAdditive, new Color(1f, 1f, 1f, 0.6f), 3560);
        private const int MaxHighlights = 400;
        private const float RowHeight = 26f;
        private const int AnyDistance = 1000;

        // Remembered between openings in the same session
        private static IntRange levelRange = new IntRange(1, 10);
        private static int maxDistance = AnyDistance;
        private static readonly HashSet<Pawn> ticked = new HashSet<Pawn>();

        private List<Pawn> candidates = new List<Pawn>();
        private List<PlanetTile> origins = new List<PlanetTile>();
        private PlanetTile origin = PlanetTile.Invalid;
        private string originLabel;

        private readonly List<(PlanetTile tile, int level, float dist, string desc)> results = new List<(PlanetTile, int, float, string)>();
        private bool dirty = true;
        private Vector2 partyScroll, resultScroll;
        private PlanetTile hovered = PlanetTile.Invalid;

        public override Vector2 InitialSize => new Vector2(700f, 520f);
        protected override float Margin => 12f;

        public Dialog_DungeonFinder()
        {
            doCloseX = true;
            doCloseButton = false;
            draggable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            forcePause = false;
            RefreshCandidates();
            var caravan = Find.WorldSelector.FirstSelectedObject as Caravan;
            if (caravan != null && caravan.IsPlayerControlled) LoadCaravan(caravan);
            else if (Find.AnyPlayerHomeMap != null) SetOrigin(Find.AnyPlayerHomeMap.Tile, Find.AnyPlayerHomeMap.Parent.LabelCap);
            ticked.RemoveWhere(p => !candidates.Contains(p));
        }

        private void RefreshCandidates()
        {
            candidates = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
                .Where(p => p.RaceProps.Humanlike)
                .OrderBy(p => p.LabelShort).ToList();
        }

        private void SetOrigin(PlanetTile tile, string label)
        {
            origin = tile;
            originLabel = label;
            dirty = true;
        }

        // Ticks the caravan's humanlikes and nobody else; retainers are then unticked by hand
        private void LoadCaravan(Caravan caravan)
        {
            ticked.Clear();
            foreach (var p in caravan.PawnsListForReading)
                if (p.RaceProps.Humanlike && candidates.Contains(p)) ticked.Add(p);
            SetOrigin(caravan.Tile, caravan.LabelCap);
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (!WorldRendererUtility.WorldSelected) return;
            // Outline matching dungeons; the hovered row's tile is drawn twice so it stands out
            float size = Find.WorldGrid.AverageTileSize;
            for (int i = 0; i < results.Count && i < MaxHighlights; i++)
                WorldRendererUtility.DrawQuadTangentialToPlanet(Find.WorldGrid.GetTileCenter(results[i].tile), size, 0.012f, HighlightMat);
            if (hovered.Valid)
                WorldRendererUtility.DrawQuadTangentialToPlanet(Find.WorldGrid.GetTileCenter(hovered), size * 1.4f, 0.013f, HighlightMat);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (dirty) Recalculate();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "TUBAD_Finder_Title".Translate());
            Text.Font = GameFont.Small;
            var body = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);
            var left = new Rect(body.x, body.y, 230f, body.height);
            var right = new Rect(left.xMax + 12f, body.y, body.width - left.width - 12f, body.height);
            DoFilters(left);
            DoResults(right);
        }

        private void DoFilters(Rect rect)
        {
            float y = rect.y;
            Header(rect, ref y, "TUBAD_Finder_Party");
            float hintH = Text.CalcHeight("TUBAD_Finder_PartyHint".Translate(), rect.width);
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(new Rect(rect.x, y, rect.width, hintH), "TUBAD_Finder_PartyHint".Translate());
            GUI.color = Color.white;
            y += hintH + 4f;

            var caravan = Find.WorldSelector.FirstSelectedObject as Caravan;
            bool canLoad = caravan != null && caravan.IsPlayerControlled;
            var btn = new Rect(rect.x, y, rect.width, 28f);
            if (Widgets.ButtonText(btn, "TUBAD_Finder_LoadCaravan".Translate(), active: canLoad) && canLoad)
            {
                RefreshCandidates();
                LoadCaravan(caravan);
            }
            if (!canLoad) TooltipHandler.TipRegion(btn, "TUBAD_Finder_LoadCaravan_Disabled".Translate());
            y += 32f;

            var outRect = new Rect(rect.x, y, rect.width, 150f);
            Widgets.DrawMenuSection(outRect);
            var inner = outRect.ContractedBy(4f);
            // Ticked pawns first, so a loaded caravan reads as one block at the top
            var ordered = candidates.OrderByDescending(p => ticked.Contains(p)).ToList();
            var view = new Rect(0f, 0f, inner.width - 16f, ordered.Count * 24f);
            Widgets.BeginScrollView(inner, ref partyScroll, view);
            for (int i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                bool on = ticked.Contains(p);
                bool was = on;
                Widgets.CheckboxLabeled(new Rect(0f, i * 24f, view.width, 24f), p.LabelShortCap, ref on);
                if (on != was)
                {
                    if (on) ticked.Add(p); else ticked.Remove(p);
                    dirty = true;
                }
            }
            Widgets.EndScrollView();
            y += 154f;

            int count = ticked.Count;
            GUI.color = count > DungeonUtility.MaxPartyHumanlikes ? ColorLibrary.RedReadable : Color.white;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "TUBAD_Finder_PartyCount".Translate(count, DungeonUtility.MaxPartyHumanlikes));
            GUI.color = Color.white;
            y += 30f;

            Header(rect, ref y, "TUBAD_Finder_Level");
            int maxLevel = DungeonWorldComponent.Instance?.MaxLevel ?? DungeonsMod.Settings.maxLevel;
            var before = levelRange;
            levelRange.min = Mathf.Clamp(levelRange.min, 1, maxLevel);
            levelRange.max = Mathf.Clamp(levelRange.max, levelRange.min, maxLevel);
            Widgets.IntRange(new Rect(rect.x, y, rect.width, 32f), 0x7D0A1, ref levelRange, 1, maxLevel);
            if (levelRange != before) dirty = true;
            y += 40f;

            Header(rect, ref y, "TUBAD_Finder_Distance");
            var fromRect = new Rect(rect.x, y, rect.width, 28f);
            if (Widgets.ButtonText(fromRect, "TUBAD_Finder_From".Translate(originLabel ?? "-")))
                Find.WindowStack.Add(new FloatMenu(OriginOptions()));
            y += 32f;
            string within = maxDistance >= AnyDistance ? "TUBAD_Finder_AnyDistance".Translate().ToString() : "TUBAD_Finder_Within".Translate(maxDistance).ToString();
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), within);
            y += 24f;
            int newDist = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(rect.x, y, rect.width, 20f), maxDistance, 5f, AnyDistance, roundTo: 5f));
            if (newDist != maxDistance)
            {
                maxDistance = newDist;
                dirty = true;
            }
        }

        private List<FloatMenuOption> OriginOptions()
        {
            var opts = new List<FloatMenuOption>();
            foreach (var map in Find.Maps.Where(m => m.IsPlayerHome))
            {
                var m = map;
                opts.Add(new FloatMenuOption(m.Parent.LabelCap, () => SetOrigin(m.Tile, m.Parent.LabelCap)));
            }
            foreach (var c in Find.WorldObjects.Caravans.Where(c => c.IsPlayerControlled))
            {
                var cv = c;
                opts.Add(new FloatMenuOption(cv.LabelCap, () => SetOrigin(cv.Tile, cv.LabelCap)));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("TUBAD_Finder_NoOrigins".Translate(), null));
            return opts;
        }

        private static void Header(Rect rect, ref float y, string key)
        {
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), key.Translate().CapitalizeFirst().Colorize(ColoredText.TipSectionTitleColor));
            y += 24f;
        }

        private void DoResults(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "TUBAD_Finder_Matches".Translate(results.Count));
            var outRect = new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 28f);
            Widgets.DrawMenuSection(outRect);
            var inner = outRect.ContractedBy(4f);
            var view = new Rect(0f, 0f, inner.width - 16f, results.Count * RowHeight);
            hovered = PlanetTile.Invalid;
            int maxLevel = DungeonWorldComponent.Instance?.MaxLevel ?? 1;
            Widgets.BeginScrollView(inner, ref resultScroll, view);
            int first = Mathf.Max(0, Mathf.FloorToInt(resultScroll.y / RowHeight));
            int last = Mathf.Min(results.Count, first + Mathf.CeilToInt(inner.height / RowHeight) + 1);
            for (int i = first; i < last; i++)
            {
                var r = results[i];
                var row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                Widgets.DrawBoxSolid(new Rect(row.x + 4f, row.y + 8f, 10f, 10f), DungeonUtility.LevelColor(r.level, maxLevel));
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x + 20f, row.y, 70f, row.height), "TUBAD_Finder_LevelShort".Translate(r.level));
                Widgets.Label(new Rect(row.x + 92f, row.y, row.width - 92f - 80f, row.height), r.desc.Truncate(row.width - 172f));
                Text.Anchor = TextAnchor.MiddleRight;
                string dist = origin.Valid ? "TUBAD_Finder_Tiles".Translate(Mathf.RoundToInt(r.dist)).ToString() : "";
                Widgets.Label(new Rect(row.xMax - 80f, row.y, 76f, row.height), dist);
                Text.Anchor = TextAnchor.UpperLeft;
                if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                    hovered = r.tile;
                }
                if (Widgets.ButtonInvisible(row)) DungeonUtility.JumpTo(r.tile);
            }
            Widgets.EndScrollView();
        }

        private void Recalculate()
        {
            dirty = false;
            results.Clear();
            var comp = DungeonWorldComponent.Instance;
            if (comp == null) return;
            var blocked = comp.BlockedSurfaceTiles();
            var surface = Find.WorldGrid.Surface;
            bool limit = origin.Valid && maxDistance < AnyDistance;
            for (int i = 0; i < comp.Count; i++)
            {
                int level = comp.GetLevel(i);
                if (level < levelRange.min || level > levelRange.max) continue;
                int id = comp.GetTile(i);
                if (blocked.Contains(id)) continue;
                if (ticked.Any(p => comp.HasEntered(id, p))) continue;
                var tile = new PlanetTile(id, surface);
                float dist = origin.Valid ? Find.WorldGrid.ApproxDistanceInTiles(origin, tile) : 0f;
                if (limit && dist > maxDistance) continue;
                results.Add((tile, level, dist, null));
            }
            results.Sort((a, b) => a.dist != b.dist ? a.dist.CompareTo(b.dist) : a.level.CompareTo(b.level));
            // Descriptions only for what is listed, after filtering
            for (int i = 0; i < results.Count; i++)
                results[i] = (results[i].tile, results[i].level, results[i].dist, DungeonUtility.DescribeTile(results[i].tile));
        }
    }
}
