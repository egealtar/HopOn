using System;
using System.Collections.Generic;
using System.Text;

namespace HopOn.Logic
{
    public enum SolveOutcome
    {
        /// <summary>A sequence of taps finishes the level (see <see cref="SolveResult.Moves"/>).</summary>
        Solvable,
        /// <summary>Every position the level can reach was tried and none of them wins without a race for
        /// the last waiting seat (see <see cref="LevelSolver"/>).</summary>
        Unsolvable,
        /// <summary>The search ran out of budget before it could tell.</summary>
        TooBig,
    }

    public sealed class SolveResult
    {
        public SolveOutcome Outcome { get; }

        /// <summary>The winning taps in order, with <see cref="LevelSession.BusArrivedMark"/> where the next bus
        /// is let in: the same form as <see cref="LevelSession.Moves"/>. Empty unless the level is solvable.</summary>
        public IReadOnlyList<GridPos> Moves { get; }

        /// <summary>How many different positions the search looked at.</summary>
        public int StatesVisited { get; }

        internal SolveResult(SolveOutcome outcome, IReadOnlyList<GridPos> moves, int statesVisited)
        {
            Outcome = outcome;
            Moves = moves;
            StatesVisited = statesVisited;
        }
    }

    /// <summary>
    /// Tells whether a level can be finished, for the level editor and the tests. It knows no rules of
    /// its own: it plays copies of a real <see cref="LevelSession"/> and tries every tap, depth first,
    /// remembering the positions it has already seen.
    /// It plays with an instant view: whoever is tapped is in its seat at once, so the riders of a full bus
    /// are in before the next tap, and a passenger sent to a waiting seat has crossed the line before the
    /// next tap. Only letting the next bus in is a move of its own, because the player can tap while buses
    /// change. So nothing here is ever a race: Solvable means the level can be finished without winning one.
    /// A level that only a race could finish (taking the last waiting seat and filling the bus before that
    /// passenger gets there) is reported Unsolvable, which is what a level designer wants to hear.
    /// The time limit is not played: whether the taps fit in the time is for a play test.
    /// The level editor calls Solve off the main thread, so nothing in the Logic classes may keep static state.
    /// </summary>
    public static class LevelSolver
    {
        public const int DefaultMaxStates = 200000;

        public static SolveResult Solve(Level level, int maxStates = DefaultMaxStates)
        {
            var start = new LevelSession(level);
            start.Start();
            var seen = new HashSet<string>();
            bool tooBig = false;
            var won = Search(start, seen, maxStates, ref tooBig);
            if (won != null)
            {
                return new SolveResult(SolveOutcome.Solvable, won.Moves, seen.Count);
            }
            return new SolveResult(tooBig ? SolveOutcome.TooBig : SolveOutcome.Unsolvable, new GridPos[0], seen.Count);
        }

        // The session that won, or null: no win from here, or the budget ran out (tooBig).
        // Every move takes a passenger off the board or lets a bus in, so the search always ends.
        private static LevelSession Search(LevelSession session, HashSet<string> seen, int maxStates, ref bool tooBig)
        {
            if (session.State == SessionState.Won)
            {
                return session;
            }
            string key = Key(session);
            if (seen.Contains(key))
            {
                return null;
            }
            if (seen.Count >= maxStates)
            {
                tooBig = true;
                return null;
            }
            seen.Add(key);

            foreach (var move in MovesToTry(session))
            {
                var next = session.Clone();
                if (!Play(next, move))
                {
                    continue;
                }
                var won = Search(next, seen, maxStates, ref tooBig);
                if (won != null || tooBig)
                {
                    return won;
                }
            }
            return null;
        }

        // The most promising first, so a level that can be finished is finished quickly: passengers the bus
        // at the stop would take, then letting the next bus in, then everyone else (they take waiting seats).
        // Only the order is decided here: whether a tap does anything is up to the session.
        private static List<GridPos> MovesToTry(LevelSession session)
        {
            var boarding = new List<GridPos>();
            var others = new List<GridPos>();
            foreach (var cell in session.Level.AllPositions())
            {
                var passenger = session.Board.PassengerAt(cell);
                if (passenger != null)
                {
                    (session.Buses.CanBoard(passenger.Color) ? boarding : others).Add(cell);
                }
            }
            if (!session.Buses.IsAtStop)
            {
                boarding.Add(LevelSession.BusArrivedMark);
            }
            boarding.AddRange(others);
            return boarding;
        }

        // One move through the real rules, reported back the way the instant view would. False when the
        // move did nothing or lost the level.
        private static bool Play(LevelSession session, GridPos move)
        {
            int bus = session.Buses.CurrentIndex;
            if (move == LevelSession.BusArrivedMark)
            {
                session.BusArrived();
            }
            else
            {
                int waiting = session.Waiting.Occupied;
                if (session.Tap(move) != TapResult.Moved)
                {
                    return false;
                }
                if (session.Waiting.Occupied > waiting)
                {
                    session.WaitingSeatsFilled();   // it is across the line at once: the last seat beside a bus loses here
                }
            }
            if (session.Buses.CurrentIndex != bus)
            {
                session.BusBoarded();               // a bus left: its riders are in at once
            }
            return session.State != SessionState.Lost;
        }

        // Everything that decides what can still happen: who is still on the board, how many of each colour
        // sit in the waiting seats (which seat does not matter: a bus takes its colour wherever it sits), the
        // line off the board in order, and the bus at the front. The session's two "the view has not reported
        // yet" flags are not in it because the instant view never leaves them set: BusBoarded follows every
        // bus that leaves, and a tap that sets the pending loss loses at once in WaitingSeatsFilled.
        private static string Key(LevelSession session)
        {
            var key = new StringBuilder();
            foreach (var cell in session.Level.AllPositions())
            {
                key.Append(session.Board.PassengerAt(cell) != null ? '1' : '0');
            }
            var waiting = new int[Enum.GetValues(typeof(PassengerColor)).Length];
            for (int seat = 0; seat < session.Waiting.SeatCount; seat++)
            {
                var passenger = session.Waiting.At(seat);
                if (passenger != null)
                {
                    waiting[(int)passenger.Color]++;
                }
            }
            key.Append(' ').Append(string.Join(",", waiting)).Append(' ');
            foreach (var passenger in session.Queue)
            {
                key.Append((int)passenger.Color);
            }
            key.Append(' ').Append(session.Buses.CurrentIndex)
               .Append(' ').Append(session.Buses.SeatsTaken)
               .Append(session.Buses.IsAtStop ? " at the stop" : " away");
            return key.ToString();
        }
    }
}
