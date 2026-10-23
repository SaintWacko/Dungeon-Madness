using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    public class DungeonSettings : ModSettings
    {
        // ---- World generation: read when a world's dungeons are generated, so changes only reach new worlds
        // (and the "Regenerate dungeons" dev action).
        public const int DefaultSpacing = 10;
        // Minimum distance in tiles between two dungeons
        public int spacing = DefaultSpacing;

        public const int DefaultMaxLevel = 30;
        public int maxLevel = DefaultMaxLevel;

        // Levels are assigned by rank: a dungeon whose noise value is higher than fraction p of all dungeons gets
        // level 1 + p^skew * maxLevel. 1 = every level equally common; higher = low levels more common.
        public const float DefaultLevelSkew = 1.5f;
        public float levelSkew = DefaultLevelSkew;

        // Noise frequency over the unit sphere: higher = more, smaller peaks and valleys
        public const float DefaultRoughness = 7f;
        public float roughness = DefaultRoughness;

        // ---- Parties
        public const int DefaultMaxPartySize = 5;
        // Humanlikes only; animals and mechs do not count
        public int maxPartySize = DefaultMaxPartySize;
        // Party wealth (pawns + worn and wielded gear) at which the effective level is double the dungeon's level.
        // Linear: effective level = level x (1 + wealth / wealthToDouble).
        public const float DefaultWealthToDouble = 50000f;
        public float wealthToDouble = DefaultWealthToDouble;

        // ---- Creatures: read when a dungeon map is generated, so changes reach the next party to enter
        // Average bastle points a pack gets per effective level; the pack's total is split between its members
        public const float DefaultPointsPerLevel = 1f;
        public float pointsPerLevel = DefaultPointsPerLevel;
        // A pack's total is the average times 1 +/- this
        public const float DefaultPointNoise = 0.25f;
        public float pointNoise = DefaultPointNoise;
        // Chance a chamber other than the entrance has a pack, and the chance the entrance does
        public const float DefaultOccupancy = 0.6f;
        public float occupancy = DefaultOccupancy;
        public const float DefaultEntranceChance = 0.1f;
        public float entranceChance = DefaultEntranceChance;
        // Chance an animal or humanlike pack is dungeon-mad
        public const float DefaultMadChance = 0.95f;
        public float madChance = DefaultMadChance;
        // Chance a pack (not mechs) is made of stone or another material; outside the point budget
        public const float DefaultStoneChance = 0.15f;
        public float stoneChance = DefaultStoneChance;
        public const int DefaultMaxPackSize = 10;
        public int maxPackSize = DefaultMaxPackSize;
        // Strongest species allowed, by vanilla combat power: base + perLevel x effective level
        public const float DefaultPowerBase = 60f;
        public float powerBase = DefaultPowerBase;
        public const float DefaultPowerPerLevel = 10f;
        public float powerPerLevel = DefaultPowerPerLevel;

        public float PowerCeiling(float level) => powerBase + powerPerLevel * level;

        // Sane dwellers: chance one agrees to leave when invited (times the inviter's social impact), and chance one
        // who came out joins the colony rather than going their own way
        public const float DefaultInviteChance = 0.8f;
        public float inviteChance = DefaultInviteChance;
        public const float DefaultJoinChance = 0.5f;
        public float joinChance = DefaultJoinChance;

        // ---- Loot: read when a dungeon map is generated (binding odds: when an entad leaves the dungeon)
        // Chance a chamber other than the entrance has a loot pile
        public const float DefaultLootChance = 0.4f;
        public float lootChance = DefaultLootChance;
        // A pile's market value per effective level
        public const float DefaultLootValuePerLevel = 60f;
        public float lootValuePerLevel = DefaultLootValuePerLevel;
        // The piles' ordinary goods are rolled for the whole dungeon in rolls of this value per effective level,
        // which is also the most one item can be worth (about four default piles)
        public const float DefaultLootRollValuePerLevel = 250f;
        public float lootRollValuePerLevel = DefaultLootRollValuePerLevel;
        // Chance each loot slot (in a pile or on a storeroom shelf) is an entad
        public const float DefaultEntadChance = 1f / 3f;
        public float entadChance = DefaultEntadChance;
        // Entad trait points per effective level
        public const float DefaultEntadPointsPerLevel = 1f;
        public float entadPointsPerLevel = DefaultEntadPointsPerLevel;
        // Binding odds when an entad leaves; the rest is unbound
        public const float DefaultBindSingleChance = 0.2f;
        public float bindSingleChance = DefaultBindSingleChance;
        public const float DefaultBindPartyChance = 0.4f;
        public float bindPartyChance = DefaultBindPartyChance;

        // ---- Influence: spending verity on a visit from the entry dialog. Read when the screen is open.
        public bool influenceEnabled = true;
        public InfluenceCurve influenceCurve = InfluenceCurve.Fibonacci;
        // A base step costs the dungeon's own level / this, rounded, at least 1 (level 12: 2)
        public const float DefaultInfluenceLevelsPerStep = 6f;
        public float influenceLevelsPerStep = DefaultInfluenceLevelsPerStep;
        // Rows a dungeon offers besides level and theme. Below the number of slots, each tile is dealt its own and
        // the fee attunes to it once; at every slot, every row is offered everywhere and the fee is paid per visit.
        public const int DefaultInfluenceRowCount = 4;
        public int influenceRowCount = DefaultInfluenceRowCount;
        // The attunement fee, or the per-visit fee when every row is offered everywhere, in base steps
        public const int DefaultInfluenceFeeSteps = 3;
        public int influenceFeeSteps = DefaultInfluenceFeeSteps;
        // Choosing another theme, in base steps
        public const int DefaultInfluenceThemeSteps = 5;
        public int influenceThemeSteps = DefaultInfluenceThemeSteps;
        public bool influenceTheme = true;
        // Rows switched off in settings (empty: every row is offered)
        public List<InfluenceKind> influenceDisabled = new List<InfluenceKind>();

        public bool InfluenceRowEnabled(InfluenceKind k) => !influenceDisabled.Contains(k);

        // ---- Display: take effect immediately
        public bool showIcons = true;
        public const float DefaultIconOpacity = 0.4f;
        public float iconOpacity = DefaultIconOpacity;
        // Tint world icons from green (level 1) to purple (max level)
        public bool tintByLevel = true;

        public void ResetToDefaults()
        {
            spacing = DefaultSpacing;
            maxLevel = DefaultMaxLevel;
            levelSkew = DefaultLevelSkew;
            roughness = DefaultRoughness;
            maxPartySize = DefaultMaxPartySize;
            wealthToDouble = DefaultWealthToDouble;
            pointsPerLevel = DefaultPointsPerLevel;
            pointNoise = DefaultPointNoise;
            occupancy = DefaultOccupancy;
            entranceChance = DefaultEntranceChance;
            madChance = DefaultMadChance;
            stoneChance = DefaultStoneChance;
            maxPackSize = DefaultMaxPackSize;
            powerBase = DefaultPowerBase;
            powerPerLevel = DefaultPowerPerLevel;
            inviteChance = DefaultInviteChance;
            joinChance = DefaultJoinChance;
            lootChance = DefaultLootChance;
            lootValuePerLevel = DefaultLootValuePerLevel;
            lootRollValuePerLevel = DefaultLootRollValuePerLevel;
            entadChance = DefaultEntadChance;
            entadPointsPerLevel = DefaultEntadPointsPerLevel;
            bindSingleChance = DefaultBindSingleChance;
            bindPartyChance = DefaultBindPartyChance;
            influenceEnabled = true;
            influenceCurve = InfluenceCurve.Fibonacci;
            influenceLevelsPerStep = DefaultInfluenceLevelsPerStep;
            influenceFeeSteps = DefaultInfluenceFeeSteps;
            influenceRowCount = DefaultInfluenceRowCount;
            influenceThemeSteps = DefaultInfluenceThemeSteps;
            influenceTheme = true;
            influenceDisabled.Clear();
            showIcons = true;
            iconOpacity = DefaultIconOpacity;
            tintByLevel = true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref spacing, "spacing", DefaultSpacing);
            spacing = Mathf.Clamp(spacing, 2, 20);
            Scribe_Values.Look(ref maxLevel, "maxLevel", DefaultMaxLevel);
            maxLevel = Mathf.Clamp(maxLevel, 5, 200);
            Scribe_Values.Look(ref levelSkew, "levelSkew", DefaultLevelSkew);
            levelSkew = Mathf.Clamp(levelSkew, 0.5f, 4f);
            Scribe_Values.Look(ref roughness, "roughness", DefaultRoughness);
            roughness = Mathf.Clamp(roughness, 0.5f, 15f);
            Scribe_Values.Look(ref maxPartySize, "maxPartySize", DefaultMaxPartySize);
            maxPartySize = Mathf.Clamp(maxPartySize, 1, 20);
            Scribe_Values.Look(ref wealthToDouble, "wealthToDouble", DefaultWealthToDouble);
            wealthToDouble = Mathf.Clamp(wealthToDouble, 5000f, 300000f);
            Scribe_Values.Look(ref pointsPerLevel, "pointsPerLevel", DefaultPointsPerLevel);
            pointsPerLevel = Mathf.Clamp(pointsPerLevel, 0.05f, 2f);
            Scribe_Values.Look(ref pointNoise, "pointNoise", DefaultPointNoise);
            pointNoise = Mathf.Clamp(pointNoise, 0f, 0.5f);
            Scribe_Values.Look(ref occupancy, "occupancy", DefaultOccupancy);
            occupancy = Mathf.Clamp01(occupancy);
            Scribe_Values.Look(ref entranceChance, "entranceChance", DefaultEntranceChance);
            entranceChance = Mathf.Clamp01(entranceChance);
            Scribe_Values.Look(ref madChance, "madChance", DefaultMadChance);
            madChance = Mathf.Clamp01(madChance);
            Scribe_Values.Look(ref stoneChance, "stoneChance", DefaultStoneChance);
            stoneChance = Mathf.Clamp01(stoneChance);
            Scribe_Values.Look(ref maxPackSize, "maxPackSize", DefaultMaxPackSize);
            maxPackSize = Mathf.Clamp(maxPackSize, 1, 12);
            Scribe_Values.Look(ref powerBase, "powerBase", DefaultPowerBase);
            powerBase = Mathf.Clamp(powerBase, 20f, 500f);
            Scribe_Values.Look(ref powerPerLevel, "powerPerLevel", DefaultPowerPerLevel);
            powerPerLevel = Mathf.Clamp(powerPerLevel, 0f, 50f);
            Scribe_Values.Look(ref inviteChance, "inviteChance", DefaultInviteChance);
            inviteChance = Mathf.Clamp01(inviteChance);
            Scribe_Values.Look(ref joinChance, "joinChance", DefaultJoinChance);
            joinChance = Mathf.Clamp01(joinChance);
            Scribe_Values.Look(ref lootChance, "lootChance", DefaultLootChance);
            lootChance = Mathf.Clamp01(lootChance);
            Scribe_Values.Look(ref lootValuePerLevel, "lootValuePerLevel", DefaultLootValuePerLevel);
            lootValuePerLevel = Mathf.Clamp(lootValuePerLevel, 0f, 500f);
            Scribe_Values.Look(ref lootRollValuePerLevel, "lootRollValuePerLevel", DefaultLootRollValuePerLevel);
            lootRollValuePerLevel = Mathf.Clamp(lootRollValuePerLevel, 20f, 1000f);
            Scribe_Values.Look(ref entadChance, "entadChance", DefaultEntadChance);
            entadChance = Mathf.Clamp01(entadChance);
            Scribe_Values.Look(ref entadPointsPerLevel, "entadPointsPerLevel", DefaultEntadPointsPerLevel);
            entadPointsPerLevel = Mathf.Clamp(entadPointsPerLevel, 0.05f, 2f);
            Scribe_Values.Look(ref bindSingleChance, "bindSingleChance", DefaultBindSingleChance);
            bindSingleChance = Mathf.Clamp01(bindSingleChance);
            Scribe_Values.Look(ref bindPartyChance, "bindPartyChance", DefaultBindPartyChance);
            bindPartyChance = Mathf.Clamp(bindPartyChance, 0f, 1f - bindSingleChance);
            Scribe_Values.Look(ref influenceEnabled, "influenceEnabled", true);
            Scribe_Values.Look(ref influenceCurve, "influenceCurve", InfluenceCurve.Fibonacci);
            Scribe_Values.Look(ref influenceLevelsPerStep, "influenceLevelsPerStep", DefaultInfluenceLevelsPerStep);
            influenceLevelsPerStep = Mathf.Clamp(influenceLevelsPerStep, 1f, 30f);
            Scribe_Values.Look(ref influenceFeeSteps, "influenceFeeSteps", DefaultInfluenceFeeSteps);
            influenceFeeSteps = Mathf.Clamp(influenceFeeSteps, 0, 20);
            Scribe_Values.Look(ref influenceRowCount, "influenceRowCount", DefaultInfluenceRowCount);
            influenceRowCount = Mathf.Clamp(influenceRowCount, 0, DungeonInfluence.Slots.Length);
            Scribe_Values.Look(ref influenceThemeSteps, "influenceThemeSteps", DefaultInfluenceThemeSteps);
            influenceThemeSteps = Mathf.Clamp(influenceThemeSteps, 0, 30);
            Scribe_Values.Look(ref influenceTheme, "influenceTheme", true);
            Scribe_Collections.Look(ref influenceDisabled, "influenceDisabled", LookMode.Value);
            if (influenceDisabled == null) influenceDisabled = new List<InfluenceKind>();
            Scribe_Values.Look(ref showIcons, "showIcons", true);
            Scribe_Values.Look(ref iconOpacity, "iconOpacity", DefaultIconOpacity);
            iconOpacity = Mathf.Clamp(iconOpacity, 0.1f, 1f);
            Scribe_Values.Look(ref tintByLevel, "tintByLevel", true);
        }
    }
}
