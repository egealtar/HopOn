using System;
using System.Collections.Generic;
using System.IO;
using HopOn.Logic;
using UnityEngine;

namespace HopOn.Data
{
    /// <summary>Levels shipped inside the build: the catalog's JSON files, read and checked when asked for.</summary>
    public sealed class CatalogLevelProvider : ILevelProvider
    {
        private readonly LevelCatalog _catalog;

        public CatalogLevelProvider(LevelCatalog catalog)
        {
            _catalog = catalog != null ? catalog : throw new ArgumentNullException(nameof(catalog), "Assign the Level Catalog.");
        }

        public int Count => _catalog.Levels.Count;

        public Level Get(int index)
        {
            TextAsset file = _catalog.Levels[index];
            if (file == null)
            {
                throw new InvalidDataException($"Entry {index + 1} of the level catalog is empty.");
            }
            if (!TryParse(file.name, file.text, out var level, out var errors))
            {
                throw new InvalidDataException($"Level '{file.name}' is broken: {string.Join(" ", errors)}");
            }
            return level;
        }

        public int IndexOf(string levelId)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_catalog.Levels[i] != null && _catalog.Levels[i].name == levelId)
                {
                    return i;
                }
            }
            return -1;
        }

        public string IdAt(int index)
        {
            TextAsset file = _catalog.Levels[index];
            return file != null ? file.name : throw new InvalidDataException($"Entry {index + 1} of the level catalog is empty.");
        }

        /// <summary>JSON text to a checked level, or false with every problem found.
        /// The tests read every shipped level through this same method.</summary>
        public static bool TryParse(string id, string json, out Level level, out List<string> errors)
        {
            level = null;
            LevelFile file;
            try
            {
                file = JsonUtility.FromJson<LevelFile>(json);
            }
            catch (Exception e)
            {
                errors = new List<string> { $"Not valid JSON ({e.Message})." };
                return false;
            }
            if (file == null)
            {
                errors = new List<string> { "The file is empty." };
                return false;
            }
            return Level.TryCreate(id, file.cells, file.buses, file.timeLimitSeconds, out level, out errors);
        }
    }
}
