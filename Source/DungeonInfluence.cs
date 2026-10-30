using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    // The rows of the Influence screen. DungeonInfluence.steps is saved by index, so new kinds go on the end; attuned
    // tiles save them by name, so a kind is never renamed.
    public enum InfluenceKind
    {
        Level, PackSize, Occupancy, Madness, EntadChance, EntadValue, LootValue,
        Animals, Insects, Mechs, BuiltRooms, FurnishedRooms, DungeonSize, LootChance,
        MeleeWeapons, RangedWeapons, Apparel, Furniture,
    }

    // How the price of each further step away from normal grows. Marginal cost of the nth step, times the base step:
    // Fibonacci 1, 1, 2, 3, 5, 8...; Linear 1, 2, 3, 4...; Doubling 1, 2, 4, 8...
    public enum InfluenceCurve { Fibonacci, Linear, Doubling }

    /// <summary>
    /// The dungeon an influence is priced and shown against: its tile, its own level, the theme it has unless the
    /// influence picks another, and the tile's hilliness factor on built rooms.
    /// </summary>
    public class InfluenceTarget
    {
        public readonly PlanetTile tile;
        public readonly int baseLevel;
        public readonly DungeonThemeDef normalTheme;
        public readonly float builtFactor;

        public InfluenceTarget(PlanetTile tile, int baseLevel)
        {
            this.tile = tile;
            this.baseLevel = baseLevel;
            normalTheme = DungeonThemeUtility.ThemeFor(tile);
            builtFactor = DungeonThemeUtility.HillinessBuiltFactor(Find.WorldGrid[tile].hilliness);
        }

        public int BaseStep => DungeonInfluence.BaseStep(baseLevel);
    }

    /// <summary>
    /// Verity spent on one party's visit to a dungeon (the Influence screen on the entry dialog). Steps away from normal
    /// per row, plus an optional theme. Each row has its own cost ladder, so a little on every row is cheap and a lot on
    /// one is not. Prices come from the dungeon's own level (never the party's wealth), so they hold still while the
    /// party is being picked. The whole cost is spent when the party enters; cancelling the entry spends nothing, and a
    /// party lost inside does not get it back.
    ///
    /// Unless the settings offer every row everywhere, a dungeon offers only level, theme and a fixed handful of rows
    /// picked for its tile, and the colony pays the fee once to attune to it before it can be influenced at all. The
    /// attuned rows are stored with the tile, so a row added in a later version never turns up on a tile attuned before.
    /// </summary>
    public class DungeonInfluence : IExposable
    {
        // Past this many steps even linear costs are absurd; also keeps doubling inside a long
        public const int MaxSteps = 30;
        // Chance rows move this many percentage points a step; creature rows move a type's weight by as much
        public const float ChanceStep = 0.05f;
        // Entad value and loot value move this share of normal a step
        public const float ValueStep = 0.10f;
        // Dungeon size moves the map's side by this many cells a step
        public const int SizeStep = 5;

        public static readonly InfluenceKind[] Kinds = (InfluenceKind[])Enum.GetValues(typeof(InfluenceKind));

        /// <summary>The order rows are shown in: the dungeon, its creatures, its loot.</summary>
        public static readonly InfluenceKind[] DisplayOrder =
        {
            InfluenceKind.Level, InfluenceKind.DungeonSize, InfluenceKind.BuiltRooms, InfluenceKind.FurnishedRooms,
            InfluenceKind.Occupancy, InfluenceKind.PackSize, InfluenceKind.Madness,
            InfluenceKind.Animals, InfluenceKind.Insects, InfluenceKind.Mechs,
            InfluenceKind.LootChance, InfluenceKind.LootValue, InfluenceKind.EntadChance, InfluenceKind.EntadValue,
            InfluenceKind.MeleeWeapons, InfluenceKind.RangedWeapons, InfluenceKind.Apparel, InfluenceKind.Furniture,
        };

        public static readonly InfluenceKind[] TypeRows =
            { InfluenceKind.MeleeWeapons, InfluenceKind.RangedWeapons, InfluenceKind.Apparel, InfluenceKind.Furniture };

        /// <summary>The optional rows a tile can be dealt: every row but level (level and theme are always offered).
        /// Each row is its own card, so a creature or item type row comes up as often as any other, but a tile takes at
        /// most one row of each group. Enum order, so a kind added on the end leaves the old cards' hashes alone.</summary>
        public static readonly InfluenceKind[] Deck = Kinds.Where(k => k != InfluenceKind.Level).ToArray();

        // Creature types and item types are exclusive groups; every other row is a group of its own
        public static int GroupOf(InfluenceKind k) => IsCreatureRow(k) ? -1 : IsTypeRow(k) ? -2 : (int)k;

        /// <summary>The most rows a tile can be dealt, one per group: the settings' row count at this means all.</summary>
        public static readonly int GroupCount = Deck.Select(GroupOf).Distinct().Count();

        public static bool IsCreatureRow(InfluenceKind k) => k == InfluenceKind.Animals || k == InfluenceKind.Insects || k == InfluenceKind.Mechs;
        public static bool IsTypeRow(InfluenceKind k) => k >= InfluenceKind.MeleeWeapons && k <= InfluenceKind.Furniture;

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
        public DungeonThemeDef ThemeOf(InfluenceTarget t) => theme ?? t.normalTheme;

        public void Reset()
        {
            for (int i = 0; i < steps.Count; i++) steps[i] = 0;
            theme = null;
        }

        // ---- Which rows a tile offers

        /// <summary>Whether dungeons offer a subset of rows and must be attuned to first. Off when the settings
        /// offer every slot, which is stage 1's behaviour: every row everywhere and the fee on each visit.</summary>
        public static bool UsesAttunement => DungeonsMod.Settings.influenceRowCount < GroupCount;

        public static bool NeedsAttuning(PlanetTile tile) =>
            UsesAttunement && !(DungeonWorldComponent.Instance?.IsAttuned(tile.tileId) ?? false);

        /// <summary>Whether a row can do anything in this game: no insect faction, no insects.</summary>
        public static bool Available(InfluenceKind k)
        {
            // The settings page can be open at the main menu, with no factions to ask
            if (Current.Game?.World == null) return true;
            if (k == InfluenceKind.Insects) return Faction.OfInsects != null;
            if (k == InfluenceKind.Mechs) return Faction.OfMechanoids != null;
            return true;
        }

        private static bool Usable(InfluenceKind k) => DungeonsMod.Settings.InfluenceRowEnabled(k) && Available(k);

        /// <summary>The rows dealt to a tile when it is attuned: the settings' count of cards from the deck, shuffled by
        /// a hash of the world, the tile and the card, skipping rows that are switched off and rows whose group the
        /// tile already holds.</summary>
        public static List<InfluenceKind> RollRows(PlanetTile tile)
        {
            int tileSeed = Gen.HashCombineInt(Gen.HashCombineInt(Find.World.info.Seed, tile.tileId ^ 0x1F7A), tile.Layer?.LayerID ?? 0);
            var order = Enumerable.Range(0, Deck.Length).OrderBy(i => Gen.HashCombineInt(tileSeed, i)).ThenBy(i => i);
            var rows = new List<InfluenceKind>();
            var groups = new HashSet<int>();
            int want = DungeonsMod.Settings.influenceRowCount;
            foreach (int i in order)
            {
                if (rows.Count >= want) break;
                var k = Deck[i];
                if (!Usable(k) || !groups.Add(GroupOf(k))) continue;
                rows.Add(k);
            }
            return rows;
        }

        /// <summary>The rows the Influence screen shows for a tile, in display order. Rows switched off in the settings
        /// since attuning are left out.</summary>
        public static List<InfluenceKind> OfferedRows(PlanetTile tile)
        {
            if (!UsesAttunement) return DisplayOrder.Where(Usable).ToList();
            var attuned = DungeonWorldComponent.Instance?.AttunedRows(tile.tileId);
            return DisplayOrder.Where(k => Usable(k) && (k == InfluenceKind.Level || attuned != null && attuned.Contains(k))).ToList();
        }

        /// <summary>An attuned tile's own rows as a short list ("pack size, insects"), or null when it is not attuned.</summary>
        public static string AttunedSummary(PlanetTile tile)
        {
            if (!UsesAttunement) return null;
            var attuned = DungeonWorldComponent.Instance?.AttunedRows(tile.tileId);
            if (attuned == null) return null;
            var labels = DisplayOrder.Where(k => attuned.Contains(k) && Usable(k)).Select(k => ("DMAD_Influence_Row_" + k).Translate().ToString().UncapitalizeFirst()).ToList();
            return labels.Count == 0 ? "DMAD_Attuned_NoRows".Translate().ToString() : string.Join(", ", labels);
        }

        /// <summary>Drops rows the tile does not offer (or the settings have since switched off), so they are neither
        /// charged nor applied, and pulls the rest back inside their limits.</summary>
        public void Restrict(InfluenceTarget t)
        {
            var offered = OfferedRows(t.tile);
            foreach (var k in Kinds) if (!offered.Contains(k)) this[k] = 0;
            if (!DungeonsMod.Settings.influenceTheme) theme = null;
            ClampToLimits(t);
        }

        /// <summary>Another theme or level moves some rows' limits (built rooms, creature weights, size); a row past
        /// its new limit would be charged for steps that do nothing.</summary>
        public void ClampToLimits(InfluenceTarget t)
        {
            // Level first: the size row's limit depends on it
            foreach (var k in Kinds.OrderBy(k => k != InfluenceKind.Level))
            {
                var lim = Limits(k, t);
                this[k] = Mathf.Clamp(this[k], lim.min, lim.max);
            }
        }

        // ---- Effects, read by entry, layout, creature planning and loot

        public int Level(int baseLevel) => Mathf.Max(1, baseLevel + this[InfluenceKind.Level]);
        /// <summary>Added to a pack's size after the max-pack-size clamp, so it can go past it; never below 1.</summary>
        public int PackSize(int count) => Mathf.Max(1, count + this[InfluenceKind.PackSize]);
        public float Occupancy(float normal) => Chance(normal, InfluenceKind.Occupancy);
        public float MadChance(float normal) => Chance(normal, InfluenceKind.Madness);
        public float EntadChance(float normal) => Chance(normal, InfluenceKind.EntadChance);
        public float EntadValueFactor => 1f + this[InfluenceKind.EntadValue] * ValueStep;
        public float LootValueFactor => 1f + this[InfluenceKind.LootValue] * ValueStep;
        public float BuiltChance(float normal) => Chance(normal, InfluenceKind.BuiltRooms);
        public float FurnishedChance(float normal) => Chance(normal, InfluenceKind.FurnishedRooms);
        public float LootChance(float normal) => Chance(normal, InfluenceKind.LootChance);

        /// <summary>How many times as likely an item type is as normal, for entads and ordinary loot alike; 0 at the
        /// bottom, which means never.</summary>
        public float TypeFactor(InfluenceKind k) => Mathf.Max(0f, 1f + this[k] * ValueStep);
        public bool AnyTypeSteps => TypeRows.Any(k => this[k] != 0);
        /// <summary>Paying for more furniture guarantees a furnished room, the only place entad furniture can be.</summary>
        public bool NeedsFurnishedRoom => this[InfluenceKind.Furniture] > 0;
        /// <summary>No upper bound: the player paid for it. Never below the smallest dungeon.</summary>
        public IntVec3 MapSize(IntVec3 normal)
        {
            int side = Mathf.Max(DungeonEntry.MinMapSize, normal.x + this[InfluenceKind.DungeonSize] * SizeStep);
            return new IntVec3(side, 1, side);
        }

        private float Chance(float normal, InfluenceKind k) => Mathf.Clamp01(normal + this[k] * ChanceStep);

        /// <summary>
        /// How likely each creature type is for a pack in a natural chamber or a ruin (furnished rooms always hold
        /// people). Normal weights are the theme's: mechs at its mech chance in ruins only, insects at its insect
        /// chance of the rest, animals whatever is left. Each creature row step adds ChanceStep to its type's weight
        /// everywhere, so influenced mechs turn up in caverns too. A type whose faction is missing weighs nothing.
        /// </summary>
        public void CreatureWeights(DungeonThemeDef theme, bool ruin, out float animal, out float insect, out float mech)
        {
            float m = ruin ? Mathf.Clamp01(theme.mechChance) : 0f;
            float i = (1f - m) * Mathf.Clamp01(theme.insectChance);
            animal = Mathf.Max(0f, 1f - m - i + this[InfluenceKind.Animals] * ChanceStep);
            insect = Faction.OfInsects == null ? 0f : Mathf.Max(0f, i + this[InfluenceKind.Insects] * ChanceStep);
            mech = Faction.OfMechanoids == null ? 0f : Mathf.Max(0f, m + this[InfluenceKind.Mechs] * ChanceStep);
        }

        private float CreatureShare(InfluenceKind k, DungeonThemeDef theme, bool ruin)
        {
            CreatureWeights(theme, ruin, out float a, out float i, out float m);
            float total = a + i + m;
            if (total <= 0f) return k == InfluenceKind.Animals ? 1f : 0f;
            return (k == InfluenceKind.Animals ? a : k == InfluenceKind.Insects ? i : m) / total;
        }

        // The theme's own weight for a creature type, the larger of caverns and ruins: steps down to zero everywhere
        private static float NormalCreatureWeight(InfluenceKind k, DungeonThemeDef theme)
        {
            None.CreatureWeights(theme, false, out float a0, out float i0, out float m0);
            None.CreatureWeights(theme, true, out float a1, out float i1, out float m1);
            return k == InfluenceKind.Animals ? Mathf.Max(a0, a1) : k == InfluenceKind.Insects ? Mathf.Max(i0, i1) : Mathf.Max(m0, m1);
        }

        private static float NormalBuilt(InfluenceTarget t, DungeonThemeDef theme) => Mathf.Clamp01(theme.builtChance * t.builtFactor);

        private int NormalSide(InfluenceTarget t) => DungeonEntry.MapSizeFor(Level(t.baseLevel)).x;

        // ---- Limits

        /// <summary>The steps a row may take, normal being 0. One-way rows stop at normal on their pointless side.</summary>
        public IntRange Limits(InfluenceKind k, InfluenceTarget t)
        {
            var s = DungeonsMod.Settings;
            var th = ThemeOf(t);
            switch (k)
            {
                case InfluenceKind.Level: return new IntRange(Mathf.Max(1 - t.baseLevel, -MaxSteps), MaxSteps);
                // Below max - 1 every pack is already 1; above the cap is allowed, the player chose it
                case InfluenceKind.PackSize: return new IntRange(-(s.maxPackSize - 1), MaxSteps);
                case InfluenceKind.Occupancy: return ChanceLimits(s.occupancy);
                case InfluenceKind.Madness: return new IntRange(-ChanceStepsTo(s.madChance, 0f), 0);
                case InfluenceKind.EntadChance: return new IntRange(0, ChanceStepsTo(s.entadChance, 1f));
                case InfluenceKind.Animals:
                case InfluenceKind.Insects:
                case InfluenceKind.Mechs: return new IntRange(-ChanceStepsTo(NormalCreatureWeight(k, th), 0f), MaxSteps);
                case InfluenceKind.BuiltRooms: return ChanceLimits(NormalBuilt(t, th));
                case InfluenceKind.FurnishedRooms: return ChanceLimits(Mathf.Clamp01(th.furnishedChance));
                case InfluenceKind.DungeonSize:
                    return new IntRange(-Mathf.CeilToInt((NormalSide(t) - DungeonEntry.MinMapSize) / (float)SizeStep), MaxSteps);
                case InfluenceKind.LootChance: return ChanceLimits(s.lootChance);
                case InfluenceKind.MeleeWeapons:
                case InfluenceKind.RangedWeapons:
                case InfluenceKind.Apparel:
                case InfluenceKind.Furniture: return new IntRange(-Mathf.RoundToInt(1f / ValueStep), MaxSteps);
                default: return new IntRange(0, MaxSteps); // entad value, loot value
            }
        }

        private static IntRange ChanceLimits(float normal) => new IntRange(-ChanceStepsTo(normal, 0f), ChanceStepsTo(normal, 1f));

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
        /// <summary>The influence fee: paid once to attune to a tile, or on every influenced visit when every row is
        /// offered everywhere.</summary>
        public static long Fee(int baseStep) => (long)DungeonsMod.Settings.influenceFeeSteps * baseStep;
        /// <summary>The fee this visit pays when anything is influenced: none on an attuned tile.</summary>
        public static long VisitFee(int baseStep) => UsesAttunement ? 0 : Fee(baseStep);

        /// <summary>The whole price: the visit fee when anything changes, every row's ladder, and the theme fee.</summary>
        public long TotalCost(InfluenceTarget t)
        {
            if (!Any(t.normalTheme)) return 0;
            int step = t.BaseStep;
            long total = VisitFee(step);
            foreach (var k in Kinds) total += RowCost(this[k], step);
            if (ThemeChanged(t.normalTheme)) total += ThemeFee(step);
            return total;
        }

        /// <summary>One line per change, for the entry dialog's tooltip and the entry letter.</summary>
        public IEnumerable<string> Describe(InfluenceTarget t)
        {
            foreach (var k in DisplayOrder)
            {
                if (this[k] == 0) continue;
                yield return ("DMAD_Influence_Row_" + k).Translate() + ": " + ValueText(k, t);
            }
            if (ThemeChanged(t.normalTheme))
                yield return "DMAD_Influence_Theme".Translate() + ": " + theme.LabelCap;
        }

        /// <summary>A row's value as the player reads it, influenced.</summary>
        public string ValueText(InfluenceKind k, InfluenceTarget t)
        {
            var s = DungeonsMod.Settings;
            var th = ThemeOf(t);
            switch (k)
            {
                case InfluenceKind.Level: return Level(t.baseLevel).ToString();
                case InfluenceKind.PackSize: return this[k].ToStringWithSign();
                case InfluenceKind.Occupancy: return Occupancy(s.occupancy).ToStringPercent();
                case InfluenceKind.Madness: return MadChance(s.madChance).ToStringPercent();
                case InfluenceKind.EntadChance: return EntadChance(s.entadChance).ToStringPercent();
                case InfluenceKind.EntadValue: return EntadValueFactor.ToStringPercent();
                case InfluenceKind.LootValue: return LootValueFactor.ToStringPercent();
                case InfluenceKind.Animals:
                case InfluenceKind.Insects:
                case InfluenceKind.Mechs:
                    return "DMAD_Influence_Shares".Translate(CreatureShare(k, th, false).ToStringPercent(), CreatureShare(k, th, true).ToStringPercent());
                case InfluenceKind.BuiltRooms: return BuiltChance(NormalBuilt(t, th)).ToStringPercent();
                case InfluenceKind.FurnishedRooms: return FurnishedChance(Mathf.Clamp01(th.furnishedChance)).ToStringPercent();
                case InfluenceKind.DungeonSize:
                    int side = MapSize(new IntVec3(NormalSide(t), 1, NormalSide(t))).x;
                    return side + "x" + side;
                case InfluenceKind.LootChance: return LootChance(s.lootChance).ToStringPercent();
                default:
                    if (TypeFactor(k) <= 0f) return "DMAD_Influence_Never".Translate();
                    return (this[k] > 0 ? "+" : "") + (this[k] * ValueStep).ToStringPercent();
            }
        }

        /// <summary>The same row at normal, under the chosen theme and level: what the row's own steps change.</summary>
        public string NormalText(InfluenceKind k, InfluenceTarget t)
        {
            var normal = Copy();
            if (IsCreatureRow(k))
            {
                normal[InfluenceKind.Animals] = 0;
                normal[InfluenceKind.Insects] = 0;
                normal[InfluenceKind.Mechs] = 0;
            }
            else normal[k] = 0;
            return normal.ValueText(k, t);
        }

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
