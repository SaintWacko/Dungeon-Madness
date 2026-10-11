using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TitanFramework;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace ThisUsedToBeAboutDungeons
{
    public enum PackType : byte
    {
        Animal,
        Insect,
        Mech,
        Humanlike,
    }

    /// <summary>Marks a PawnKindDef as one the dungeon can fill furnished rooms with. Any mod can add it to its own kinds.</summary>
    public class DungeonDweller : DefModExtension
    {
    }

    /// <summary>
    /// One pack of creatures waiting in a chamber. Everything about it is decided when the map is generated (species,
    /// count, points, mad, stone, and a seed), but the pawns themselves are only made when the chamber is first seen,
    /// so a dungeon holds a handful of numbers per chamber rather than dozens of unspawned pawns. The seed makes the
    /// pack come out the same way whenever it wakes.
    /// </summary>
    public class DungeonPack : IExposable
    {
        public int chamber;
        public PackType type;
        public PawnKindDef kind;
        public int count;
        /// <summary>Titan points each member gets: the pack's total split between them, rounded up.</summary>
        public float points;
        public bool mad;
        public bool stone;
        /// <summary>The tile's stone, which a stone pack is made of three times in four.</summary>
        public ThingDef rock;
        public int seed;
        /// <summary>Floor cells of the chamber: where the pack appears, and the fog check.</summary>
        public List<IntVec3> cells = new List<IntVec3>();
        /// <summary>The chamber cell nearest its centre; what a pack with a lord guards.</summary>
        public IntVec3 home;
        public float radius;
        public bool woken;

        public void ExposeData()
        {
            Scribe_Values.Look(ref chamber, "chamber");
            Scribe_Values.Look(ref type, "type");
            Scribe_Defs.Look(ref kind, "kind");
            Scribe_Values.Look(ref count, "count");
            Scribe_Values.Look(ref points, "points");
            Scribe_Values.Look(ref mad, "mad");
            Scribe_Values.Look(ref stone, "stone");
            Scribe_Defs.Look(ref rock, "rock");
            Scribe_Values.Look(ref seed, "seed");
            Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
            Scribe_Values.Look(ref home, "home");
            Scribe_Values.Look(ref radius, "radius");
            Scribe_Values.Look(ref woken, "woken");
            if (cells == null) cells = new List<IntVec3>();
        }

        public override string ToString() =>
            $"chamber {chamber}: {count}x {kind?.defName} ({type}{(mad ? ", mad" : "")}{(stone ? ", stone" : "")}) at {points} pts each";
    }

    /// <summary>Holds a dungeon map's packs and wakes each one the first time any of its chamber is out of the fog.
    /// Present on every map; does nothing on maps that have no packs.</summary>
    public class MapComponent_DungeonCreatures : MapComponent
    {
        public List<DungeonPack> packs = new List<DungeonPack>();
        /// <summary>Sane dwellers who agreed to leave with the party, waiting by the entrance.</summary>
        public List<Pawn> followers = new List<Pawn>();
        /// <summary>Sane dwellers who said no; they cannot be asked again.</summary>
        public List<Pawn> refused = new List<Pawn>();

        /// <summary>When each hungry dweller first eyed a player pawn as prey. Not saved: a reload only means a fresh
        /// warning and a fresh grace period.</summary>
        public Dictionary<Pawn, int> eyeingSince = new Dictionary<Pawn, int>();
        /// <summary>The last tick each dweller eyed a player pawn, so the warning resets after a day of leaving them be.</summary>
        public Dictionary<Pawn, int> lastEyed = new Dictionary<Pawn, int>();

        public MapComponent_DungeonCreatures(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (packs.Count == 0 || !map.IsHashIntervalTick(60)) return;
            for (int i = 0; i < packs.Count; i++)
            {
                var pack = packs[i];
                if (pack.woken) continue;
                // Unfogging floods a whole connected space at once, so a few cells per chamber are enough to see it
                foreach (var c in pack.cells)
                    if (!map.fogGrid.IsFogged(c))
                    {
                        Wake(pack);
                        break;
                    }
            }
        }

        public void Wake(DungeonPack pack)
        {
            if (pack.woken) return;
            pack.woken = true;
            List<Pawn> pawns;
            try { pawns = DungeonCreatureGen.Make(pack, map); }
            catch (System.Exception e)
            {
                Log.Error($"[Dungeons] Could not make pack {pack}: {e}");
                return;
            }
            if (pawns.Count == 0) return;

            var free = pack.cells.Where(c => c.Standable(map)).InRandomOrder().ToList();
            foreach (var p in pawns)
            {
                IntVec3 cell;
                if (free.Count > 0) { cell = free[free.Count - 1]; free.RemoveAt(free.Count - 1); }
                else if (!CellFinder.TryFindRandomCellNear(pack.home, map, Mathf.CeilToInt(pack.radius) + 2, c => c.Standable(map), out cell))
                    cell = pack.home;
                GenSpawn.Spawn(p, cell, map, Rot4.Random);
            }

            if (pack.mad)
            {
                foreach (var p in pawns) p.health.AddHediff(DungeonDefOf.TUBAD_DungeonMad);
            }
            else if (pack.type != PackType.Animal)
            {
                // Mechs and insects guard their chamber for their faction. Sane dwellers have no faction, but a lord
                // keeps them home: without one, a factionless person's think tree keeps trying to walk off the map,
                // and a dungeon's edge is solid wall.
                Faction faction = pawns[0].Faction;
                var job = new LordJob_DefendPoint(pack.home, wanderRadius: Mathf.Max(3f, pack.radius * 0.7f), defendRadius: pack.radius + 8f,
                    isCaravanSendable: false, addFleeToil: false);
                LordMaker.MakeNewLord(faction, job, map, pawns);
            }
            if (Prefs.DevMode) Log.Message($"[Dungeons] Woke {pack}");
        }

        public void WakeAll()
        {
            foreach (var pack in packs) Wake(pack);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref packs, "packs", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // A dead or discarded pawn would be saved as a reference that cannot be resolved on load
                followers.RemoveAll(p => p == null || p.Destroyed || p.Dead);
                refused.RemoveAll(p => p == null || p.Destroyed || p.Dead);
            }
            Scribe_Collections.Look(ref followers, "followers", LookMode.Reference);
            Scribe_Collections.Look(ref refused, "refused", LookMode.Reference);
            if (packs == null) packs = new List<DungeonPack>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (followers == null) followers = new List<Pawn>();
                if (refused == null) refused = new List<Pawn>();
                followers.RemoveAll(p => p == null);
                refused.RemoveAll(p => p == null);
            }
        }
    }

    public static class DungeonCreatureGen
    {
        /// <summary>
        /// Decides every chamber's pack. Runs inside map generation's seeded Rand, so a party's dungeon is populated
        /// the same way every time it is generated from the same seed.
        /// </summary>
        public static void Plan(Map map, DungeonLayoutResult layout, DungeonThemeDef theme, ThingDef rockStuff)
        {
            var comp = map.GetComponent<MapComponent_DungeonCreatures>();
            if (comp == null) return;
            comp.packs.Clear();
            var s = DungeonsMod.Settings;
            var site = map.Parent as DungeonSite;
            float level = site == null ? 1f : Mathf.Max(1f, site.effectiveLevel > 0f ? site.effectiveLevel : site.level);
            float average = s.pointsPerLevel * level;
            float ceiling = s.PowerCeiling(level);

            // Floor cells by chamber: open, owned by the chamber, not a door or plug
            var byChamber = new Dictionary<int, List<IntVec3>>();
            for (int k = 0; k < layout.owner.Length; k++)
            {
                int id = layout.owner[k];
                if (id < 0 || !layout.open[k] || layout.feature[k] != CellFeature.None) continue;
                if (!byChamber.TryGetValue(id, out var list)) byChamber[id] = list = new List<IntVec3>();
                list.Add(new IntVec3(k % layout.size, 0, k / layout.size));
            }

            foreach (var ch in layout.data.chambers)
            {
                if (!Rand.Chance(ch.id == 0 ? s.entranceChance : s.occupancy)) continue;
                if (!byChamber.TryGetValue(ch.id, out var cells) || cells.Count == 0) continue;

                PackType type = PickType(ch, theme);
                PawnKindDef kind = PickKind(type, map.Biome, ceiling);
                if (kind == null && type != PackType.Animal)
                {
                    type = PackType.Animal;
                    kind = PickKind(type, map.Biome, ceiling);
                }
                if (kind == null) continue;

                int count = Mathf.Clamp(SizeRange(kind, type).RandomInRange, 1, s.maxPackSize);
                float total = Mathf.Max(0.01f, average * Rand.Range(1f - s.pointNoise, 1f + s.pointNoise));
                IntVec3 home = cells.MinBy(c => c.DistanceToSquared(ch.center));
                comp.packs.Add(new DungeonPack
                {
                    chamber = ch.id,
                    type = type,
                    kind = kind,
                    count = count,
                    points = Mathf.Ceil(total / count),
                    mad = (type == PackType.Animal || type == PackType.Humanlike) && Rand.Chance(s.madChance),
                    stone = type != PackType.Mech && Rand.Chance(s.stoneChance),
                    rock = rockStuff,
                    seed = Rand.Int,
                    cells = cells.InRandomOrder().Take(16).ToList(),
                    home = home,
                    radius = Mathf.Max(3f, ch.radius),
                });
            }
            if (Prefs.DevMode)
                Log.Message($"[Dungeons] Effective level {level:0.#}: {average:0.##} points per pack, species up to {ceiling:0} combat power. " +
                    $"{comp.packs.Count} packs: {string.Join("; ", comp.packs)}");
        }

        static PackType PickType(DungeonChamber ch, DungeonThemeDef theme)
        {
            if (ch.Furnished) return PackType.Humanlike;
            if (ch.use == RoomUse.Ruin && Faction.OfMechanoids != null && Rand.Chance(theme.mechChance)) return PackType.Mech;
            if (Faction.OfInsects != null && Rand.Chance(theme.insectChance)) return PackType.Insect;
            return PackType.Animal;
        }

        static IntRange SizeRange(PawnKindDef kind, PackType type)
        {
            switch (type)
            {
                case PackType.Insect: return new IntRange(Mathf.Max(2, kind.wildGroupSize.min), Mathf.Max(4, kind.wildGroupSize.max));
                case PackType.Mech: return new IntRange(1, 3);
                case PackType.Humanlike: return new IntRange(1, 4);
                default: return kind.wildGroupSize;
            }
        }

        /// <summary>A random species of the pack's type, no stronger than the ceiling. When every candidate is
        /// stronger, the weakest one, so a low ceiling still gets a pack.</summary>
        static PawnKindDef PickKind(PackType type, BiomeDef biome, float ceiling)
        {
            List<PawnKindDef> pool;
            System.Func<PawnKindDef, float> weight = k => 1f;
            switch (type)
            {
                case PackType.Animal:
                    pool = biome.AllWildAnimals.Where(k => k.RaceProps.Animal && k.RaceProps.animalType != AnimalType.Dryad && !k.RaceProps.IsAnomalyEntity && k.combatPower > 0f).Distinct().ToList();
                    weight = k => biome.CommonalityOfAnimal(k);
                    break;
                case PackType.Insect:
                    pool = DefDatabase<PawnKindDef>.AllDefs.Where(k => k.defaultFactionDef == FactionDefOf.Insect && k.RaceProps.Insect && !k.isBoss && k.combatPower > 0f).ToList();
                    break;
                case PackType.Mech:
                    pool = (FactionDefOf.Mechanoid.pawnGroupMakers ?? new List<PawnGroupMaker>())
                        .SelectMany(g => g.options ?? new List<PawnGenOption>()).Select(o => o.kind)
                        .Where(k => k != null && k.RaceProps.IsMechanoid && !k.isBoss && k.combatPower > 0f).Distinct().ToList();
                    break;
                default:
                    pool = DefDatabase<PawnKindDef>.AllDefs.Where(k => k.HasModExtension<DungeonDweller>() && k.RaceProps.Humanlike).ToList();
                    break;
            }
            if (pool.Count == 0) return null;
            var fits = pool.Where(k => k.combatPower <= ceiling).ToList();
            if (fits.Count == 0) return pool.MinBy(k => k.combatPower);
            return fits.TryRandomElementByWeight(k => Mathf.Max(0.001f, weight(k)), out var pick) ? pick : fits.RandomElement();
        }

        /// <summary>Makes the pack's pawns, unspawned. The first is rolled as a titan; the rest are copies of its
        /// titan (same traits at the same rarities, same material) on their own bodies, so sex, age and colouring
        /// still vary.</summary>
        public static List<Pawn> Make(DungeonPack pack, Map map)
        {
            var pawns = new List<Pawn>();
            Faction faction = null;
            if (pack.type == PackType.Insect) faction = Faction.OfInsects;
            else if (pack.type == PackType.Mech) faction = Faction.OfMechanoids;
            if (pack.kind == null || (pack.type == PackType.Insect || pack.type == PackType.Mech) && faction == null) return pawns;

            Rand.PushState(pack.seed);
            try
            {
                Pawn leader = null;
                for (int i = 0; i < pack.count; i++)
                {
                    var request = new PawnGenerationRequest(pack.kind, faction, PawnGenerationContext.NonPlayer, map.Tile,
                        forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowFood: pack.type == PackType.Humanlike,
                        allowAddictions: false);
                    Pawn p = PawnGenerator.GeneratePawn(request);
                    if (leader == null)
                    {
                        leader = p;
                        // Material first: rolling then skips traits a construct cannot have
                        if (pack.stone && !(pack.rock != null && Rand.Chance(0.75f) && TitanUtility.SetMaterial(p, pack.rock)))
                            TitanUtility.RollMaterial(p);
                        TitanUtility.RollTraits(p, pack.points);
                    }
                    else TitanUtility.CopyTitan(leader, p);
                    pawns.Add(p);
                }
            }
            finally { Rand.PopState(); }
            return pawns;
        }
    }
}
