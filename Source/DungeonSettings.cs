using UnityEngine;
using Verse;

namespace ThisUsedToBeAboutDungeons
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

        // Dungeons on water tiles while Vehicle Framework is active
        public bool oceanDungeons = true;

        // ---- Parties
        public const int DefaultMaxPartySize = 5;
        // Humanlikes only; animals and mechs do not count
        public int maxPartySize = DefaultMaxPartySize;
        // Party wealth (pawns + worn and wielded gear) at which the effective level is double the dungeon's level.
        // Linear: effective level = level x (1 + wealth / wealthToDouble).
        public const float DefaultWealthToDouble = 50000f;
        public float wealthToDouble = DefaultWealthToDouble;

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
            oceanDungeons = true;
            maxPartySize = DefaultMaxPartySize;
            wealthToDouble = DefaultWealthToDouble;
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
            Scribe_Values.Look(ref oceanDungeons, "oceanDungeons", true);
            Scribe_Values.Look(ref maxPartySize, "maxPartySize", DefaultMaxPartySize);
            maxPartySize = Mathf.Clamp(maxPartySize, 1, 20);
            Scribe_Values.Look(ref wealthToDouble, "wealthToDouble", DefaultWealthToDouble);
            wealthToDouble = Mathf.Clamp(wealthToDouble, 5000f, 300000f);
            Scribe_Values.Look(ref showIcons, "showIcons", true);
            Scribe_Values.Look(ref iconOpacity, "iconOpacity", DefaultIconOpacity);
            iconOpacity = Mathf.Clamp(iconOpacity, 0.1f, 1f);
            Scribe_Values.Look(ref tintByLevel, "tintByLevel", true);
        }
    }
}
