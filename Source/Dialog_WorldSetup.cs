using System;
using System.Collections.Generic;
using BastleFramework;
using EntadFramework;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    // Per-game choice of where bastles and entads turn up outside dungeons. Accept writes every switch as an
    // override, not just the changed ones, so editing the frameworks' mod settings later never changes this game.
    public class Dialog_WorldSetup : Window
    {
        // By name rather than by enum member: a row whose switch the installed framework doesn't have yet (or has
        // renamed) is left out instead of failing to load the window. The first build against Bastle hit exactly
        // that, a DLL older than the source it was built from, without RewardAnimals.
        private static readonly List<BastleWorldFeature> BastleConverted = Present<BastleWorldFeature>(
            "WildAnimals", "AnimalEvents", "GroupAnimals", "TraderAnimals", "RewardAnimals");
        private static readonly List<BastleWorldFeature> BastleEvents = Present<BastleWorldFeature>(
            "Sighting", "Pack", "Rampage", "HuntQuest");
        private static readonly List<EntadWorldFeature> EntadSources = Present<EntadWorldFeature>(
            "Carriers", "Traders", "Rewards", "Loot", "Appraisers");

        private static List<T> Present<T>(params string[] names) where T : struct
        {
            var found = new List<T>();
            foreach (var n in names)
                if (Enum.IsDefined(typeof(T), n)) found.Add((T)Enum.Parse(typeof(T), n));
            return found;
        }

        // Every enum value, so a source a framework adds later is carried through Accept unchanged
        private readonly Dictionary<BastleWorldFeature, bool> bastle = new Dictionary<BastleWorldFeature, bool>();
        private readonly Dictionary<EntadWorldFeature, bool> entad = new Dictionary<EntadWorldFeature, bool>();

        private const float RowHeight = 26f;
        private const float Indent = 18f;

        public override Vector2 InitialSize => new Vector2(720f, 590f);

        public Dialog_WorldSetup()
        {
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            foreach (BastleWorldFeature f in Enum.GetValues(typeof(BastleWorldFeature)))
                bastle[f] = BastleWorld.GetForGame(f) ?? BastleWorld.FromSettings(f);
            foreach (EntadWorldFeature f in Enum.GetValues(typeof(EntadWorldFeature)))
                entad[f] = EntadWorld.GetForGame(f) ?? EntadWorld.FromSettings(f);
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "DMAD_WorldSetup_Title".Translate());
            y += 36f;
            Text.Font = GameFont.Small;

            string intro = "DMAD_WorldSetup_Intro".Translate();
            float h = Text.CalcHeight(intro, inRect.width);
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, h), intro);
            GUI.color = Color.white;
            y += h + 10f;

            DoPresets(new Rect(inRect.x, y, inRect.width, 32f));
            y += 42f;

            string foot = "DMAD_WorldSetup_Footnote".Translate();
            float footH = Text.CalcHeight(foot, inRect.width);
            float colBottom = inRect.yMax - 40f - footH - 8f;
            float colW = (inRect.width - 14f) / 2f;
            DoBastleColumn(new Rect(inRect.x, y, colW, colBottom - y));
            DoEntadColumn(new Rect(inRect.x + colW + 14f, y, colW, colBottom - y));

            GUI.color = ColoredText.SubtleGrayColor;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, colBottom + 6f, inRect.width, footH + 4f), foot);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            if (Widgets.ButtonText(new Rect(inRect.xMax - 160f, inRect.yMax - 36f, 160f, 36f), "AcceptButton".Translate()))
                Accept();
        }

        public override void OnAcceptKeyPressed() => Accept();

        private void DoPresets(Rect rect)
        {
            float w = (rect.width - 12f) / 3f;
            var a = new Rect(rect.x, rect.y, w, rect.height);
            var b = new Rect(a.xMax + 6f, rect.y, w, rect.height);
            var c = new Rect(b.xMax + 6f, rect.y, w, rect.height);
            if (Widgets.ButtonText(a, "DMAD_WorldSetup_PresetDungeonsOnly".Translate()))
            {
                bastle[BastleWorldFeature.World] = false;
                entad[EntadWorldFeature.World] = false;
            }
            if (Widgets.ButtonText(b, "DMAD_WorldSetup_PresetLivingWorld".Translate()))
            {
                foreach (var k in new List<BastleWorldFeature>(bastle.Keys)) bastle[k] = true;
                foreach (var k in new List<EntadWorldFeature>(entad.Keys)) entad[k] = true;
            }
            if (Widgets.ButtonText(c, "DMAD_WorldSetup_PresetSettings".Translate()))
            {
                foreach (var k in new List<BastleWorldFeature>(bastle.Keys)) bastle[k] = BastleWorld.FromSettings(k);
                foreach (var k in new List<EntadWorldFeature>(entad.Keys)) entad[k] = EntadWorld.FromSettings(k);
            }
            TooltipHandler.TipRegion(a, "DMAD_WorldSetup_PresetDungeonsOnly_Tip".Translate());
            TooltipHandler.TipRegion(b, "DMAD_WorldSetup_PresetLivingWorld_Tip".Translate());
            TooltipHandler.TipRegion(c, "DMAD_WorldSetup_PresetSettings_Tip".Translate());
        }

        private void DoBastleColumn(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var r = rect.ContractedBy(8f);
            float y = r.y;
            Heading(r, ref y, "DMAD_WorldSetup_Bastles", GameFont.Medium);
            bool master = bastle[BastleWorldFeature.World];
            Row(r, ref y, 0f, "DMAD_WorldSetup_Bastle_World", ref master, BastleWorld.FromSettings(BastleWorldFeature.World), false);
            bastle[BastleWorldFeature.World] = master;
            y += 4f;
            Heading(r, ref y, "DMAD_WorldSetup_BastleConverted", GameFont.Small);
            foreach (var f in BastleConverted) BastleRow(r, ref y, f, !master);
            y += 4f;
            Heading(r, ref y, "DMAD_WorldSetup_BastleEvents", GameFont.Small);
            foreach (var f in BastleEvents) BastleRow(r, ref y, f, !master);
        }

        private void BastleRow(Rect r, ref float y, BastleWorldFeature f, bool dim)
        {
            bool v = bastle[f];
            Row(r, ref y, Indent, "DMAD_WorldSetup_Bastle_" + f, ref v, BastleWorld.FromSettings(f), dim);
            bastle[f] = v;
        }

        private void DoEntadColumn(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var r = rect.ContractedBy(8f);
            float y = r.y;
            Heading(r, ref y, "DMAD_WorldSetup_Entads", GameFont.Medium);
            bool master = entad[EntadWorldFeature.World];
            Row(r, ref y, 0f, "DMAD_WorldSetup_Entad_World", ref master, EntadWorld.FromSettings(EntadWorldFeature.World), false);
            entad[EntadWorldFeature.World] = master;
            y += 4f;
            foreach (var f in EntadSources)
            {
                bool v = entad[f];
                Row(r, ref y, Indent, "DMAD_WorldSetup_Entad_" + f, ref v, EntadWorld.FromSettings(f), !master);
                entad[f] = v;
            }
            if (!master || !bastle[BastleWorldFeature.World])
            {
                y += 8f;
                string note = "DMAD_WorldSetup_Greyed".Translate();
                float h = Text.CalcHeight(note, r.width);
                GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(new Rect(r.x, y, r.width, h), note);
                GUI.color = Color.white;
            }
        }

        private static void Heading(Rect r, ref float y, string key, GameFont font)
        {
            Text.Font = font;
            float h = Text.LineHeight + 2f;
            if (font == GameFont.Small) GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(new Rect(r.x, y, r.width, h), key.Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += h;
        }

        // Greyed rows stay clickable: they keep their tick for when the master switch goes back on
        private static void Row(Rect r, ref float y, float indent, string key, ref bool value, bool fromSettings, bool dim)
        {
            var row = new Rect(r.x + indent, y, r.width - indent, RowHeight);
            string label = key.Translate();
            if (value != fromSettings) label += " *";
            if (dim) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Widgets.DrawHighlightIfMouseover(row);
            Widgets.CheckboxLabeled(row, label, ref value);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(row, (key + "_Tip").Translate());
            y += RowHeight;
        }

        private void Accept()
        {
            foreach (var kv in bastle) BastleWorld.SetForGame(kv.Key, kv.Value);
            foreach (var kv in entad) EntadWorld.SetForGame(kv.Key, kv.Value);
            var game = DungeonGameComponent.Instance;
            if (game != null) game.worldConfigured = true;
            Close();
        }
    }
}
