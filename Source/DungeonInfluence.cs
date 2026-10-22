using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    // The rows of the Influence screen. Saved by index (DungeonInfluence.steps), so new kinds go on the end.
    public enum InfluenceKind { Level, PackSize, Occupancy, Madness, EntadChance, EntadValue, LootValue }

    // How the price of each further step away from normal grows. Marginal cost of the nth step, times the base step:
    // Fibonacci 1, 1, 2, 3, 5, 8...; Linear 1, 2, 3, 4...; Doubling 1, 2, 4, 8...
    public enum InfluenceCurve { Fibonacci, Linear, Doubling }

    /// <summary>
    /// Verity spent on one party's visit to a dungeon (the Influence screen on the entry dialog). Steps away from normal
    /// per row, plus an optional theme. Each row has its own cost ladder, so a little on every row is cheap and a lot on
    /// one is not. Prices come from the dungeon's own level (never the party's wealth), so they hold still while the
    /// party is being picked. The whole cost is spent when the party enters; cancelling the entry spends nothing, and a
    /// party lost inside does not get it back.
    /// </summary>
    public class DungeonInfluence : IExposable
    {
        // Past this many steps even linear costs are absurd; also keeps doubling inside a long
        public const int MaxSteps = 30;
        // Occupancy, madness and entad chance move this many percentage points a step
        public const float ChanceStep = 0.05f;
        // Entad value and loot value move this share of normal a step
        public const float ValueStep = 0.10f;

        public static readonly InfluenceKind[] Kinds = (InfluenceKind[])Enum.GetValues(typeof(InfluenceKind));

        private List<int> steps = new List<int>();
        /// <summary>The theme chosen for this visit; null keeps the tile's own.</summary>
        public DungeonThemeDef theme;

        /// <summary>No influence: what a site without one (or an older save) reads. Never modify it.</summary>
        public static readonly DungeonInfluence None = new DungeonInfluence();

        public DungeonInfluence() => Pad();

        private void Pad()
        {
            if (steps == null) steps = new List<int>();
            while (steps.Count < Kinds.Length) steps.Add(0);
        }

        public int this[InfluenceKind k]
        {
            get => steps[(int)k];
            set => steps[(int)k] = value;
        }

        public static DungeonInfluence Of(Map map) => (map?.Parent as DungeonSite)?.influence ?? None;

        public bool ThemeChanged(DungeonThemeDef normal) => theme != null && theme != normal;
        public bool AnySteps => steps.Any(s => s != 0);
        public bool Any(DungeonThemeDef normal) => AnySteps || ThemeChanged(normal);

        public void Reset()
        {
            for (int i = 0; i < steps.Count; i++) steps[i] = 0;
            theme = null;
        }

        /// <summary>Drops rows the settings have since switched off, so they are neither charged nor applied.</summary>
        public void DropDisabled()
        {
            foreach (var k in Kinds) if (!DungeonsMod.Settings.InfluenceRowEnabled(k)) this[k] = 0;
            if (!DungeonsMod.Settings.influenceTheme) theme = null;
        }

        // ---- Effects, read by entry, creature planning and loot

        public int Level(int baseLevel) => Mathf.Max(1, baseLevel + this[InfluenceKind.Level]);
        /// <summary>Added to a pack's size after the max-pack-size clamp, so it can go past it; never below 1.</summary>
        public int PackSize(int count) => Mathf.Max(1, count + this[InfluenceKind.PackSize]);
        public float Occupancy(float normal) => Mathf.Clamp01(normal + this[InfluenceKind.Occupancy] * ChanceStep);
        public float MadChance(float normal) => Mathf.Clamp01(normal + this[InfluenceKind.Madness] * ChanceStep);
        public float EntadChance(float normal) => Mathf.Clamp01(normal + this[InfluenceKind.EntadChance] * ChanceStep);
        public float EntadValueFactor => 1f + this[InfluenceKind.EntadValue] * ValueStep;
        public float LootValueFactor => 1f + this[InfluenceKind.LootValue] * ValueStep;

        // ---- Limits

        /// <summary>The steps a row may take, normal being 0. One-way rows stop at normal on their pointless side.</summary>
        public static IntRange Limits(InfluenceKind k, int baseLevel)
        {
            var s = DungeonsMod.Settings;
            switch (k)
            {
                case InfluenceKind.Level: return new IntRange(Mathf.Max(1 - baseLevel, -MaxSteps), MaxSteps);
                // Below max - 1 every pack is already 1; above the cap is allowed, the player chose it
                case InfluenceKind.PackSize: return new IntRange(-(s.maxPackSize - 1), MaxSteps);
                case InfluenceKind.Occupancy: return new IntRange(-ChanceStepsTo(s.occupancy, 0f), ChanceStepsTo(s.occupancy, 1f));
                case InfluenceKind.Madness: return new IntRange(-ChanceStepsTo(s.madChance, 0f), 0);
                case InfluenceKind.EntadChance: return new IntRange(0, ChanceStepsTo(s.entadChance, 1f));
                default: return new IntRange(0, MaxSteps); // entad value, loot value
            }
        }

        // Steps of ChanceStep from a chance to a bound, rounding up so the bound itself is reachable
        private static int ChanceStepsTo(float from, float to) =>
            Mathf.Min(MaxSteps, Mathf.CeilToInt(Mathf.Abs(to - from) / ChanceStep - 0.001f));

        // ---- Costs

        /// <summary>The price of one base step: the dungeon's own level / levels per step, rounded, at least 1.</summary>
        public static int BaseStep(int baseLevel) =>
            Mathf.Max(1, Mathf.RoundToInt(baseLevel / Mathf.Max(0.5f, DungeonsMod.Settings.influenceLevelsPerStep)));

        /// <summary>Marginal cost, in base steps, of the nth step away from normal (n from 1).</summary>
        public static long Marginal(int n)
        {
            if (n <= 0) return 0;
            switch (DungeonsMod.Settings.influenceCurve)
            {
                case InfluenceCurve.Linear: return n;
                case InfluenceCurve.Doubling: return 1L << Mathf.Min(n - 1, 40);
                default:
                    long a = 1, b = 1;
                    for (int i = 2; i < n; i++) { long c = a + b; a = b; b = c; }
                    return n <= 2 ? 1 : b;
            }
        }

        /// <summary>Everything a row costs at this many steps from normal, in either direction.</summary>
        public static long RowCost(int stepsAway, int baseStep)
        {
            long total = 0;
            for (int n = 1; n <= Mathf.Abs(stepsAway); n++) total += Marginal(n);
            return total * baseStep;
        }

        public static long ThemeFee(int baseStep) => (long)DungeonsMod.Settings.influenceThemeSteps * baseStep;
        public static long Fee(int baseStep) => (long)DungeonsMod.Settings.influenceFeeSteps * baseStep;

        /// <summary>The whole price: the fee when anything changes, every row's ladder, and the theme fee.</summary>
        public long TotalCost(int baseLevel, DungeonThemeDef normalTheme)
        {
            if (!Any(normalTheme)) return 0;
            int step = BaseStep(baseLevel);
            long total = Fee(step);
            foreach (var k in Kinds) total += RowCost(this[k], step);
            if (ThemeChanged(normalTheme)) total += ThemeFee(step);
            return total;
        }

        /// <summary>One line per change, for the entry dialog's tooltip and the entry letter.</summary>
        public IEnumerable<string> Describe(int baseLevel, DungeonThemeDef normalTheme)
        {
            foreach (var k in Kinds)
            {
                if (this[k] == 0) continue;
                yield return ("DMAD_Influence_Row_" + k).Translate() + ": " + ValueText(k, baseLevel);
            }
            if (ThemeChanged(normalTheme))
                yield return "DMAD_Influence_Theme".Translate() + ": " + theme.LabelCap;
        }

        /// <summary>A row's value as the player reads it, influenced.</summary>
        public string ValueText(InfluenceKind k, int baseLevel)
        {
            var s = DungeonsMod.Settings;
            switch (k)
            {
                case InfluenceKind.Level: return Level(baseLevel).ToString();
                case InfluenceKind.PackSize: return this[k].ToStringWithSign();
                case InfluenceKind.Occupancy: return Occupancy(s.occupancy).ToStringPercent();
                case InfluenceKind.Madness: return MadChance(s.madChance).ToStringPercent();
                case InfluenceKind.EntadChance: return EntadChance(s.entadChance).ToStringPercent();
                case InfluenceKind.EntadValue: return EntadValueFactor.ToStringPercent();
                default: return LootValueFactor.ToStringPercent();
            }
        }

        /// <summary>The same row at normal.</summary>
        public static string NormalText(InfluenceKind k, int baseLevel) => None.ValueText(k, baseLevel);

        public DungeonInfluence Copy()
        {
            var c = new DungeonInfluence { theme = theme };
            for (int i = 0; i < steps.Count && i < c.steps.Count; i++) c.steps[i] = steps[i];
            return c;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref steps, "steps", LookMode.Value);
            Scribe_Defs.Look(ref theme, "theme");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Pad();
        }
    }
}
