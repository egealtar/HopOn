using HopOn.Data;
using HopOn.Logic;
using NUnit.Framework;
using UnityEngine;

namespace HopOn.Tests
{
    /// <summary>The solver: it finds a way through a level that has one, says so when there is none, and
    /// every level that ships can be finished.</summary>
    public sealed class LevelSolverTests
    {
        [Test]
        public void SolvableLevel_IsSolved_AndItsMovesWinTheRealGame()
        {
            var level = TestLevels.Make("RBG", ".R.B.", "GRBG.", ".RBG.");

            var result = LevelSolver.Solve(level);

            Assert.AreEqual(SolveOutcome.Solvable, result.Outcome);
            Assert.AreEqual(SessionState.Won, Replay(level, result).State);
        }

        [Test]
        public void FourInTheWay_IsSolvable_TheyAllFitInTheWaitingSeats()
        {
            // One corridor: the four blues on top must wait before the first red can reach the red bus.
            var level = TestLevels.Make("RBB", "B", "B", "B", "B", "R", "R", "R", "B", "B");

            var result = LevelSolver.Solve(level);

            Assert.AreEqual(SolveOutcome.Solvable, result.Outcome);
            Assert.AreEqual(SessionState.Won, Replay(level, result).State);
        }

        [Test]
        public void FiveInTheWay_IsUnsolvable_TheFifthTakesTheLastSeatBesideTheBus()
        {
            // The level checks pass (colours match the seats, everyone can walk out), but the fifth blue
            // takes the last waiting seat while the red bus stands empty at the stop.
            var level = TestLevels.Make("RBB", "B", "B", "B", "B", "B", "R", "R", "R", "B");

            var result = LevelSolver.Solve(level);

            Assert.AreEqual(SolveOutcome.Unsolvable, result.Outcome);
            Assert.AreEqual(0, result.Moves.Count);
        }

        [Test]
        public void TwoColumns_AreUnsolvable_HoweverTheTapsAreOrdered()
        {
            // Unlike the corridor above, the taps can be ordered many ways here and the same position is reached
            // by more than one: the search has to try them all. Five others must leave before the third red can walk.
            var level = TestLevels.Make("RBGG", "GB", "BR", "R#", "GG", ".G", "GB", "GR");

            var result = LevelSolver.Solve(level);

            Assert.AreEqual(SolveOutcome.Unsolvable, result.Outcome);
        }

        [Test]
        public void TinyBudget_IsTooBig()
        {
            var level = TestLevels.Make("RBG", ".R.B.", "GRBG.", ".RBG.");

            var result = LevelSolver.Solve(level, 2);

            Assert.AreEqual(SolveOutcome.TooBig, result.Outcome);
            Assert.AreEqual(0, result.Moves.Count);
            Assert.AreEqual(2, result.StatesVisited);
        }

        [Test]
        public void ShippedLevels_CanAllBeFinished()
        {
            string[] catalogs = UnityEditor.AssetDatabase.FindAssets("t:LevelCatalog");
            Assert.AreEqual(1, catalogs.Length, "Expected exactly one LevelCatalog asset.");
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelCatalog>(UnityEditor.AssetDatabase.GUIDToAssetPath(catalogs[0]));

            foreach (TextAsset file in catalog.Levels)
            {
                Assert.IsNotNull(file, "The level catalog has an empty entry.");
                bool ok = CatalogLevelProvider.TryParse(file.name, file.text, out Level level, out var errors);
                Assert.IsTrue(ok, $"{file.name}: {string.Join(" ", errors)}");

                var result = LevelSolver.Solve(level);

                Assert.AreEqual(SolveOutcome.Solvable, result.Outcome, $"{file.name} cannot be finished.");
                Assert.AreEqual(SessionState.Won, Replay(level, result).State, $"{file.name}: the solver's moves do not win.");
            }
        }

        [Test]
        public void Clone_IsItsOwnSession()
        {
            var session = TestLevels.Playing("RB", "RRRBBB");
            session.Tap(new GridPos(0, 3));     // a blue waits
            bool originalHeard = false;
            session.PassengerLeftBoard += (passenger, path, seat) => originalHeard = true;

            var clone = session.Clone();
            CollectionAssert.AreEqual(session.Moves, clone.Moves);
            clone.Tap(new GridPos(0, 0));
            clone.Tap(new GridPos(0, 1));
            clone.Tap(new GridPos(0, 2));       // the clone's red bus leaves
            clone.Tap(new GridPos(0, 4));       // and another blue waits

            Assert.IsFalse(originalHeard, "The clone must not raise the original's events.");
            Assert.IsNotNull(session.Board.PassengerAt(new GridPos(0, 0)));
            Assert.AreEqual(0, session.Buses.CurrentIndex);
            Assert.AreEqual(0, session.Buses.SeatsTaken);
            Assert.IsTrue(session.Buses.IsAtStop);
            Assert.AreEqual(1, session.Waiting.Occupied);
            Assert.AreEqual(1, session.Moves.Count);
            Assert.AreEqual(1, clone.Buses.CurrentIndex);
            Assert.AreEqual(2, clone.Waiting.Occupied);
            Assert.AreEqual(5, clone.Moves.Count);
        }

        // Plays the solver's moves on a fresh session the way the solver means them: whoever is tapped is in
        // its seat before the next move, and the next bus pulls in only where the moves say so.
        private static LevelSession Replay(Level level, SolveResult result)
        {
            var session = new LevelSession(level);
            session.Start();
            foreach (var move in result.Moves)
            {
                int bus = session.Buses.CurrentIndex;
                if (move == LevelSession.BusArrivedMark)
                {
                    Assert.IsFalse(session.Buses.IsAtStop, "A bus is let in while one is at the stop.");
                    session.BusArrived();
                }
                else
                {
                    Assert.AreEqual(TapResult.Moved, session.Tap(move), $"The tap on {move} did nothing.");
                    session.WaitingSeatsFilled();
                }
                if (session.Buses.CurrentIndex != bus)
                {
                    session.BusBoarded();
                }
            }
            CollectionAssert.AreEqual(result.Moves, session.Moves);
            return session;
        }
    }
}
