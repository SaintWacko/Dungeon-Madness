using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    public class DungeonsMod : Mod
    {
        public static DungeonSettings Settings;
        public static DungeonsMod Instance;

        public DungeonsMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<DungeonSettings>();
            new Harmony("saintwacko.ThisUsedToBeAboutDungeons").PatchAll();
        }

        public override string SettingsCategory() => "TUBAD_ModName".Translate();

        public override void WriteSettings()
        {
            base.WriteSettings();
            // Tint and visibility are baked into the world layer's mesh
            DungeonWorldComponent.SetLayerDirty();
        }

        private enum Tab { World }
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
                new TabRecord("TUBAD_Tab_World".Translate(), () => SetTab(Tab.World), tab == Tab.World),
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
            DoWorld(list);
            viewHeight = list.CurHeight + 20f;
            list.End();
            Widgets.EndScrollView();

            var reset = new Rect(inRect.x, inRect.yMax - 32f, 200f, 32f);
            if (Widgets.ButtonText(reset, "TUBAD_Settings_Reset".Translate())) Settings.ResetToDefaults();
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

        private static void DoWorld(Listing_Standard list)
        {
            var s = Settings;
            Section(list, "TUBAD_Settings_Display");
            list.CheckboxLabeled("TUBAD_Settings_ShowIcons".Translate(), ref s.showIcons, "TUBAD_Settings_ShowIcons_Tip".Translate());
            list.Label("TUBAD_Settings_IconOpacity".Translate(s.iconOpacity.ToStringPercent()));
            s.iconOpacity = Mathf.Round(list.Slider(s.iconOpacity, 0.1f, 1f) * 20f) / 20f;
            list.CheckboxLabeled("TUBAD_Settings_TintByLevel".Translate(), ref s.tintByLevel, "TUBAD_Settings_TintByLevel_Tip".Translate());
            list.Gap();

            Section(list, "TUBAD_Settings_Generation");
            list.Label("TUBAD_Settings_Generation_Desc".Translate());
            list.Gap(6f);
            list.Label("TUBAD_Settings_Spacing".Translate(s.spacing));
            s.spacing = Mathf.RoundToInt(list.Slider(s.spacing, 2f, 20f));
            list.Label("TUBAD_Settings_Spacing_Desc".Translate());
            list.Gap();
            list.Label("TUBAD_Settings_MaxLevel".Translate(s.maxLevel));
            s.maxLevel = Mathf.RoundToInt(list.Slider(s.maxLevel, 5f, 200f));
            list.Gap();
            list.Label("TUBAD_Settings_Skew".Translate(s.levelSkew.ToString("0.##")));
            s.levelSkew = Mathf.Round(list.Slider(s.levelSkew, 0.5f, 4f) * 20f) / 20f;
            int low = Mathf.Min(10, s.maxLevel);
            float lowFrac = Mathf.Pow((float)low / s.maxLevel, 1f / s.levelSkew);
            float topFrac = 1f - Mathf.Pow((s.maxLevel - 1f) / s.maxLevel, 1f / s.levelSkew);
            list.Label("TUBAD_Settings_Skew_Desc".Translate(low, lowFrac.ToStringPercent(), s.maxLevel, topFrac.ToStringPercent("0.#")));
            list.Gap();
            list.Label("TUBAD_Settings_Roughness".Translate(s.roughness.ToString("0.#")));
            s.roughness = Mathf.Round(list.Slider(s.roughness, 0.5f, 15f) * 2f) / 2f;
            list.Label("TUBAD_Settings_Roughness_Desc".Translate());
            list.Gap();
            list.CheckboxLabeled("TUBAD_Settings_Ocean".Translate(), ref s.oceanDungeons, "TUBAD_Settings_Ocean_Tip".Translate());
        }
    }
}
