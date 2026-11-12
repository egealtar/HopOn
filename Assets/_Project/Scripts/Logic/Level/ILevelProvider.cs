namespace HopOn.Logic
{
    /// <summary>
    /// Where levels come from. The game only knows this interface: today the levels ship inside
    /// the build; a live game could add a provider that gets them, or their order, from a server
    /// without touching anything else.
    /// </summary>
    public interface ILevelProvider
    {
        int Count { get; }

        /// <summary>Level <paramref name="index"/> (0-based). Throws if the file is broken; the
        /// tests check that every shipped level loads.</summary>
        Level Get(int index);

        /// <summary>The index of the level with this id (its file name), or -1 if there is none.
        /// The save stores ids, not indexes, so progress follows the level when the list changes.</summary>
        int IndexOf(string levelId);
    }
}
