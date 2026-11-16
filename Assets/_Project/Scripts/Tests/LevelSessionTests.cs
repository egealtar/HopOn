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
            session.Tap(new GridPos(0, 3));     // red bus full: leaves; the stop is empty until the blue bus pulls in

            Assert.AreEqual(1, session.Buses.CurrentIndex);
            Assert.IsFalse(session.Buses.IsAtStop);
            Assert.AreEqual(1, session.Waiting.Occupied, "The blue waits until its bus is at the stop.");

            session.BusArrived();               // the blue bus takes the waiting blue

            Assert.IsTrue(session.Buses.IsAtStop);
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
            session.Tap(new GridPos(0, 5));     // green bus leaves
            session.BusArrived();               // blue bus pulls in and takes the blue from seat 1

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
                session.Tap(new GridPos(0, col));   // three blues wait, three reds fill the red bus
            }
            Assert.AreEqual(SessionState.Playing, session.State);

            session.BusArrived();                   // the blue bus fills from the waiting seats and leaves: the last one

            Assert.AreEqual(SessionState.Won, session.State);
        }

        [Test]
        public void TapWhileBusesChange_GoesToAWaitingSeat_EvenForTheComingBusColour()
        {
            var session = TestLevels.Playing("RB", "RRRBBB");
            session.Tap(new GridPos(0, 0));
            session.Tap(new GridPos(0, 1));
            session.Tap(new GridPos(0, 2));     // red bus full: leaves

            session.Tap(new GridPos(0, 3));     // a blue, before the blue bus is at the stop

            Assert.AreEqual(PassengerColor.Blue, session.Waiting.At(0).Color, "No bus is at the stop: it waits.");
            Assert.AreEqual(0, session.Buses.SeatsTaken);

            session.BusArrived();
            session.Tap(new GridPos(0, 4));     // now straight onto the bus

            Assert.AreEqual(2, session.Buses.SeatsTaken);
            Assert.AreEqual(0, session.Waiting.Occupied);
        }

        [Test]
        public void BusArrived_WithNoBusOnItsWay_ChangesNothing()
        {
            var session = TestLevels.Playing("RB", "RRRBBB");
            session.Tap(new GridPos(0, 0));

            session.BusArrived();               // the red bus is at the stop already

            Assert.AreEqual(0, session.Buses.CurrentIndex);
            Assert.AreEqual(1, session.Buses.SeatsTaken);
            Assert.AreEqual(1, session.Moves.Count, "Nothing to save.");
        }

        [Test]
        public void WaitingSeatsFillingWhileBusesChange_WaitForTheComingBus_WhichFreesThem()
        {
            var session = TestLevels.Playing("RBG", "RRRBBBGGG");
            session.Tap(new GridPos(0, 0));
            session.Tap(new GridPos(0, 1));
            session.Tap(new GridPos(0, 2));     // red bus full: leaves
            session.BusBoarded();               // its riders are in on screen
            for (int col = 3; col < 8; col++)
            {
                session.Tap(new GridPos(0, col));   // three blues and two greens fill every waiting seat
            }
            Assert.IsTrue(session.Waiting.IsFull);
            session.WaitingSeatsFilled();       // all five sit down before the bus pulls in
            Assert.AreEqual(SessionState.Playing, session.State, "The coming bus may still free seats.");

            session.BusArrived();               // the blue bus takes its three and leaves

            Assert.AreEqual(SessionState.Playing, session.State);
            Assert.AreEqual(2, session.Waiting.Occupied);
            session.BusArrived();               // the green bus takes its two
            session.Tap(new GridPos(0, 8));     // and the last green: the last bus leaves

            Assert.AreEqual(SessionState.Won, session.State);
        }

        [Test]
        public void WaitingSeatsFullWhenTheComingBusTakesNobody_LosesWithNoSeats()
        {
            var session = TestLevels.Playing("RGBY", "RRRBBBYYYGGG");
            session.Tap(new GridPos(0, 0));
            session.Tap(new GridPos(0, 1));
            session.Tap(new GridPos(0, 2));     // red bus full: leaves, the green bus is coming
            session.BusBoarded();
            for (int col = 3; col < 8; col++)
            {
                session.Tap(new GridPos(0, col));   // blues and yellows fill every waiting seat
            }
            Assert.AreEqual(SessionState.Playing, session.State);

            session.BusArrived();               // green: none of them

            Assert.AreEqual(SessionState.Lost, session.State);
            Assert.AreEqual(LoseReason.NoSeats, session.LoseReason);
        }

        [Test]
        public void TapWhileBusesChange_WithEveryWaitingSeatTaken_IsHeldBack()
        {
            var session = TestLevels.Playing("RBG", "RRRBBBGGG");
            for (int col = 0; col < 3; col++)
            {
                session.Tap(new GridPos(0, col));   // red bus leaves
            }
            session.BusBoarded();
            for (int col = 3; col < 8; col++)
            {
                session.Tap(new GridPos(0, col));   // five fill the waiting seats
            }

            Assert.AreEqual(TapResult.Ignored, session.Tap(new GridPos(0, 8)), "Nowhere to go until the bus pulls in.");
            Assert.IsNotNull(session.Board.PassengerAt(new GridPos(0, 8)));
            Assert.AreEqual(0, session.Queue.Count);
        }

        [Test]
        public void RidersOnTheirWay_HoldBackTheTapForTheLastWaitingSeat_UntilTheyAreIn()
        {
            // Bus Jam: a red tapped first is never beaten to the stop by a passenger for the last seat.
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            for (int col = 0; col < 3; col++)
            {
                session.Tap(new GridPos(0, col));       // three blues wait
            }
            session.Tap(new GridPos(0, 6));
            session.Tap(new GridPos(0, 7));
            session.Tap(new GridPos(0, 8));             // the reds fill the bus and are on their way

            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 3)), "Not the last seat: it goes.");
            Assert.AreEqual(TapResult.Ignored, session.Tap(new GridPos(0, 4)), "The last seat waits for the reds.");
            Assert.IsNotNull(session.Board.PassengerAt(new GridPos(0, 4)));

            session.BusBoarded();                       // the reds are in

            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 4)));
            session.WaitingSeatsFilled();
            Assert.AreEqual(SessionState.Playing, session.State, "The coming blue bus decides.");
        }

        [Test]
        public void PassengerWithNowhereToGo_WaitsOffTheBoard_AndTakesTheSeatTheNextBusFrees_WithoutLosing()
        {
            // Bus Jam: a red saves the bus at the stop, a green tapped meanwhile waits off the board, and the blue
            // bus frees one seat for it. Every seat is taken again, yet the level goes on.
            var session = TestLevels.Playing("RBGY", "RRGGYBYRGBBY");
            session.Tap(new GridPos(0, 0));
            session.Tap(new GridPos(0, 1));             // two reds in the red bus
            for (int col = 2; col < 6; col++)
            {
                session.Tap(new GridPos(0, col));       // green, green, yellow, blue wait
            }
            session.Tap(new GridPos(0, 6));             // a yellow takes the last seat
            session.Tap(new GridPos(0, 7));             // the last red fills the bus: a race

            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 8)), "A green with nowhere to go sets off.");
            Assert.IsNull(session.Board.PassengerAt(new GridPos(0, 8)));
            Assert.AreEqual(1, session.Queue.Count);

            session.BusBoarded();                       // the red crosses the line first
            session.WaitingSeatsFilled();
            Assert.AreEqual(SessionState.Playing, session.State);

            session.BusArrived();                       // the blue bus takes the waiting blue; the green sits down

            Assert.AreEqual(0, session.Queue.Count);
            Assert.AreEqual(PassengerColor.Green, session.Waiting.At(3).Color);
            Assert.IsTrue(session.Waiting.IsFull);
            session.WaitingSeatsFilled();
            Assert.AreEqual(SessionState.Playing, session.State, "Seats filled by the line moving up decide nothing.");

            session.Tap(new GridPos(0, 9));
            session.Tap(new GridPos(0, 10));            // the blue bus is full
            session.BusBoarded();
            session.BusArrived();                       // the green bus takes the three greens and leaves
            session.BusArrived();                       // the yellow bus takes two yellows
            session.Tap(new GridPos(0, 11));            // and the last one

            Assert.AreEqual(SessionState.Won, session.State);
        }

        [Test]
        public void TheLineOffTheBoard_HoldsAtMostItsLength()
        {
            var session = TestLevels.Playing("RBBB", "BBBBBBBBBRRR");
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));       // five blues take every waiting seat
            }
            for (int col = 5; col < 5 + LevelSession.QueueLength; col++)
            {
                Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, col)));
            }

            Assert.AreEqual(TapResult.Ignored, session.Tap(new GridPos(0, 5 + LevelSession.QueueLength)));
            session.WaitingSeatsFilled();
            Assert.AreEqual(SessionState.Lost, session.State, "The red bus at the stop takes none of them.");
        }

        [Test]
        public void LastWaitingSeatSatIn_LosesWithNoSeats()
        {
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));
            }
            Assert.AreEqual(SessionState.Playing, session.State, "Not before the last one sits down on screen.");

            session.WaitingSeatsFilled();

            Assert.AreEqual(SessionState.Lost, session.State);
            Assert.AreEqual(LoseReason.NoSeats, session.LoseReason);
            Assert.AreEqual(TapResult.Ignored, session.Tap(new GridPos(0, 6)));
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
        public void WaitingSeatsTaken_PassengersForTheBusAtTheStop_FillItBeforeTheSeatsAreSatIn_AndPlayGoesOn()
        {
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));       // five blues take every waiting seat
            }

            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 6)));
            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 7)));
            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 8)));   // the red bus is full: it leaves
            Assert.IsFalse(session.Buses.IsAtStop);
            session.BusBoarded();                       // the reds reach the bus before the last blue reaches its seat
            session.WaitingSeatsFilled();               // the blues sit down while the stop is empty
            Assert.AreEqual(SessionState.Playing, session.State, "The coming blue bus decides.");

            session.BusArrived();                       // the blue bus takes three and leaves
            session.BusArrived();                       // the next takes the other two
            session.Tap(new GridPos(0, 5));             // and the last blue

            Assert.AreEqual(SessionState.Won, session.State);
        }

        [Test]
        public void WaitingSeatsTaken_RiderForTheBusAtTheStop_ArrivingAfterTheLastSeatIsSatIn_Loses()
        {
            // Ege's case: the bus at the stop has two riders, every waiting seat is taken, and a red far away is
            // tapped. It fills the bus, but the last waiting passenger sits down before it gets in.
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            session.Tap(new GridPos(0, 6));
            session.Tap(new GridPos(0, 7));             // two reds in the red bus
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));       // five blues take every waiting seat
            }
            session.Tap(new GridPos(0, 8));             // the last red: the bus is full, but the red is still on its way

            session.WaitingSeatsFilled();               // the last blue sits down first

            Assert.AreEqual(SessionState.Lost, session.State);
            Assert.AreEqual(LoseReason.NoSeats, session.LoseReason);
        }

        [Test]
        public void WaitingSeatsFillingAfterTheBusFilledNormally_StillWaitForTheComingBus()
        {
            // Not a race: the bus was full before the waiting seats were all taken, so the next bus decides.
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            session.Tap(new GridPos(0, 6));
            session.Tap(new GridPos(0, 7));
            session.Tap(new GridPos(0, 8));             // the red bus is full: it leaves
            session.BusBoarded();                       // its riders are in before the last seat can be taken
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));       // five blues while buses change
            }

            session.WaitingSeatsFilled();               // they sit down before the red bus has even driven off

            Assert.AreEqual(SessionState.Playing, session.State);
        }

        [Test]
        public void WaitingSeatsTaken_BusAtTheStopNotFilledInTime_LosesWhenTheSeatsAreSatIn()
        {
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));
            }
            session.Tap(new GridPos(0, 6));             // one red boards, the red bus still has room

            session.WaitingSeatsFilled();

            Assert.AreEqual(SessionState.Lost, session.State);
            Assert.AreEqual(LoseReason.NoSeats, session.LoseReason);
        }

        [Test]
        public void WaitingSeatsTaken_PassengerWithNowhereToGo_WaitsOffTheBoard_AndTheSeatsSatInStillLose()
        {
            var session = TestLevels.Playing("RBB", "BBBBBBRRR");
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                session.Tap(new GridPos(0, col));
            }

            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 5)));     // a blue: no seat, it waits off the board
            Assert.IsNull(session.Board.PassengerAt(new GridPos(0, 5)));
            Assert.AreEqual(1, session.Queue.Count);
            Assert.AreEqual(TapResult.Moved, session.Tap(new GridPos(0, 6)));     // a red still boards: the bus has room
            Assert.AreEqual(1, session.Buses.SeatsTaken);
            session.WaitingSeatsFilled();

            Assert.AreEqual(SessionState.Lost, session.State, "The bus did not fill in time.");
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
        public void Resume_ReplaysBusArrivals_ToTheSameState()
        {
            var level = TestLevels.Make("RB", "BRRR", "BB..");
            var played = new LevelSession(level);
            played.Start();
            played.Tap(new GridPos(0, 0));      // blue waits
            played.Tap(new GridPos(0, 1));
            played.Tap(new GridPos(0, 2));
            played.Tap(new GridPos(0, 3));      // red bus leaves
            played.Tap(new GridPos(1, 0));      // a blue while buses change: waits too
            played.BusArrived();                // the blue bus takes both
            Assert.AreEqual(6, played.Moves.Count);

            Assert.IsTrue(LevelSession.TryResume(level, played.Moves, 30f, out var resumed));

            Assert.AreEqual(played.Buses.CurrentIndex, resumed.Buses.CurrentIndex);
            Assert.AreEqual(played.Buses.SeatsTaken, resumed.Buses.SeatsTaken);
            Assert.AreEqual(played.Waiting.Occupied, resumed.Waiting.Occupied);
            Assert.IsTrue(resumed.Buses.IsAtStop);
        }

        [Test]
        public void Resume_SavedWhileBusesChange_StartsWithTheBusAtTheStop()
        {
            var level = TestLevels.Make("RB", "BRRR", "BB..");
            var played = new LevelSession(level);
            played.Start();
            played.Tap(new GridPos(0, 0));
            played.Tap(new GridPos(0, 1));
            played.Tap(new GridPos(0, 2));
            played.Tap(new GridPos(0, 3));      // saved before the blue bus pulled in

            Assert.IsTrue(LevelSession.TryResume(level, played.Moves, 30f, out var resumed));

            Assert.IsTrue(resumed.Buses.IsAtStop, "The resumed level shows the bus at the stop.");
            Assert.AreEqual(1, resumed.Buses.SeatsTaken);
            Assert.AreEqual(0, resumed.Waiting.Occupied);
        }

        [Test]
        public void Resume_SavedWithEveryWaitingSeatTaken_LosesOnPlay()
        {
            var level = TestLevels.Make("RBB", "BBBBBBRRR");
            var played = new LevelSession(level);
            played.Start();
            for (int col = 0; col < Level.WaitingSeats; col++)
            {
                played.Tap(new GridPos(0, col));        // saved before the last one sat down
            }

            Assert.IsTrue(LevelSession.TryResume(level, played.Moves, 30f, out var resumed));
            Assert.AreEqual(SessionState.Ready, resumed.State);
            resumed.Start();                            // they all sit in their seats from the start

            Assert.AreEqual(SessionState.Lost, resumed.State);
            Assert.AreEqual(LoseReason.NoSeats, resumed.LoseReason);
        }

        [Test]
        public void Resume_SavedWhileBusesChange_WithEveryWaitingSeatTaken_AndTheComingBusTakingNobody_LosesOnPlay()
        {
            var level = TestLevels.Make("RGBY", "RRRBBBYYYGGG");
            var played = new LevelSession(level);
            played.Start();
            for (int col = 0; col < 3; col++)
            {
                played.Tap(new GridPos(0, col));        // the red bus leaves
            }
            played.BusBoarded();
            for (int col = 3; col < 8; col++)
            {
                played.Tap(new GridPos(0, col));        // blues and yellows take every seat
            }
            Assert.IsTrue(played.Waiting.IsFull);

            Assert.IsTrue(LevelSession.TryResume(level, played.Moves, 30f, out var resumed));
            Assert.AreEqual(SessionState.Ready, resumed.State, "Nothing is decided before Play.");
            resumed.Start();

            Assert.AreEqual(SessionState.Lost, resumed.State);
        }

        [Test]
        public void Resume_WhereTheArrivingBusFillsAndLeaves_PullsInTheNextOne()
        {
            var level = TestLevels.Make("RBB", "BBBRRRBBB");
            var played = new LevelSession(level);
            played.Start();
            for (int col = 0; col < 6; col++)
            {
                played.Tap(new GridPos(0, col));        // three blues wait, the red bus fills and leaves
            }

            Assert.IsTrue(LevelSession.TryResume(level, played.Moves, 30f, out var resumed));

            Assert.AreEqual(2, resumed.Buses.CurrentIndex, "The first blue bus took the waiting three and left.");
            Assert.IsTrue(resumed.Buses.IsAtStop);
            resumed.Start();
            Assert.AreEqual(TapResult.Moved, resumed.Tap(new GridPos(0, 6)));
            Assert.AreEqual(1, resumed.Buses.SeatsTaken);
        }

        [Test]
        public void Resume_WhereTheLastBusFillsAndLeaves_WinsOnPlay()
        {
            var level = TestLevels.Make("RB", "BBBRRR");
            var played = new LevelSession(level);
            played.Start();
            for (int col = 0; col < 6; col++)
            {
                played.Tap(new GridPos(0, col));        // three blues wait, the red bus leaves; the blue bus would take them
            }

            Assert.IsTrue(LevelSession.TryResume(level, played.Moves, 30f, out var resumed));
            Assert.AreEqual(SessionState.Ready, resumed.State, "Nothing is decided before Play.");
            resumed.Start();

            Assert.AreEqual(SessionState.Won, resumed.State);
        }

        [Test]
        public void Resume_SavedWithSomeoneWaitingOffTheBoard_SeatsItWhenTheNextBusFreesASeat()
        {
            var level = TestLevels.Make("RBGY", "RRGGYBYRGBBY");
            var played = new LevelSession(level);
            played.Start();
            for (int col = 0; col < 9; col++)
            {
                played.Tap(new GridPos(0, col));        // as in Bus Jam: a race, and a green off the board
            }
            Assert.AreEqual(1, played.Queue.Count);

            Assert.IsTrue(LevelSession.TryResume(level, played.Moves, 30f, out var resumed));

            Assert.AreEqual(0, resumed.Queue.Count, "The blue bus pulled in and freed a seat for it.");
            Assert.AreEqual(PassengerColor.Green, resumed.Waiting.At(3).Color);
            resumed.Start();
            Assert.AreEqual(SessionState.Playing, resumed.State);
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
