using System.Collections.Generic;
using System.Linq;
using EntadFramework;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DungeonMadness
{
    /// <summary>
    /// Spend verity on the coming visit, opened from the entry dialog. Edits the entry dialog's DungeonInfluence in
    /// place; nothing is spent here. One row per influence: -10 -5 -1, the value, +1 +5 +10, and what the row costs.
    /// A button that would overshoot a row's limit stops at it; one is greyed only when it cannot move the row or the
    /// colony could not afford the result.
    /// </summary>
    public class Dialog_InfluenceDungeon : Window
    {
        private readonly DungeonInfluence influence;
        private readonly InfluenceTarget target;
        private readonly int baseLevel;
        private readonly DungeonThemeDef normalTheme;
        private readonly int step;
        // Level, then the tile's attuned rows (or every row, with attunement off), in display order
        private readonly List<InfluenceKind> offered;

        private const float RowHeight = 54f;
        private const float LabelWidth = 130f;
        private const float BtnWidth = 38f;
        private const float BtnGap = 3f;
        private const float SpentWidth = 70f;
        private static readonly int[] Deltas = { -10, -5, -1, 1, 5, 10 };

        public override Vector2 InitialSize => new Vector2(680f, 640f);

        public Dialog_InfluenceDungeon(DungeonInfluence influence, InfluenceTarget target)
        {
            this.influence = influence;
            this.target = target;
            baseLevel = target.baseLevel;
            normalTheme = target.normalTheme;
            step = target.BaseStep;
            offered = DungeonInfluence.OfferedRows(target.tile);
            influence.Restrict(target);
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
        }

        private static float Verity => EntadApi.Verity;

        // The cost of the whole influence if one row were at `steps`
        private long CostWith(InfluenceKind k, int steps)
        {
            int old = influence[k];
            influence[k] = steps;
            long cost = influence.TotalCost(target);
            influence[k] = old;
            return cost;
        }

        private bool Affordable(long cost) => cost <= Verity + 0.0001f;

        public override void DoWindowContents(Rect inRect)
        {
            // A new theme or level can move other rows' limits
            influence.ClampToLimits(target);
            float y = inRect.y;
            using (new TextBlock(GameFont.Medium))
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "DMAD_Influence_Title".Translate());
            using (new TextBlock(GameFont.Small, TextAnchor.UpperRight))
            {
                GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(new Rect(inRect.x, y + 6f, inRect.width - 30f, 24f),
                    "DMAD_Influence_Header".Translate(baseLevel, normalTheme.LabelCap, step));
                GUI.color = Color.white;
            }
            y += 36f;

            var ladder = Enumerable.Range(1, 5).Select(n => (DungeonInfluence.Marginal(n) * step).ToString());
            var s = DungeonsMod.Settings;
            string intro = DungeonInfluence.UsesAttunement
                ? "DMAD_Influence_IntroAttuned".Translate(string.Join(", ", ladder))
                : "DMAD_Influence_Intro".Translate(string.Join(", ", ladder), DungeonInfluence.Fee(step));
            using (new TextBlock(GameFont.Small))
            {
                float h = Text.CalcHeight(intro, inRect.width);
                GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, h), intro);
                GUI.color = Color.white;
                y += h + 8f;
            }

            // Column headers
            float btns = BtnWidth * 3f + BtnGap * 2f;
            float valueWidth = inRect.width - LabelWidth - btns * 2f - SpentWidth - 24f;
            using (new TextBlock(GameFont.Tiny))
            {
                GUI.color = ColoredText.SubtleGrayColor;
                float x = inRect.x + LabelWidth;
                Widgets.Label(new Rect(x, y, btns, 18f), "DMAD_Influence_Less".Translate());
                x += btns + 6f;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(x, y, valueWidth, 18f), "DMAD_Influence_Value".Translate());
                x += valueWidth + 6f;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(x, y, btns, 18f), "DMAD_Influence_More".Translate());
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(inRect.xMax - SpentWidth, y, SpentWidth, 18f), "DMAD_Influence_Spent".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            y += 18f;
            Widgets.DrawLineHorizontal(inRect.x, y, inRect.width, ColoredText.SubtleGrayColor.ToTransparent(0.4f));
            y += 4f;

            float footerTop = inRect.yMax - 64f;
            var rowsRect = new Rect(inRect.x, y, inRect.width, footerTop - y - 6f);
            var kinds = offered;
            float viewH = kinds.Count * RowHeight + (s.influenceTheme ? RowHeight : 0f);
            var view = new Rect(0f, 0f, rowsRect.width - (viewH > rowsRect.height ? 16f : 0f), viewH);
            Widgets.BeginScrollView(rowsRect, ref scroll, view);
            float ry = 0f;
            // Narrower than the header's when a scrollbar shows
            float rowValueWidth = view.width - LabelWidth - btns * 2f - SpentWidth - 24f;
            foreach (var k in kinds)
            {
                DoRow(new Rect(0f, ry, view.width, RowHeight), k, rowValueWidth, btns);
                ry += RowHeight;
            }
            if (s.influenceTheme) DoThemeRow(new Rect(0f, ry, view.width, RowHeight));
            Widgets.EndScrollView();

            DoFooter(new Rect(inRect.x, footerTop, inRect.width, 64f));
        }

        private Vector2 scroll;

        private void DoRow(Rect rect, InfluenceKind k, float valueWidth, float btns)
        {
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, LabelWidth, rect.height), ("DMAD_Influence_Row_" + k + "_Tip").Translate());
            int cur = influence[k];
            IntRange lim = influence.Limits(k, target);
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft))
                Widgets.Label(new Rect(rect.x + 4f, rect.y, LabelWidth - 4f, rect.height), ("DMAD_Influence_Row_" + k).Translate());

            float by = rect.y + (rect.height - 26f) / 2f;
            float x = rect.x + LabelWidth;
            for (int i = 0; i < Deltas.Length; i++)
            {
                if (i == 3) x += btns + 6f + valueWidth + 6f - (BtnWidth + BtnGap) * 3f;
                int d = Deltas[i];
                int target = Mathf.Clamp(cur + d, lim.min, lim.max);
                long cost = CostWith(k, target);
                long now = influence.TotalCost(target);
                // A step that lowers the total is always allowed, even over budget
                bool ok = target != cur && (cost <= now || Affordable(cost));
                var b = new Rect(x, by, BtnWidth, 26f);
                if (target != cur)
                {
                    TooltipHandler.TipRegion(b, cost >= now
                        ? "DMAD_Influence_BtnCost".Translate(cost - now, cost)
                        : "DMAD_Influence_BtnRefund".Translate(now - cost, cost));
                }
                using (new TextBlock(GameFont.Tiny))
                {
                    // Widgets.ButtonText's active flag only blocks the click; it draws the same, so grey it here
                    if (!ok) GUI.color = new Color(1f, 1f, 1f, 0.35f);
                    bool clicked = Widgets.ButtonText(b, d > 0 ? "+" + d : d.ToString(), active: ok);
                    GUI.color = Color.white;
                    if (clicked && ok)
                    {
                        influence[k] = target;
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                }
                x += BtnWidth + BtnGap;
            }

            // Value: influenced value, normal, next step's price
            var v = new Rect(rect.x + LabelWidth + btns + 6f, rect.y + 2f, valueWidth, rect.height - 4f);
            using (new TextBlock(GameFont.Small, TextAnchor.UpperCenter))
            {
                GUI.color = cur != 0 ? ColoredText.ImpactColor : Color.white;
                Widgets.Label(new Rect(v.x, v.y, v.width, 22f), influence.ValueText(k, target));
            }
            using (new TextBlock(GameFont.Tiny, TextAnchor.UpperCenter))
            {
                GUI.color = ColoredText.SubtleGrayColor;
                string normal = k == InfluenceKind.PackSize
                    ? "DMAD_Influence_PackNote".Translate().ToString()
                    : "DMAD_Influence_Normal".Translate(influence.NormalText(k, target)).ToString();
                Widgets.Label(new Rect(v.x, v.y + 20f, v.width, 16f), normal);
                int away = Mathf.Abs(cur) + 1;
                bool further = cur >= 0 && cur < lim.max || cur <= 0 && cur > lim.min;
                Widgets.Label(new Rect(v.x, v.y + 34f, v.width, 16f), further
                    ? "DMAD_Influence_Next".Translate(DungeonInfluence.Marginal(away) * step
                        + (influence.Any(normalTheme) ? 0 : DungeonInfluence.VisitFee(step))).ToString()
                    : "DMAD_Influence_AtLimit".Translate().ToString());
            }
            GUI.color = Color.white;

            long spent = DungeonInfluence.RowCost(cur, step);
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleRight))
            {
                if (spent == 0) GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(new Rect(rect.xMax - SpentWidth, rect.y, SpentWidth - 4f, rect.height), spent == 0 ? "-" : spent.ToString());
                GUI.color = Color.white;
            }
        }

        private void DoThemeRow(Rect rect)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width, ColoredText.SubtleGrayColor.ToTransparent(0.3f));
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft))
                Widgets.Label(new Rect(rect.x + 4f, rect.y, LabelWidth - 4f, rect.height), "DMAD_Influence_Theme".Translate());
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, LabelWidth, rect.height), "DMAD_Influence_Theme_Tip".Translate());

            DungeonThemeDef shown = influence.theme ?? normalTheme;
            bool changed = influence.ThemeChanged(normalTheme);
            float x = rect.x + LabelWidth;
            var b = new Rect(x, rect.y + (rect.height - 30f) / 2f, 200f, 30f);
            if (Widgets.ButtonText(b, shown.LabelCap))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("DMAD_Influence_ThemeNormal".Translate(normalTheme.LabelCap), () => influence.theme = null),
                };
                foreach (var t in DefDatabase<DungeonThemeDef>.AllDefsListForReading.OrderBy(t => t.label))
                {
                    if (t == normalTheme) continue;
                    var old = influence.theme;
                    influence.theme = t;
                    long cost = influence.TotalCost(target);
                    influence.theme = old;
                    var pick = t;
                    options.Add(Affordable(cost)
                        ? new FloatMenuOption(t.LabelCap, () => influence.theme = pick)
                        : new FloatMenuOption(t.LabelCap + " (" + "DMAD_Influence_CannotAfford".Translate() + ")", null));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleLeft))
            {
                GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(new Rect(b.xMax + 10f, rect.y, rect.xMax - SpentWidth - b.xMax - 10f, rect.height),
                    "DMAD_Influence_ThemeNote".Translate(normalTheme.LabelCap, DungeonInfluence.ThemeFee(step)));
                GUI.color = Color.white;
            }
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleRight))
            {
                long spent = changed ? DungeonInfluence.ThemeFee(step) : 0;
                if (spent == 0) GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(new Rect(rect.xMax - SpentWidth, rect.y, SpentWidth - 4f, rect.height), spent == 0 ? "-" : spent.ToString());
                GUI.color = Color.white;
            }
        }

        private void DoFooter(Rect rect)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width, ColoredText.SubtleGrayColor.ToTransparent(0.4f));
            long total = influence.TotalCost(target);
            bool any = influence.Any(normalTheme);
            float verity = Verity;
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft))
            {
                float y = rect.y + 4f;
                Widgets.Label(new Rect(rect.x, y, 360f, 20f), DungeonInfluence.UsesAttunement
                    ? "DMAD_Influence_FeeAttuned".Translate()
                    : "DMAD_Influence_Fee".Translate(any ? DungeonInfluence.Fee(step) : 0));
                Widgets.Label(new Rect(rect.x, y + 20f, 360f, 20f), "DMAD_Influence_Total".Translate(total));
                GUI.color = total > verity + 0.0001f ? ColorLibrary.RedReadable : ColoredText.SubtleGrayColor;
                Widgets.Label(new Rect(rect.x, y + 40f, 360f, 20f),
                    "DMAD_Influence_Verity".Translate(verity.ToString("0.#"), (verity - total).ToString("0.#")));
                GUI.color = Color.white;
            }
            var done = new Rect(rect.xMax - 140f, rect.y + 14f, 140f, 38f);
            if (Widgets.ButtonText(done, "DoneButton".Translate())) Close();
            if (Widgets.ButtonText(new Rect(done.x - 150f, done.y, 140f, 38f), "ResetButton".Translate()))
            {
                influence.Reset();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }
    }
}
