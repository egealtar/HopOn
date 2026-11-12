using System;
using System.Collections.Generic;

namespace HopOn.Logic
{
    public enum SessionState
    {
        /// <summary>Waiting for Play: taps and time are ignored.</summary>
        Ready,
        Playing,
        Won,
        Lost,
    }

    public enum TapResult
    {
        /// <summary>Nothing happened: the cell is empty, or the level is not being played.</summary>
        Ignored,
        /// <summary>The passenger has no free way out and stays where it is.</summary>
        Blocked,
        /// <summary>The passenger left the board for a bus seat or a waiting seat.</summary>
        Moved,
    }

    /// <summary>
    /// One level being played: every rule of the game in one place, the time limit included.
    /// A tap is settled completely before <see cref="Tap"/> returns: the passenger leaves the
    /// board, its seat is reserved, full buses leave, the next bus takes its waiting passengers,
    /// and the level is won or lost. The events report each step in order, and the view plays
    /// them back as animations at its own pace. So two fast taps can never claim the same seat,
    /// and the level can never be both won and lost.
    /// Pure C# with no Unity types: the owner feeds it time through <see cref="Tick"/>, and it can
    /// be tested without the engine.
    /// </summary>
    public sealed class LevelSession
    {
        private readonly List<GridPos> _moves = new List<GridPos>();

        public Level Level { get; }
        public Board Board { get; }
        public BusQueue Buses { get; }
        public WaitingArea Waiting { get; }
        public SessionState State { get; private set; } = SessionState.Ready;

        /// <summary>Why the level was lost. Only meaningful when <see cref="State"/> is Lost.</summary>
        public LoseReason LoseReason { get; private set; }

        public float RemainingSeconds { get; private set; }

        /// <summary>The cell of every tap that moved a passenger, in order. This is what gets saved:
        /// replaying these taps on the same level rebuilds exactly this state (see <see cref="TryResume"/>).</summary>
        public IReadOnlyList<GridPos> Moves => _moves;

        /// <summary>A passenger left the board: it walks the path (from its cell to the exit, both
        /// included), then goes to the seat, on a bus or in the waiting area.</summary>
        public event Action<Passenger, IReadOnlyList<GridPos>, Seat> PassengerLeftBoard;

        /// <summary>A waiting passenger boards the bus that just pulled in: the waiting seat it
        /// leaves, then its bus seat.</summary>
        public event Action<Passenger, int, Seat> PassengerLeftWaiting;

        /// <summary>The front bus is full and leaves; the argument is its index in the bus order.
        /// The next bus, if there is one, pulls in at the same moment.</summary>
        public event Action<int> BusDeparted;

        public LevelSession(Level level)
        {
            Level = level ?? throw new ArgumentNullException(nameof(level));
            Board = new Board(level);
            Buses = new BusQueue(level.Buses);
            Waiting = new WaitingArea();
            RemainingSeconds = level.TimeLimitSeconds;
        }

        /// <summary>
        /// Rebuilds a level in progress from a save: replays the saved taps through the same rules,
        /// before anyone listens, so no animation plays and no separate restore code is needed.
        /// The result waits in Ready for Play, like a fresh level. False when the save no longer
        /// fits the level (its file was edited since): the caller then starts the level fresh.
        /// </summary>
        public static bool TryResume(Level level, IReadOnlyList<GridPos> moves, float remainingSeconds,
                                     out LevelSession session)
        {
            session = null;
            // Written as "not inside the range" so that a broken save with NaN seconds is refused too:
            // every comparison with NaN is false, so "NaN <= 0" alone would let it through.
            if (moves == null || !(remainingSeconds > 0f && remainingSeconds <= level.TimeLimitSeconds))
            {
                return false;
            }
            var replay = new LevelSession(level);
            foreach (var cell in moves)
            {
                // Every saved tap moved someone and the level never ended, or it would not have been saved.
                if (replay.Settle(cell) != TapResult.Moved || replay.State != SessionState.Ready)
                {
                    return false;
                }
            }
            replay.RemainingSeconds = remainingSeconds;
            session = replay;
            return true;
        }

        /// <summary>The player pressed Play: from now on taps count and time runs.</summary>
        public void Start()
        {
            if (State == SessionState.Ready)
            {
                State = SessionState.Playing;
            }
        }

