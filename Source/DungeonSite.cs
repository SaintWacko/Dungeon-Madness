using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    /// <summary>
    /// The world object for one dungeon instance: made when a party enters, destroyed with its map when the party
    /// has left or died. The next party at this dungeon gets a fresh one. It sits on the dungeon's own tile, so the
    /// map takes that tile's biome and temperature.
    /// </summary>
    [StaticConstructorOnStartup]
    public class DungeonSite : MapParent, IThingHolder
    {
        public int level;
        public float partyWealth;
        public float effectiveLevel;

        // Rolled when the party enters so each party gets a different layout from the same tile
        public int layoutSeed;
        // Chambers and dividers, filled in by map generation; phase 4 places creatures with it
        public DungeonLayoutData layout;
        // The theme this visit was generated with. Every visit makes a new site, so this is only for the visit;
        // "same tile, same theme" comes from DungeonThemeUtility.ThemeFor's seed, not from this field
        public DungeonThemeDef theme;

        // Items left at the entrance when nobody stayed outside, and items carried out through the exit before any
        // pawn came out to take them. Handed to the caravan on the tile as soon as one exists.
        private ThingOwner<Thing> stash;

        private bool everyoneLeft;

        // The humanlikes who entered together (not dwellers who joined later): who a party-bound entad binds to
        public List<Pawn> party = new List<Pawn>();
        // Entads made for this dungeon that have not left it yet, by thing id (a minified piece keeps its id)
        private HashSet<int> pendingEntads = new HashSet<int>();
        // One line per entad that has left, for the letter when the visit ends
        private List<string> bindReport = new List<string>();

        public DungeonSite()
        {
            stash = new ThingOwner<Thing>(this);
        }

        public override MapGeneratorDef MapGeneratorDef => DungeonDefOf.DMAD_Dungeon;

        // Vanilla's generic "Enter" would walk a second caravan in from the map edge, which is solid wall, with no
        // party checks. The only way in is the mod's own entry.
        protected override bool UseGenericEnterMapFloatMenuOption => false;

        // Same for transport pods and shuttles landing in a running dungeon
        public override IEnumerable<FloatMenuOption> GetTransportersFloatMenuOptions(IEnumerable<IThingHolder> pods, System.Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            yield break;
        }

        public override IEnumerable<FloatMenuOption> GetShuttleFloatMenuOptions(IEnumerable<IThingHolder> pods, System.Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            yield break;
        }

        public override string Label => "DMAD_SiteLabel".Translate(level);

        static readonly MaterialPropertyBlock drawBlock = new MaterialPropertyBlock();

        // Vanilla's zoomed-in draw (WorldObject.Draw) orients the quad so texture-up points east, which lays an upright
        // sprite on its side; settlement icons are symmetric so vanilla never notices. Same draw, turned a quarter
        // so texture-up points north, matching the batched dungeon icons in WorldDrawLayer_Dungeons.
        public override void Draw()
        {
            Material mat = Material;
            if (Tile.LayerDef.isSpace || mat == null) return;
            float size = 0.7f * Tile.Layer.AverageTileSize;
            float alt = DrawAltitude + Rand.RangeSeeded(0f, 0.01f, ID) + def.drawAltitudeOffset;
            float transition = ExpandableWorldObjectsUtility.RawTransitionPct;
            if (def.expandingIcon && transition > 0f && !ExpandableWorldObjectsUtility.HiddenByRules(this))
            {
                Color c = mat.color;
                drawBlock.SetColor(ShaderPropertyIDs.Color, new Color(c.r, c.g, c.b, c.a * (1f - transition)));
                WorldRendererUtility.DrawQuadTangentialToPlanet(DrawPos, size, alt, mat, -90f, propertyBlock: drawBlock);
            }
            else
                WorldRendererUtility.DrawQuadTangentialToPlanet(DrawPos, size, alt, mat, -90f);
        }

        // MapParent's version is not virtual and returns null; re-listing IThingHolder on this class makes the
        // interface map to this one, so the stash's contents are reachable as held things (and kept by world GC)
        public new ThingOwner GetDirectlyHeldThings() => stash;

        public override void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, stash);
            if (HasMap) outChildren.Add(Map);
        }

        public void AddToStash(Thing t)
        {
            if (t.holdingOwner != null) t.holdingOwner.TryTransferToContainer(t, stash, t.stackCount);
            else stash.TryAdd(t);
        }

        /// <summary>A player caravan waiting on this tile, if any.</summary>
        public Caravan WaitingCaravan()
        {
            foreach (var c in Find.WorldObjects.Caravans)
                if (c.Tile == Tile && c.IsPlayerControlled && !c.pather.Moving) return c;
            return null;
        }

        /// <summary>Moves a pawn standing on the exit out to the caravan on this tile, making one if needed.
        /// Returns false when the pawn cannot leave yet (an animal or prisoner with no caravan to join).</summary>
        public bool TryMovePawnOut(Pawn pawn)
        {
            // A dweller who agreed to come: joins (and goes on below as a colonist) or goes their own way
            if (DungeonDwellerUtility.TryResolveFollowerOut(pawn, this)) return true;
            ResolveHeldBy(pawn);

            // Pawn.ExitMap destroys a carried item, and a drafted pawn keeps carrying through the exit
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried != null && !(carried is Pawn) && pawn.inventory != null)
                pawn.carryTracker.innerContainer.TryTransferToContainer(carried, pawn.inventory.innerContainer, carried.stackCount);

            var caravan = WaitingCaravan();
            if (caravan != null)
            {
                caravan.AddPawn(pawn, addCarriedPawnToWorldPawnsIfAny: true);
                pawn.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.Invalid);
            }
            else
            {
                if (!pawn.IsColonist && !pawn.IsColonySubhumanPlayerControlled && !pawn.IsColonyMechPlayerControlled) return false;
                caravan = CaravanMaker.MakeCaravan(Gen.YieldSingle(pawn), Faction.OfPlayer, Tile, addToWorldPawnsIfNotAlready: false);
                pawn.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.Invalid);
                if (!pawn.IsWorldPawn()) Find.WorldPawns.PassToWorld(pawn);
            }
            if (pawn.inventory != null) pawn.inventory.UnloadEverything = false;
            FlushStash(caravan);
            return true;
        }

        public void RegisterEntad(Thing t) => pendingEntads.Add(t.thingIDNumber);
        public void UnregisterEntad(Thing t) => pendingEntads.Remove(t.thingIDNumber);

        /// <summary>Binds one of this dungeon's entads as it leaves, by the settings' odds: to one pawn (the holder,
        /// meaning whoever has it equipped, worn or in their inventory; a random member of the party when it was only
        /// hauled or carried in hand, so holder is null), to the whole party, or to nobody.
        /// Anything else, or an entad already resolved, is ignored.</summary>
        public void ResolveEntad(Thing thing, Pawn holder)
        {
            Thing inner = thing is MinifiedThing m ? m.InnerThing : thing;
            if (inner == null || !pendingEntads.Remove(inner.thingIDNumber)) return;
            var s = DungeonsMod.Settings;
            var living = party.Where(p => p != null && !p.Dead && !p.Destroyed).ToList();
            float roll = Rand.Value;
            string how = "DMAD_Bind_None".Translate();
            if (roll < s.bindSingleChance)
            {
                Pawn who = holder != null && holder.RaceProps.Humanlike && !holder.Dead ? holder : living.RandomElementWithFallback();
                if (who != null && EntadFramework.EntadApi.Bind(inner, new[] { who }))
                    how = "DMAD_Bind_Single".Translate(who.Named("PAWN"));
            }
            else if (roll < s.bindSingleChance + s.bindPartyChance && living.Count > 0)
            {
                if (EntadFramework.EntadApi.Bind(inner, living))
                    how = "DMAD_Bind_Party".Translate(living.Select(p => p.LabelShort).ToCommaList(useAnd: true));
            }
            bindReport.Add("DMAD_Bind_Line".Translate(inner.LabelCap, how));
        }

        /// <summary>Everything a pawn has on them as they leave. Equipment, apparel and inventory count as theirs for a
        /// single bind; a thing merely carried in hand is treated like a hauled one (a random party member).</summary>
        public void ResolveHeldBy(Pawn p)
        {
            if (pendingEntads.Count == 0 || p == null) return;
            var held = new List<Thing>();
            if (p.equipment != null) held.AddRange(p.equipment.AllEquipmentListForReading);
            if (p.apparel != null) held.AddRange(p.apparel.WornApparel);
            if (p.inventory != null) held.AddRange(p.inventory.innerContainer);
            foreach (var t in held) ResolveEntad(t, p);
            if (p.carryTracker?.CarriedThing is Thing c && !(c is Pawn)) ResolveEntad(c, null);
        }

        public void FlushStash(Caravan caravan)
        {
            if (caravan == null) return;
            for (int i = stash.Count - 1; i >= 0; i--)
            {
                Thing t = stash[i];
                stash.Remove(t);
                // A downed or roped animal (or a downed person) carried to the exit arrives as a hauled thing;
                // GiveThing only takes items and destroys a pawn, so it joins the caravan as a member instead
                if (t is Pawn p)
                {
                    if (DungeonDwellerUtility.TryResolveCarriedFollower(p, this)) continue;
                    caravan.AddPawn(p, addCarriedPawnToWorldPawnsIfAny: true);
                    if (!p.Dead && !p.IsWorldPawn()) Find.WorldPawns.PassToWorld(p);
                    if (p.inventory != null) p.inventory.UnloadEverything = false;
                }
                else CaravanInventoryUtility.GiveThing(caravan, t);
            }
        }

        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = true;
            // Vanilla's AnyPawnBlockingMapRemoval also counts colonist corpses, which would keep a wiped party's
            // dungeon open forever. Here only living player pawns that can act on their own keep it: downed ones
            // count, since they can still be revived; animals alone do not.
            foreach (var p in Map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || p.Faction != Faction.OfPlayer) continue;
                if (p.RaceProps.Humanlike || p.IsColonyMech) return false;
            }
            // Animals and followers walk out on their own, after the colonists may already have gone: wait for them
            foreach (var p in Map.mapPawns.AllPawnsSpawned)
                if (p.CurJobDef == JobDefOf.EnterPortal && p.CurJob.targetA.Thing is DungeonExit && !p.Downed
                    && (p.Faction == Faction.OfPlayer || DungeonDwellerUtility.IsFollower(p)))
                    return false;
            return true;
        }

        public override void PostMapGenerate()
        {
            base.PostMapGenerate();
            everyoneLeft = false;
        }

        public void Notify_PawnLeft() => everyoneLeft = true;

        public override void Notify_MyMapAboutToBeRemoved()
        {
            base.Notify_MyMapAboutToBeRemoved();
            bool anyCorpse = Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
                .OfType<Corpse>().Any(c => c.InnerPawn.Faction == Faction.OfPlayer && c.InnerPawn.RaceProps.Humanlike);
            // Animals waiting on the exit for a caravan that only formed this tick would otherwise be left behind
            foreach (var exit in Map.listerThings.ThingsOfDef(DungeonDefOf.DMAD_DungeonExit))
                (exit as DungeonExit)?.MoveArrivedOut();
            var caravan = WaitingCaravan();
            // A colonist carried out (downed) with nobody outside would vanish with the site: they form a caravan
            if (caravan == null)
            {
                var colonist = stash.OfType<Pawn>().FirstOrDefault(p => !p.Dead && p.RaceProps.Humanlike && CaravanUtility.IsOwner(p, Faction.OfPlayer));
                if (colonist != null)
                {
                    stash.Remove(colonist);
                    caravan = CaravanMaker.MakeCaravan(Gen.YieldSingle(colonist), Faction.OfPlayer, Tile, addToWorldPawnsIfNotAlready: true);
                    everyoneLeft = true;
                }
            }
            FlushStash(caravan);
            if (!everyoneLeft || (anyCorpse && caravan == null))
                Find.LetterStack.ReceiveLetter("DMAD_PartyLost_Label".Translate(), "DMAD_PartyLost_Text".Translate(level),
                    LetterDefOf.NegativeEvent, new GlobalTargetInfo(Tile));
            if (bindReport.Count > 0)
                Find.LetterStack.ReceiveLetter("DMAD_Entads_Label".Translate(), "DMAD_Entads_Text".Translate(bindReport.Count, string.Join("\n", bindReport)),
                    LetterDefOf.PositiveEvent, caravan != null ? new GlobalTargetInfo(caravan) : new GlobalTargetInfo(Tile));
            bindReport.Clear();
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (var o in base.GetFloatMenuOptions(caravan)) yield return o;
            // A party is inside: other caravans can travel here and wait, but not enter
            yield return new FloatMenuOption("DMAD_EnterDungeon".Translate() + " (" + "DMAD_PartyInside".Translate() + ")", null);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref level, "level");
            Scribe_Values.Look(ref partyWealth, "partyWealth");
            Scribe_Values.Look(ref effectiveLevel, "effectiveLevel");
            Scribe_Values.Look(ref layoutSeed, "layoutSeed");
            Scribe_Deep.Look(ref layout, "layout");
            Scribe_Defs.Look(ref theme, "theme");
            Scribe_Values.Look(ref everyoneLeft, "everyoneLeft");
            Scribe_Deep.Look(ref stash, "stash", this);
            if (stash == null) stash = new ThingOwner<Thing>(this);
            Scribe_Collections.Look(ref party, "party", LookMode.Reference);
            Scribe_Collections.Look(ref pendingEntads, "pendingEntads", LookMode.Value);
            Scribe_Collections.Look(ref bindReport, "bindReport", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (party == null) party = new List<Pawn>();
                party.RemoveAll(p => p == null);
                if (pendingEntads == null) pendingEntads = new HashSet<int>();
                if (bindReport == null) bindReport = new List<string>();
            }
        }
    }

    [DefOf]
    public static class DungeonDefOf
    {
        public static WorldObjectDef DMAD_DungeonSite;
        public static MapGeneratorDef DMAD_Dungeon;
        public static ThingDef DMAD_DungeonWall;
        public static ThingDef DMAD_DungeonRubble;
        public static ThingDef DMAD_DungeonExit;
        public static DungeonThemeDef DMAD_Cave;
        public static ThingDef DMAD_TangleCore;
        public static ThingDef DMAD_DungeonWallPowered;
        public static HediffDef DMAD_DungeonMad;
        public static MentalStateDef DMAD_DungeonMadness;
        static DungeonDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(DungeonDefOf));
    }
}
