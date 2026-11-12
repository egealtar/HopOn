using System.Collections.Generic;
using HopOn.Logic;
using NUnit.Framework;

namespace HopOn.Tests
{
    /// <summary>The rules of play, one behaviour per test. Board rows read like the screen: row 0
    /// is the top row, where passengers leave the board. Buses are written in arrival order.</summary>
    public sealed class LevelSessionTests
    {
        [Test]
        public void Tap_BeforePlay_IsIgnored()
        {
            var session = new LevelSession(TestLevels.Make("R", "RRR"));

            Assert.AreEqual(TapResult.Ignored, session.Tap(new GridPos(0, 0)));
            Assert.IsNotNull(session.Board.PassengerAt(new GridPos(0, 0)));
        }

        [Test]
        public void Tap_MatchingBus_BoardsIt()
        {
            var session = TestLevels.Playing("R", "RRR");

            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 0)));
            Assert.IsNull(session.Board.PassengerAt(new GridPos(0, 0)));
            Assert.AreEqual(1, session.Buses.SeatsTaken);
            Assert.AreEqual(0, session.Waiting.Occupied);
        }

        [Test]
        public void Tap_OtherColour_TakesTheLeftmostWaitingSeat()
        {
            var session = TestLevels.Playing("RB", "RRRBBB");

            session.Tap(new GridPos(0, 3));

            Assert.AreEqual(PassengerColor.Blue, session.Waiting.At(0).Color);
        }

        [Test]
        public void Tap_WalledIn_IsBlockedUntilTheWayOpens()
        {
            var session = TestLevels.Playing("R", "R", "R", "R");

            Assert.AreEqual(TapResult.Blocked, session.Tap(new GridPos(2, 0)));
            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 0)));
            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(1, 0)));
            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(2, 0)));
        }

        [Test]
        public void Path_GoesAroundObstaclesAndPassengers()
        {
            // The two top-right exits are taken, so the passenger at the bottom right walks left around the wall.
            var session = TestLevels.Playing("R", ".#RR", ".#..", "...R");

            Assert.IsTrue(session.Board.TryFindPathToExit(new GridPos(2, 3), out var path));
            var expected = new List<GridPos>
            {
                new GridPos(2, 3), new GridPos(2, 2), new GridPos(2, 1), new GridPos(2, 0), new GridPos(1, 0), new GridPos(0, 0),
            };
            CollectionAssert.AreEqual(expected, path);
        }

        [Test]
        public void FullBus_Leaves_AndTheNextBusTakesItsWaitingPassengers()
        {
            var session = TestLevels.Playing("RB", "BRRR", "BB..");

            session.Tap(new GridPos(0, 0));     // blue waits
            session.Tap(new GridPos(0, 1));
            session.Tap(new GridPos(0, 2));
            session.Tap(new GridPos(0, 3));     // red bus full: leaves, blue bus takes the waiting blue

            Assert.AreEqual(1, session.Buses.CurrentIndex);
            Assert.AreEqual(1, session.Buses.SeatsTaken);
            Assert.AreEqual(0, session.Waiting.Occupied);
            Assert.AreEqual(SessionState.Playing, session.State);
        }

        [Test]
        public void WaitingSeats_DoNotShift_AndTheNextPassengerFillsTheGap()
        {
            var session = TestLevels.Playing("GBR", "RBRGGG", "BBR...");
            session.Tap(new GridPos(0, 0));     // red: seat 0
            session.Tap(new GridPos(0, 1));     // blue: seat 1
            session.Tap(new GridPos(0, 2));     // red: seat 2
            session.Tap(new GridPos(0, 3));
            session.Tap(new GridPos(0, 4));
            session.Tap(new GridPos(0, 5));     // green bus leaves, blue bus takes the blue from seat 1

            Assert.AreEqual(PassengerColor.Red, session.Waiting.At(0).Color);
            Assert.IsNull(session.Waiting.At(1));
            Assert.AreEqual(PassengerColor.Red, session.Waiting.At(2).Color);

            session.Tap(new GridPos(1, 2));     // a red, while the blue bus is at the stop
            Assert.AreEqual(PassengerColor.Red, session.Waiting.At(1).Color);
        }

        [Test]
        public void LastBusLeaving_WinsTheLevel()
        {
            var session = TestLevels.Playing("RB", "BBBRRR");

            for (int col = 0; col < 6; col++)
            {
                session.Tap(new GridPos(0, col));
            }

            Assert.AreEqual(SessionState.Won, session.State);
        }

        [Test]
        public void LastWaitingSeatFilling_LosesWithNoSeats()
        {
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");

            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));
            }

            Assert.AreEqual(SessionState.Lost, session.State);
            Assert.AreEqual(LoseReason.NoSeats, session.LoseReason);
        }

        [Test]
        public void Tap_AfterTimeRanOut_IsIgnored()
        {
            var session = new LevelSession(TestLevels.MakeTimed(10f, "R", "RRR"));
            session.Start();
            session.Tick(11f);

            Assert.AreEqual(TapResult.Ignored, session.Tap(new GridPos(0, 0)));
        }

        [Test]
        public void AfterSeatsFilled_PassengerForTheBusAtTheStop_StillSetsOff_ButNoBusLeaves()
        {
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));       // five blues fill the waiting seats: lost
            }

            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 6)));
            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 7)));
            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 8)));
            Assert.AreEqual(0, session.Buses.CurrentIndex, "The red bus is full but must not leave: the level is over.");
            Assert.AreEqual(SessionState.Lost, session.State);
        }

        [Test]
        public void AfterSeatsFilled_PassengerWithNowhereToGo_DoesNotMove()
        {
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));
            }

            Assert.AreEqual(TapResult.Ignored, session.Tap(new GridPos(0, 5)));    // a blue: no waiting seat left
            Assert.IsNotNull(session.Board.PassengerAt(new GridPos(0, 5)));
        }

        [Test]
        public void Tick_BeforePlay_DoesNothing()
        {
            var session = new LevelSession(TestLevels.MakeTimed(10f, "R", "RRR"));

            session.Tick(5f);

            Assert.AreEqual(10f, session.RemainingSeconds);
        }

        [Test]
        public void Tick_PastTheLimit_LosesWithTimeUp()
        {
            var session = new LevelSession(TestLevels.MakeTimed(10f, "R", "RRR"));
            session.Start();

            session.Tick(4f);
            Assert.AreEqual(6f, session.RemainingSeconds, 0.001f);
            session.Tick(6.5f);

            Assert.AreEqual(SessionState.Lost, session.State);
            Assert.AreEqual(LoseReason.TimeUp, session.LoseReason);
            Assert.AreEqual(0f, session.RemainingSeconds);
        }

        [Test]
        public void Tick_AfterAWinInTheLastMoment_DoesNotLose()
        {
            var session = new LevelSession(TestLevels.MakeTimed(10f, "R", "RRR"));
            session.Start();
            session.Tick(9.9f);
            session.Tap(new GridPos(0, 0));
            session.Tap(new GridPos(0, 1));
            session.Tap(new GridPos(0, 2));     // the last bus leaves with 0.1 s to spare

            session.Tick(5f);

            Assert.AreEqual(SessionState.Won, session.State);
        }

        [Test]
        public void Resume_ReplaysTheSavedTaps_ToTheSameState()
        {
            var level = TestLevels.Make("GBR", "RBRGGG", "BBR...");
            var played = new LevelSession(level);
            played.Start();
            played.Tap(new GridPos(0, 0));
            played.Tap(new GridPos(0, 1));
            Assert.AreEqual(TapResult.Blocked, played.Tap(new GridPos(1, 2)));   // not a move, so not saved
            played.Tap(new GridPos(0, 3));
            Assert.AreEqual(3, played.Moves.Count);

            Assert.IsTrue(LevelSession.TryResume(level, played.Moves, 30f, out var resumed));

            Assert.AreEqual(SessionState.Ready, resumed.State);
            Assert.AreEqual(30f, resumed.RemainingSeconds);
            Assert.AreEqual(played.Buses.CurrentIndex, resumed.Buses.CurrentIndex);
            Assert.AreEqual(played.Buses.SeatsTaken, resumed.Buses.SeatsTaken);
            for (int seat = 0; seat < Level.WaitingSeats; seat++)
            {
                Assert.AreEqual(played.Waiting.At(seat)?.Id, resumed.Waiting.At(seat)?.Id);
            }
            foreach (var cell in level.AllPositions())
            {
                Assert.AreEqual(played.Board.PassengerAt(cell)?.Id, resumed.Board.PassengerAt(cell)?.Id);
            }
        }

        [Test]
        public void Resume_WithTapsThatNoLongerFit_StartsOver()
        {
            // The saved tap points at a passenger who cannot move (the level file changed since).
            var level = TestLevels.Make("R", "R", "R", "R");
            var moves = new List<GridPos> { new GridPos(2, 0) };

            Assert.IsFalse(LevelSession.TryResume(level, moves, 30f, out _));
        }

        [Test]
        public void Resume_WithNoTimeLeft_StartsOver()
        {
            var level = TestLevels.Make("R", "RRR");

            Assert.IsFalse(LevelSession.TryResume(level, new List<GridPos>(), 0f, out _));
        }

        [Test]
        public void Resume_WithABrokenSave_StartsOver()
        {
            var level = TestLevels.Make("R", "RRR");

            Assert.IsFalse(LevelSession.TryResume(level, new List<GridPos>(), float.NaN, out _));
            Assert.IsFalse(LevelSession.TryResume(level, null, 30f, out _));
        }
    }
}
