using System.Collections.Generic;
using UnityEngine;

namespace HopOn.Data
{
    /// <summary>
    /// The level files that ship with the game, in play order; reorder them by dragging in the
    /// inspector. A level's id is its file name.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "Hop On/Level Catalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        [SerializeField] private List<TextAsset> levels = new List<TextAsset>();

        public IReadOnlyList<TextAsset> Levels => levels;
    }
}
