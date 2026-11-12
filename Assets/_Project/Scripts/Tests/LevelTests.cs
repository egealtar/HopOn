using System.Collections.Generic;
using HopOn.Data;
using HopOn.Logic;
using HopOn.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HopOn.Tests
{
    /// <summary>Reading and checking level files: the spelling, the game rules, and every level that ships.</summary>
    public sealed class LevelTests
    {
        [Test]
        public void RowZero_IsTheTopRow_WhereTheExitsAre()
        {
            var level = TestLevels.Make("R", "RRR", "...");

            Assert.AreEqual(PassengerColor.Red, level.PassengerAt(new GridPos(0, 2)));
            Assert.IsTrue(level.IsExit(new GridPos(0, 1)));
            Assert.IsFalse(level.IsExit(new GridPos(1, 1)));
        }

        [Test]
        public void UnknownLetter_IsReportedWithItsRowAndColumn()
        {
            var errors = Errors("R", "RXR");

            Assert.AreEqual(1, CountContaining(errors, "'X' at row 0, column 1"), string.Join(" ", errors));
        }

        [Test]
        public void ColourCountMismatch_IsReported()
        {
            var errors = Errors("R", "RRRR");

            Assert.AreEqual(1, CountContaining(errors, "Red: 4 passengers but 3 bus seats"), string.Join(" ", errors));
        }

        [Test]
        public void WalledInPassengers_AreReported()
        {
            var errors = Errors("RB", "RRR", "###", "BBB");

            Assert.AreEqual(3, CountContaining(errors, "walled in"), string.Join(" ", errors));
        }

        [Test]
        public void TopRowWithoutFloor_IsReportedAsNoExit()
        {
            var errors = Errors("R", "###", "RRR");

            Assert.AreEqual(1, CountContaining(errors, "No exit"), string.Join(" ", errors));
        }

        [Test]
        public void ShippedLevels_AtLeastFive_AllLoad_FitTheScreen_WithUniqueIds()
        {
            string[] catalogs = AssetDatabase.FindAssets("t:LevelCatalog");
            Assert.AreEqual(1, catalogs.Length, "Expected exactly one LevelCatalog asset.");
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(AssetDatabase.GUIDToAssetPath(catalogs[0]));
            Assert.GreaterOrEqual(catalog.Levels.Count, 5, "The brief asks for at least 5 levels.");

            var ids = new HashSet<string>();
            foreach (TextAsset file in catalog.Levels)
            {
                Assert.IsNotNull(file, "The level catalog has an empty entry.");
                bool ok = CatalogLevelProvider.TryParse(file.name, file.text, out Level level, out var errors);
                Assert.IsTrue(ok, $"{file.name}: {string.Join(" ", errors)}");
                Assert.LessOrEqual(level.Width, BoardLayout.MaxBoardWidth, $"{file.name} is wider than the camera frames.");
                Assert.LessOrEqual(level.Height, BoardLayout.MaxBoardHeight, $"{file.name} is taller than the camera frames.");
                Assert.IsTrue(ids.Add(file.name), $"Two levels share the id {file.name}.");
            }
        }

        private static List<string> Errors(string buses, params string[] rows)
        {
            Assert.IsFalse(Level.TryCreate("test", rows, buses, 60f, out _, out var errors), "The level should be rejected.");
            return errors;
        }

        private static int CountContaining(List<string> errors, string text)
        {
            int count = 0;
            foreach (string error in errors)
            {
                if (error.Contains(text))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
