using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    public class DungeonsMod : Mod
    {
        public static DungeonSettings Settings;
        public static DungeonsMod Instance;

        public DungeonsMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<DungeonSettings>();
            new Harmony("saintwacko.DungeonMadness").PatchAll();
        }

        public override string SettingsCategory() => "DMAD_ModName".Translate();

        public override void WriteSettings()
        {
            base.WriteSettings();
            // Tint and visibility are baked into the world layer's mesh
            DungeonWorldComponent.SetLayerDirty();
        }

        private enum Tab { World, Parties, Creatures, Loot, Influence }
        private Tab tab;
        private Vector2 scroll;
        private float viewHeight = 900f;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // TabDrawer draws the tab strip above the rect it is given, so the body starts one tab height down,
            // and the bottom 40 px are kept for the reset button.
            var body = new Rect(inRect.x, inRect.y + TabDrawer.TabHeight, inRect.width, inRect.height - TabDrawer.TabHeight - 40f);
            var tabs = new List<TabRecord>
            {
                new TabRecord("DMAD_Tab_World".Translate(), () => SetTab(Tab.World), tab == Tab.World),
                new TabRecord("DMAD_Tab_Parties".Translate(), () => SetTab(Tab.Parties), tab == Tab.Parties),
                new TabRecord("DMAD_Tab_Creatures".Translate(), () => SetTab(Tab.Creatures), tab == Tab.Creatures),
                new TabRecord("DMAD_Tab_Loot".Translate(), () => SetTab(Tab.Loot), tab == Tab.Loot),
                new TabRecord("DMAD_Tab_Influence".Translate(), () => SetTab(Tab.Influence), tab == Tab.Influence),
            };
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);

            Rect inner = body.ContractedBy(10f);
            var view = new Rect(0f, 0f, inner.width - 16f, viewHeight);
            Widgets.BeginScrollView(inner, ref scroll, view);
            // maxOneColumn: the view is sized from last frame's content, so right after switching to a taller tab
            // the listing overruns it for one frame. Without this, Listing wraps the overflow into a second column
            // off to the right, CurHeight measures only that column and the view never grows back.
            var list = new Listing_Standard { maxOneColumn = true };
            list.Begin(view);
            if (tab == Tab.World) DoWorld(list);
            else if (tab == Tab.Parties) DoParties(list);
            else if (tab == Tab.Creatures) DoCreatures(list);
            else if (tab == Tab.Loot) DoLoot(list);
            else DoInfluence(list);
            viewHeight = list.CurHeight + 20f;
            list.End();
            Widgets.EndScrollView();

            var reset = new Rect(inRect.x, inRect.yMax - 32f, 200f, 32f);
            if (Widgets.ButtonText(reset, "DMAD_Settings_Reset".Translate())) Settings.ResetToDefaults();
        }

        private void SetTab(Tab t)
        {
            tab = t;
            scroll = Vector2.zero;
        }

        private static void Section(Listing_Standard list, string key)
        {
            Text.Font = GameFont.Medium;
            list.Label(key.Translate());
            Text.Font = GameFont.Small;
            list.GapLine(4f);
        }

        private static void DoParties(Listing_Standard list)
        {
            var s = Settings;
            list.Label("DMAD_Settings_PartySize".Translate(s.maxPartySize));
            s.maxPartySize = Mathf.RoundToInt(list.Slider(s.maxPartySize, 1f, 20f));
            list.Label("DMAD_Settings_PartySize_Desc".Translate());
            list.Gap();
            list.Label("DMAD_Settings_WealthToDouble".Translate(s.wealthToDouble.ToStringMoney()));
            s.wealthToDouble = Mathf.Round(list.Slider(s.wealthToDouble, 5000f, 300000f) / 1000f) * 1000f;
            float example = 10f * (1f + 20000f / s.wealthToDouble);
            list.Label("DMAD_Settings_WealthToDouble_Desc".Translate(20000f.ToStringMoney(), example.ToString("0.#")));
        }

        private static void DoCreatures(Listing_Standard list)
        {
            var s = Settings;
            list.Label("DMAD_Settings_Creatures_Desc".Translate());
            list.Gap();

            Section(list, "DMAD_Settings_Packs");
            list.Label("DMAD_Settings_Occupancy".Translate(s.occupancy.ToStringPercent()));
            s.occupancy = Mathf.Round(list.Slider(s.occupancy, 0f, 1f) * 20f) / 20f;
            list.Label("DMAD_Settings_EntranceChance".Translate(s.entranceChance.ToStringPercent()));
            s.entranceChance = Mathf.Round(list.Slider(s.entranceChance, 0f, 1f) * 20f) / 20f;
            list.Label("DMAD_Settings_MaxPackSize".Translate(s.maxPackSize));
            s.maxPackSize = Mathf.RoundToInt(list.Slider(s.maxPackSize, 1f, 12f));
            list.Label("DMAD_Settings_MaxPackSize_Desc".Translate());
            list.Gap();
            list.Label("DMAD_Settings_MadChance".Translate(s.madChance.ToStringPercent()));
            s.madChance = Mathf.Round(list.Slider(s.madChance, 0f, 1f) * 20f) / 20f;
            list.Label("DMAD_Settings_MadChance_Desc".Translate());
            list.Gap();
            list.Label("DMAD_Settings_StoneChance".Translate(s.stoneChance.ToStringPercent()));
            s.stoneChance = Mathf.Round(list.Slider(s.stoneChance, 0f, 1f) * 20f) / 20f;
            list.Label("DMAD_Settings_StoneChance_Desc".Translate());
            list.Gap();

            Section(list, "DMAD_Settings_Dwellers");
            list.Label("DMAD_Settings_InviteChance".Translate(s.inviteChance.ToStringPercent()));
            s.inviteChance = Mathf.Round(list.Slider(s.inviteChance, 0f, 1f) * 20f) / 20f;
            list.Label("DMAD_Settings_InviteChance_Desc".Translate());
            list.Gap();
            list.Label("DMAD_Settings_JoinChance".Translate(s.joinChance.ToStringPercent()));
            s.joinChance = Mathf.Round(list.Slider(s.joinChance, 0f, 1f) * 20f) / 20f;
            list.Label("DMAD_Settings_JoinChance_Desc".Translate());
            list.Gap();

            Section(list, "DMAD_Settings_Strength");
            list.Label("DMAD_Settings_PointsPerLevel".Translate(s.pointsPerLevel.ToString("0.##")));
            s.pointsPerLevel = Mathf.Round(list.Slider(s.pointsPerLevel, 0.05f, 2f) * 20f) / 20f;
            list.Label("DMAD_Settings_PointsPerLevel_Desc".Translate((10f * s.pointsPerLevel).ToString("0.#"), Mathf.CeilToInt(10f * s.pointsPerLevel / 4f)));
            list.Gap();
            list.Label("DMAD_Settings_PointNoise".Translate(s.pointNoise.ToStringPercent()));
            s.pointNoise = Mathf.Round(list.Slider(s.pointNoise, 0f, 0.5f) * 20f) / 20f;
            list.Gap();
            list.Label("DMAD_Settings_PowerBase".Translate(s.powerBase.ToString("0")));
            s.powerBase = Mathf.Round(list.Slider(s.powerBase, 20f, 500f) / 5f) * 5f;
            list.Label("DMAD_Settings_PowerPerLevel".Translate(s.powerPerLevel.ToString("0.#")));
            s.powerPerLevel = Mathf.Round(list.Slider(s.powerPerLevel, 0f, 50f) * 2f) / 2f;
            list.Label("DMAD_Settings_Power_Desc".Translate(s.PowerCeiling(1f).ToString("0"), s.PowerCeiling(10f).ToString("0"), s.PowerCeiling(30f).ToString("0")));
        }

        private static void DoLoot(Listing_Standard list)
        {
            var s = Settings;
            list.Label("DMAD_Settings_Loot_Desc".Translate());
            list.Gap();

            Section(list, "DMAD_Settings_LootPiles");
            list.Label("DMAD_Settings_LootChance".Translate(s.lootChance.ToStringPercent()));
            s.lootChance = Mathf.Round(list.Slider(s.lootChance, 0f, 1f) * 20f) / 20f;
            list.Label("DMAD_Settings_LootValue".Translate(s.lootValuePerLevel.ToStringMoney()));
            s.lootValuePerLevel = Mathf.Round(list.Slider(s.lootValuePerLevel, 0f, 500f) / 5f) * 5f;
            list.Label("DMAD_Settings_LootValue_Desc".Translate((10f * s.lootValuePerLevel).ToStringMoney()));
            list.Gap();
            list.Label("DMAD_Settings_LootRoll".Translate(s.lootRollValuePerLevel.ToStringMoney()));
            s.lootRollValuePerLevel = Mathf.Round(list.Slider(s.lootRollValuePerLevel, 20f, 1000f) / 10f) * 10f;
            list.Label("DMAD_Settings_LootRoll_Desc".Translate((10f * s.lootRollValuePerLevel).ToStringMoney()));
            list.Gap();

            Section(list, "DMAD_Settings_Entads");
            list.Label("DMAD_Settings_EntadChance".Translate(s.entadChance.ToStringPercent()));
            s.entadChance = Mathf.Round(list.Slider(s.entadChance, 0f, 1f) * 60f) / 60f;
            list.Label("DMAD_Settings_EntadChance_Desc".Translate());
            list.Gap();
            list.Label("DMAD_Settings_EntadPoints".Translate(s.entadPointsPerLevel.ToString("0.##")));
            s.entadPointsPerLevel = Mathf.Round(list.Slider(s.entadPointsPerLevel, 0.05f, 2f) * 20f) / 20f;
            list.Label("DMAD_Settings_EntadPoints_Desc".Translate((10f * s.entadPointsPerLevel).ToString("0.#")));
            list.Gap();

            Section(list, "DMAD_Settings_Binding");
            list.Label("DMAD_Settings_BindSingle".Translate(s.bindSingleChance.ToStringPercent()));
            s.bindSingleChance = Mathf.Round(list.Slider(s.bindSingleChance, 0f, 1f) * 20f) / 20f;
            list.Label("DMAD_Settings_BindParty".Translate(s.bindPartyChance.ToStringPercent()));
            s.bindPartyChance = Mathf.Round(list.Slider(s.bindPartyChance, 0f, 1f) * 20f) / 20f;
            s.bindPartyChance = Mathf.Min(s.bindPartyChance, 1f - s.bindSingleChance);
            list.Label("DMAD_Settings_BindNone".Translate(Mathf.Max(0f, 1f - s.bindSingleChance - s.bindPartyChance).ToStringPercent()));
            list.Label("DMAD_Settings_Binding_Desc".Translate());
        }

        private static void DoInfluence(Listing_Standard list)
        {
            var s = Settings;
            list.CheckboxLabeled("DMAD_Settings_InfluenceEnabled".Translate(), ref s.influenceEnabled, "DMAD_Settings_InfluenceEnabled_Tip".Translate());
            list.Gap();

            Section(list, "DMAD_Settings_InfluenceCosts");
            if (list.ButtonTextLabeled("DMAD_Settings_InfluenceCurve".Translate(), ("DMAD_Curve_" + s.influenceCurve).Translate()))
            {
                var options = new List<FloatMenuOption>();
                foreach (InfluenceCurve c in System.Enum.GetValues(typeof(InfluenceCurve)))
                {
                    var pick = c;
                    options.Add(new FloatMenuOption(("DMAD_Curve_" + c).Translate(), () => s.influenceCurve = pick));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            list.Label("DMAD_Settings_InfluenceCurve_Desc".Translate(string.Join(", ", System.Linq.Enumerable.Range(1, 8).Select(n => DungeonInfluence.Marginal(n).ToString())),
                DungeonInfluence.RowCost(5, 1), DungeonInfluence.RowCost(10, 1)));
            list.Gap();
            list.Label("DMAD_Settings_InfluenceLevelsPerStep".Translate(s.influenceLevelsPerStep.ToString("0.#")));
            s.influenceLevelsPerStep = Mathf.Round(list.Slider(s.influenceLevelsPerStep, 1f, 30f) * 2f) / 2f;
            list.Label("DMAD_Settings_InfluenceLevelsPerStep_Desc".Translate(DungeonInfluence.BaseStep(5), DungeonInfluence.BaseStep(12), DungeonInfluence.BaseStep(30)));
            list.Gap();
            list.Label("DMAD_Settings_InfluenceFee".Translate(s.influenceFeeSteps));
            s.influenceFeeSteps = Mathf.RoundToInt(list.Slider(s.influenceFeeSteps, 0f, 20f));
            list.Label("DMAD_Settings_InfluenceFee_Desc".Translate());
            list.Label("DMAD_Settings_InfluenceThemeFee".Translate(s.influenceThemeSteps));
            s.influenceThemeSteps = Mathf.RoundToInt(list.Slider(s.influenceThemeSteps, 0f, 30f));
            list.Gap();

            Section(list, "DMAD_Settings_InfluenceRows");
            int slots = DungeonInfluence.Slots.Length;
            list.Label("DMAD_Settings_InfluenceRowCount".Translate(s.influenceRowCount >= slots
                ? "DMAD_Settings_InfluenceRowCount_All".Translate().ToString() : s.influenceRowCount.ToString()));
            s.influenceRowCount = Mathf.RoundToInt(list.Slider(s.influenceRowCount, 0f, slots));
            list.Label("DMAD_Settings_InfluenceRowCount_Desc".Translate());
            list.Gap();
            list.Label("DMAD_Settings_InfluenceRows_Desc".Translate());
            foreach (var k in DungeonInfluence.DisplayOrder)
            {
                bool on = s.InfluenceRowEnabled(k);
                list.CheckboxLabeled(("DMAD_Influence_Row_" + k).Translate(), ref on, ("DMAD_Influence_Row_" + k + "_Tip").Translate());
                if (on) s.influenceDisabled.Remove(k);
                else if (!s.influenceDisabled.Contains(k)) s.influenceDisabled.Add(k);
            }
            list.CheckboxLabeled("DMAD_Influence_Theme".Translate(), ref s.influenceTheme, "DMAD_Influence_Theme_Tip".Translate());
        }

        private static void DoWorld(Listing_Standard list)
        {
            var s = Settings;
            Section(list, "DMAD_Settings_Display");
            list.CheckboxLabeled("DMAD_Settings_ShowIcons".Translate(), ref s.showIcons, "DMAD_Settings_ShowIcons_Tip".Translate());
            list.Label("DMAD_Settings_IconOpacity".Translate(s.iconOpacity.ToStringPercent()));
            s.iconOpacity = Mathf.Round(list.Slider(s.iconOpacity, 0.1f, 1f) * 20f) / 20f;
            list.CheckboxLabeled("DMAD_Settings_TintByLevel".Translate(), ref s.tintByLevel, "DMAD_Settings_TintByLevel_Tip".Translate());
            list.Gap();

            Section(list, "DMAD_Settings_Generation");
            list.Label("DMAD_Settings_Generation_Desc".Translate());
            list.Gap(6f);
            list.Label("DMAD_Settings_Spacing".Translate(s.spacing));
            s.spacing = Mathf.RoundToInt(list.Slider(s.spacing, 2f, 20f));
            list.Label("DMAD_Settings_Spacing_Desc".Translate());
            list.Gap();
            list.Label("DMAD_Settings_MaxLevel".Translate(s.maxLevel));
            s.maxLevel = Mathf.RoundToInt(list.Slider(s.maxLevel, 5f, 200f));
            list.Gap();
            list.Label("DMAD_Settings_Skew".Translate(s.levelSkew.ToString("0.##")));
            s.levelSkew = Mathf.Round(list.Slider(s.levelSkew, 0.5f, 4f) * 20f) / 20f;
            int low = Mathf.Min(10, s.maxLevel);
            float lowFrac = Mathf.Pow((float)low / s.maxLevel, 1f / s.levelSkew);
            float topFrac = 1f - Mathf.Pow((s.maxLevel - 1f) / s.maxLevel, 1f / s.levelSkew);
            list.Label("DMAD_Settings_Skew_Desc".Translate(low, lowFrac.ToStringPercent(), s.maxLevel, topFrac.ToStringPercent("0.#")));
            list.Gap();
            list.Label("DMAD_Settings_Roughness".Translate(s.roughness.ToString("0.#")));
            s.roughness = Mathf.Round(list.Slider(s.roughness, 0.5f, 15f) * 2f) / 2f;
            list.Label("DMAD_Settings_Roughness_Desc".Translate());
        }
    }
}