        /// <summary>Advances the countdown. The owner calls it every frame with that frame's time.</summary>
        public void Tick(float deltaSeconds)
        {
            if (State != SessionState.Playing)
            {
                return;
            }
            RemainingSeconds -= deltaSeconds;
            if (RemainingSeconds <= 0f)
            {
                RemainingSeconds = 0f;
                Lose(LoseReason.TimeUp);
            }
        }

        public TapResult Tap(GridPos cell)
        {
            if (State == SessionState.Playing)
            {
                return Settle(cell);
            }
            if (State == SessionState.Lost && LoseReason == LoseReason.NoSeats)
            {
                return SettleAfterSeatsFilled(cell);
            }
            return TapResult.Ignored;
        }

        // Like Bus Jam: the level is lost the moment the last waiting seat is taken, but a passenger
        // who still has a seat on the bus at the stop may still set off; anyone else has nowhere
        // to go. The level is over, so these walks are only for show: no bus leaves and nothing
        // else changes. The session accepts such taps from now on; it is the view that stops
        // sending taps once it freezes (when that last passenger sits down on screen).
        private TapResult SettleAfterSeatsFilled(GridPos cell)
        {
            var passenger = Board.PassengerAt(cell);
            if (passenger == null || !Buses.CanBoard(passenger.Color))
            {
                return TapResult.Ignored;
            }
            if (!Board.TryFindPathToExit(cell, out var path))
            {
                return TapResult.Blocked;
            }
            Board.Remove(cell);
            var seat = Buses.TakeSeat();
            PassengerLeftBoard?.Invoke(passenger, path, seat);
            return TapResult.Moved;
        }

        // The rules of one tap, shared by Tap and by the replay in TryResume.
        private TapResult Settle(GridPos cell)
        {
            var passenger = Board.PassengerAt(cell);
            if (passenger == null)
            {
                return TapResult.Ignored;
            }
            if (!Board.TryFindPathToExit(cell, out var path))
            {
                return TapResult.Blocked;
            }

            Board.Remove(cell);
            _moves.Add(cell);
            if (Buses.CanBoard(passenger.Color))
            {
                var seat = Buses.TakeSeat();
                PassengerLeftBoard?.Invoke(passenger, path, seat);
                SendFullBusesAway();
            }
            else
            {
                int waitingSeat = Waiting.Sit(passenger);
                PassengerLeftBoard?.Invoke(passenger, path, Seat.Waiting(waitingSeat));
                if (Waiting.IsFull)
                {
                    Lose(LoseReason.NoSeats);
                }
            }
            return TapResult.Moved;
        }

        // While the front bus is full: it leaves, the next one pulls in and takes the waiting
        // passengers of its colour. A bus can fill up from the waiting seats alone, so one tap
        // can send several buses away.
        private void SendFullBusesAway()
        {
            while (Buses.IsCurrentFull)
            {
                int leaving = Buses.CurrentIndex;
                Buses.SendFrontAway();
                BusDeparted?.Invoke(leaving);

                // Passengers and bus seats match colour for colour (the level was checked when it
                // was created), so when the last bus leaves, nobody is left behind.
                if (Buses.AllGone)
                {
                    State = SessionState.Won;
                    return;
                }
                BoardWaitingPassengers();
            }
        }

        // Left to right, every waiting passenger of the new bus's colour boards while seats last.
        private void BoardWaitingPassengers()
        {
            for (int seat = 0; seat < Waiting.SeatCount && !Buses.IsCurrentFull; seat++)
            {
                var waiting = Waiting.At(seat);
                if (waiting != null && Buses.CanBoard(waiting.Color))
                {
                    Waiting.StandUp(seat);
                    // Take the seat first, never inside the ?.Invoke(...) below: when nobody listens,
                    // ?. skips the whole call, its arguments included, and the seat would never be taken.
                    var busSeat = Buses.TakeSeat();
                    PassengerLeftWaiting?.Invoke(waiting, seat, busSeat);
                }
            }
        }

        private void Lose(LoseReason reason)
        {
            LoseReason = reason;
            State = SessionState.Lost;
        }
    }
}
