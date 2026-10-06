using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    /// <summary>
    /// Draws every dungeon icon on the surface in one batched mesh (one sub-mesh per tint), the way hills and
    /// landmarks are drawn. Added to the Surface PlanetLayerDef by Patches/TUBAD_PlanetLayer.xml.
    /// </summary>
    public class WorldDrawLayer_Dungeons : WorldDrawLayer
    {
        public const string IconPath = "World/TUBAD_Dungeon";
        private const int TintSteps = 12;
        private const float IconScale = 0.75f;

        private static Material plainMat;
        private static readonly Material[] tintMats = new Material[TintSteps];

        // Level 1 green through yellow and red to purple at the top level
        private static readonly Color[] Gradient =
        {
            new Color(0.45f, 0.85f, 0.45f),
            new Color(0.95f, 0.85f, 0.35f),
            new Color(0.95f, 0.45f, 0.30f),
            new Color(0.75f, 0.35f, 0.95f),
        };

        public override bool VisibleWhenLayerNotSelected => false;
        public override bool VisibleInBackground => false;
        public override bool Visible => base.Visible && DungeonsMod.Settings.showIcons;

        // Applied by the base Render through a property block, so changing opacity needs no rebuild
        protected override float Alpha => DungeonsMod.Settings.iconOpacity;

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate()) yield return item;

            var comp = Find.World.GetComponent<DungeonWorldComponent>();
            if (comp != null && planetLayer == Find.WorldGrid.Surface)
            {
                var settings = DungeonsMod.Settings;
                float size = planetLayer.AverageTileSize * IconScale;
                var blocked = comp.BlockedSurfaceTiles();
                for (int i = 0; i < comp.Count; i++)
                {
                    var tile = new PlanetTile(comp.GetTile(i), planetLayer);
                    if (blocked.Contains(tile.tileId)) continue;
                    Material mat = settings.tintByLevel ? TintMat(comp.GetLevel(i), comp.MaxLevel) : PlainMat;
                    LayerSubMesh subMesh = GetSubMesh(mat);
                    WorldRendererUtility.PrintQuadTangentialToPlanet(planetLayer.GetTileCenter(tile), size, 0.008f, subMesh);
                }
            }
            FinalizeMesh(MeshParts.All);
        }

        // Below world objects (3550) so a caravan on the tile draws over the icon
        private static Material PlainMat => plainMat ?? (plainMat = MaterialPool.MatFrom(IconPath, ShaderDatabase.WorldOverlayTransparentLit, Color.white, 3530));

        private static Material TintMat(int level, int maxLevel)
        {
            float t = maxLevel <= 1 ? 0f : Mathf.Clamp01((level - 1f) / (maxLevel - 1f));
            int step = Mathf.Min(TintSteps - 1, Mathf.FloorToInt(t * TintSteps));
            return tintMats[step] ?? (tintMats[step] = MaterialPool.MatFrom(IconPath, ShaderDatabase.WorldOverlayTransparentLit, ColorAt((step + 0.5f) / TintSteps), 3530));
        }

        public static Color ColorAt(float t)
        {
            float x = Mathf.Clamp01(t) * (Gradient.Length - 1);
            int i = Mathf.Min(Gradient.Length - 2, Mathf.FloorToInt(x));
            return Color.Lerp(Gradient[i], Gradient[i + 1], x - i);
        }
    }
}
