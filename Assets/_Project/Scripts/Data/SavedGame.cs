using System;
using System.Collections.Generic;
using HopOn.Logic;

namespace HopOn.Data
{
    /// <summary>
    /// Where the player is, as saved between runs (see <see cref="SaveStore"/>): the level to play, by id so
    /// progress follows its level when the catalog changes, and the level number the player sees. A level left
    /// in the middle also keeps its moves and the time left: replaying the moves through the rules
    /// (<see cref="LevelSession.TryResume"/>) rebuilds exactly where the player was, so there is no separate
    /// copy of the board that could disagree with the game.
    /// </summary>
    /// <example>
    /// { "version": 1, "levelId": "Level_03", "levelNumber": 3, "inProgress": true, "remainingSeconds": 41.5, "moves": [0, 2, 0, 3, -1, -1] }
    /// </example>
    [Serializable]
    public sealed class SavedGame
    {
        /// <summary>Bumped when the meaning of a field changes; a save from another version is not used.</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string levelId;
        public int levelNumber;

        /// <summary>The level was left in the middle: replay its moves and keep the time left.</summary>
        public bool inProgress;
        public float remainingSeconds;

        /// <summary><see cref="LevelSession.Moves"/>, two numbers a move: row, column.</summary>
        public int[] moves = Array.Empty<int>();

        /// <summary>A level to play from its start: the next one after a win, the same one after a loss.</summary>
        public static SavedGame LevelToPlay(string levelId, int levelNumber)
        {
            return new SavedGame { levelId = levelId, levelNumber = levelNumber };
        }

        /// <summary>The level being played, as it is now.</summary>
        public static SavedGame LevelInPlay(string levelId, int levelNumber, IReadOnlyList<GridPos> moves, float remainingSeconds)
        {
            var flat = new int[moves.Count * 2];
            for (int i = 0; i < moves.Count; i++)
            {
                flat[2 * i] = moves[i].Row;
                flat[2 * i + 1] = moves[i].Col;
            }
            return new SavedGame
            {
                levelId = levelId,
                levelNumber = levelNumber,
                inProgress = true,
                remainingSeconds = remainingSeconds,
                moves = flat,
            };
        }

        public List<GridPos> Moves()
        {
            var cells = new List<GridPos>(moves.Length / 2);
            for (int i = 0; i + 1 < moves.Length; i += 2)
            {
                cells.Add(new GridPos(moves[i], moves[i + 1]));
            }
            return cells;
        }

        /// <summary>Written by this version of the game and whole: anything else starts the game fresh.</summary>
        public bool IsUsable => version == CurrentVersion && !string.IsNullOrEmpty(levelId) && levelNumber >= 1
                                && moves != null && moves.Length % 2 == 0;
    }
}
