using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HopOn.Data;
using HopOn.Logic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HopOn.Tests
{
    /// <summary>The save on disk: read back exactly as written, written whole, and never a reason for the
    /// game not to start. Each test works in its own temporary folder.</summary>
    public sealed class SaveStoreTests
    {
        private string _folder;
        private SaveStore _store;

        private string SavePath => Path.Combine(_folder, "save.json");

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "HopOnSaveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _store = new SaveStore(SavePath);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void NoSave_LoadsNothing()
        {
            Assert.IsNull(_store.Load());
        }

        [Test]
        public void LevelLeftInTheMiddle_ResumesExactlyAsItWasLeft()
        {
            var level = TestLevels.Make("RB", "BRRR", "BB..");
            var played = new LevelSession(level);
            played.Start();
            played.Tap(new GridPos(0, 0));      // a blue waits
            played.Tap(new GridPos(0, 1));
            played.Tap(new GridPos(0, 2));
            played.Tap(new GridPos(0, 3));      // the red bus leaves
            played.BusArrived();                // the blue bus takes the waiting blue
            played.Tick(12.5f);

            _store.Write(SavedGame.LevelInPlay(level.Id, 4, played.Moves, played.RemainingSeconds));
            SavedGame saved = _store.Load();

            Assert.IsTrue(saved.inProgress);
            Assert.AreEqual(level.Id, saved.levelId);
            Assert.AreEqual(4, saved.levelNumber);
            Assert.IsTrue(LevelSession.TryResume(level, saved.Moves(), saved.remainingSeconds, out var resumed));
            Assert.AreEqual(played.RemainingSeconds, resumed.RemainingSeconds);
            Assert.AreEqual(played.Buses.CurrentIndex, resumed.Buses.CurrentIndex);
            Assert.AreEqual(played.Buses.SeatsTaken, resumed.Buses.SeatsTaken);
            Assert.AreEqual(played.Waiting.Occupied, resumed.Waiting.Occupied);
            foreach (var cell in level.AllPositions())
            {
                Assert.AreEqual(played.Board.PassengerAt(cell)?.Id, resumed.Board.PassengerAt(cell)?.Id);
            }
        }

        [Test]
        public void LevelToPlay_StartsFromItsStart()
        {
            _store.Write(SavedGame.LevelToPlay("Level_03", 8));
            SavedGame saved = _store.Load();

            Assert.AreEqual("Level_03", saved.levelId);
            Assert.AreEqual(8, saved.levelNumber);
            Assert.IsFalse(saved.inProgress);
            Assert.IsEmpty(saved.Moves());
        }

        [Test]
        public void NewerSave_TakesTheOldOnesPlace_AndLeavesNothingElseBehind()
        {
            _store.Write(SavedGame.LevelToPlay("Level_01", 1));
            _store.Write(SavedGame.LevelToPlay("Level_02", 2));

            Assert.AreEqual("Level_02", _store.Load().levelId);
            Assert.AreEqual(new[] { "save.json" }, Directory.GetFiles(_folder).Select(Path.GetFileName).ToArray());
        }

        [Test]
        public void BrokenFile_StartsFresh()
        {
            File.WriteAllText(SavePath, "{ \"version\": 1, \"levelId\": \"Lev");
            LogAssert.Expect(LogType.Warning, new Regex("save at"));

            Assert.IsNull(_store.Load());
        }

        [Test]
        public void SaveFromAnotherVersion_StartsFresh()
        {
            var saved = SavedGame.LevelToPlay("Level_03", 3);
            saved.version = SavedGame.CurrentVersion + 1;
            _store.Write(saved);
            LogAssert.Expect(LogType.Warning, new Regex("save at"));

            Assert.IsNull(_store.Load());
        }
    }
}
