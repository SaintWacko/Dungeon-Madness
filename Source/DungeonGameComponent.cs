using Verse;

namespace DungeonMadness
{
    // Remembers whether this game's world switches (Bastle and Entad sources outside dungeons) have been chosen.
    // A save from before Dungeon Madness was added gets this component fresh with the flag off, so it is asked too.
    public class DungeonGameComponent : GameComponent
    {
        public bool worldConfigured;

        public DungeonGameComponent(Game game) { }

        public static DungeonGameComponent Instance => Current.Game?.GetComponent<DungeonGameComponent>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref worldConfigured, "worldConfigured");
        }

        public override void StartedNewGame() => AskIfNeeded();

        public override void LoadedGame() => AskIfNeeded();

        private void AskIfNeeded()
        {
            if (worldConfigured || !DungeonsMod.Settings.askWorldSetup) return;
            // Both callbacks run inside the long event that builds or loads the game; the window waits for it to end
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (!worldConfigured && Find.WindowStack.WindowOfType<Dialog_WorldSetup>() == null)
                    Find.WindowStack.Add(new Dialog_WorldSetup());
            });
        }
    }
}
