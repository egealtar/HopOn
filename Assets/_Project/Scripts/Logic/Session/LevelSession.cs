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
        /// <summary>Nothing happened: the cell is empty, the level is not being played, or the tap is held back
        /// by a rule of the stop (see <see cref="LevelSession"/>). The player feels nothing.</summary>
        Ignored,
        /// <summary>The passenger has no free way out and stays where it is.</summary>
        Blocked,
        /// <summary>The passenger left the board: for a bus seat, a waiting seat, or the line off the board.</summary>
        Moved,
    }

    /// <summary>
    /// One level being played: every rule of the game in one place, the time limit included.
    /// A tap is settled completely before <see cref="Tap"/> returns: the passenger leaves the
    /// board, its seat is reserved (on the bus at the stop if that bus is its colour and has room,
    /// otherwise a waiting seat), a full bus leaves, and the level is won. The events report
    /// each step in order, and the view plays them back as animations at its own pace. So two fast
    /// taps can never claim the same seat, and the level can never be both won and lost.
    /// The stop, as observed in Bus Jam:
    /// - The stop is empty while buses change: from the tap that fills a bus until the view reports that
    ///   the next one has pulled in (<see cref="BusArrived"/>). The new bus takes the waiting passengers
    ///   of its colour, then the line off the board (below) moves up.
    /// - The riders who filled a bus are on their way until the view reports that they all crossed the line
    ///   in front of the waiting seats (<see cref="BusBoarded"/>). Meanwhile a tap that would take the last
    ///   free waiting seat is held back, so a rider tapped first is never beaten to it.
    /// - A tap that takes the last waiting seat while a bus is at the stop decides the level when that
    ///   passenger crosses the line (<see cref="WaitingSeatsFilled"/>): lost, unless the riders of that bus
    ///   filled it and crossed first (a race the view judges on screen).
    /// - With every waiting seat taken, a passenger with nowhere to go still sets off while the bus is at the
    ///   stop or its riders are on their way: it waits just off the board (<see cref="Queue"/>, at most
    ///   <see cref="QueueLength"/>) and takes the first seat a bus frees. Once buses change with every seat
    ///   taken, such taps are held back until the next bus has pulled in.
    /// - A bus that pulls in and takes nobody while every waiting seat is taken loses the level at once.
    ///   Seats filled by the line moving up decide nothing.
    /// Pure C# with no Unity types: the owner feeds it time through <see cref="Tick"/>, and it can
    /// be tested without the engine.
    /// </summary>
    public sealed class LevelSession
    {
        /// <summary>How many passengers can wait off the board for a seat.</summary>
        public const int QueueLength = 3;

        private readonly List<GridPos> _moves = new List<GridPos>();
        private readonly List<Passenger> _queue = new List<Passenger>();

        // A bus was filled and sent away, and the view has not yet seen all its riders cross the line.
        private bool _ridersOnTheWay;

        // A tap took the last waiting seat while a bus was at the stop: lost once that passenger crosses the line,
        // unless the bus is filled and its riders cross first. A bus pulling in clears it.
        private bool _lossPending;

        // Replaying a save: what happened is known, so the holds the view times on screen do not apply.
        private bool _replaying;

        public Level Level { get; }
        public Board Board { get; }
        public BusQueue Buses { get; }
        public WaitingArea Waiting { get; }
        public SessionState State { get; private set; } = SessionState.Ready;

        /// <summary>The passengers waiting just off the board for a waiting seat, in the order they came.</summary>
        public IReadOnlyList<Passenger> Queue => _queue;

        /// <summary>Why the level was lost. Only meaningful when <see cref="State"/> is Lost.</summary>
        public LoseReason LoseReason { get; private set; }

        public float RemainingSeconds { get; private set; }

        /// <summary>The cell of every tap that moved a passenger, in order, with <see cref="BusArrivedMark"/>
        /// where a bus pulled in. This is what gets saved: replaying it on the same level rebuilds exactly
        /// this state (see <see cref="TryResume"/>).</summary>
        public IReadOnlyList<GridPos> Moves => _moves;

        /// <summary>In <see cref="Moves"/>: a bus pulled in here (not a cell).</summary>
        public static readonly GridPos BusArrivedMark = new GridPos(-1, -1);

        /// <summary>A passenger left the board: it walks the path (from its cell to the exit, both
        /// included), then goes to the seat: on a bus, in the waiting area, or in the line off the board.</summary>
        public event Action<Passenger, IReadOnlyList<GridPos>, Seat> PassengerLeftBoard;

        /// <summary>A waiting passenger boards the bus that just pulled in: the waiting seat it
        /// leaves, then its bus seat.</summary>
        public event Action<Passenger, int, Seat> PassengerLeftWaiting;

        /// <summary>A passenger in the line off the board got a place: a bus seat or a waiting seat.</summary>
        public event Action<Passenger, Seat> PassengerLeftQueue;

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

        private LevelSession(LevelSession other)
        {
            Level = other.Level;
            Board = other.Board.Clone();
            Buses = other.Buses.Clone();
            Waiting = other.Waiting.Clone();
            _moves.AddRange(other._moves);
            _queue.AddRange(other._queue);
            _ridersOnTheWay = other._ridersOnTheWay;
            _lossPending = other._lossPending;
            _replaying = other._replaying;
            State = other.State;
            LoseReason = other.LoseReason;
            RemainingSeconds = other.RemainingSeconds;
        }

        /// <summary>A separate session in exactly this state, with the same <see cref="Moves"/> so far and nobody
        /// listening to its events: what happens to the copy never reaches this one. For looking ahead
        /// (see <see cref="LevelSolver"/>). Passengers never change, so the two share them.</summary>
        internal LevelSession Clone() => new LevelSession(this);

        /// <summary>
        /// Rebuilds a level in progress from a save: replays the saved taps through the same rules,
        /// before anyone listens, so no animation plays and no separate restore code is needed.
        /// The result waits in Ready for Play, like a fresh level, and nothing is decided before Play
        /// (see <see cref="Start"/>). False when the save no longer fits the level (its file was
        /// edited since): the caller then starts the level fresh.
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
            var replay = new LevelSession(level) { _replaying = true };
            foreach (var move in moves)
            {
                if (move.Equals(BusArrivedMark))
                {
                    if (!replay.TryArrive())
                    {
                        return false;
                    }
                    continue;
                }
                // Every saved tap moved someone and the level never ended, or it would not have been saved.
                if (replay.Settle(move) != TapResult.Moved || replay.State != SessionState.Ready)
                {
                    return false;
                }
            }
            // The resumed level shows its front bus at the stop, so a bus saved on its way has arrived. If the
            // waiting passengers fill it, it leaves too and the next one pulls in, until one stays or none is left.
            replay._ridersOnTheWay = false;     // the resumed picture shows that bus gone: its riders made it
            while (replay.TryArrive())
            {
            }
            replay._replaying = false;
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
                // A resumed level may be decided already: its last bus filled up and left as it pulled in,
                // or the level was lost with every waiting seat taken (they all sit in their seats from the start).
                if (Buses.AllGone)
                {
                    State = SessionState.Won;
                }
                WaitingSeatsFilled();
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
            return State == SessionState.Playing ? Settle(cell) : TapResult.Ignored;
        }

        /// <summary>The view reports that every passenger in a waiting seat has crossed the line in front of the
        /// seats. Lost when a tap took the last seat while a bus was at the stop, unless that bus was filled and
        /// its riders crossed first. Ignored when the level is over.</summary>
        public void WaitingSeatsFilled()
        {
            if (State == SessionState.Playing && _lossPending && Waiting.IsFull && (Buses.IsAtStop || _ridersOnTheWay))
            {
                Lose(LoseReason.NoSeats);
            }
        }

        /// <summary>The view reports that every rider of the bus that was sent away has crossed the line: they
        /// made it, and the next bus decides from now on.</summary>
        public void BusBoarded()
        {
            _ridersOnTheWay = false;
            _lossPending = false;
        }

        /// <summary>The view reports that the next bus has pulled in: it takes the waiting passengers of its
        /// colour (and leaves at once if they fill it). Ignored when no bus is on its way or the level is over.</summary>
        public void BusArrived()
        {
            if (State == SessionState.Playing)
            {
                TryArrive();
            }
        }

        private bool TryArrive()
        {
            if (Buses.AllGone || Buses.IsAtStop)
            {
                return false;
            }
            Buses.Arrive();
            _moves.Add(BusArrivedMark);
            _ridersOnTheWay = false;
            _lossPending = false;
            bool tookSomeone = BoardWaitingPassengers();
            tookSomeone |= MoveTheQueueUp();
            if (!tookSomeone && Waiting.IsFull)
            {
                if (State == SessionState.Playing)
                {
                    Lose(LoseReason.NoSeats);
                }
                else
                {
                    _lossPending = true;        // a resumed level loses on Play instead (Start)
                }
            }
            SendFullBusesAway();
            return true;
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
            bool toBus = Buses.CanBoard(passenger.Color);
            if (!toBus && Waiting.IsFull)
            {
                // Nowhere to go. While the bus is at the stop or its riders are on their way, it sets off and
                // waits off the board; while buses change, the tap is held back until the next bus is in.
                if (!(Buses.IsAtStop || _ridersOnTheWay) || _queue.Count >= QueueLength)
                {
                    return TapResult.Ignored;
                }
                Board.Remove(cell);
                _moves.Add(cell);
                _queue.Add(passenger);
                PassengerLeftBoard?.Invoke(passenger, path, Seat.InQueue(_queue.Count - 1));
                return TapResult.Moved;
            }
            if (!toBus && _ridersOnTheWay && !_replaying && Waiting.Occupied == Waiting.SeatCount - 1)
            {
                return TapResult.Ignored;       // the last seat waits until the riders on their way are in
            }

            Board.Remove(cell);
            _moves.Add(cell);
            if (toBus)
            {
                var seat = Buses.TakeSeat();
                PassengerLeftBoard?.Invoke(passenger, path, seat);
                SendFullBusesAway();
            }
            else
            {
                int waitingSeat = Waiting.Sit(passenger);
                if (Waiting.IsFull && Buses.IsAtStop)
                {
                    _lossPending = true;
                }
                PassengerLeftBoard?.Invoke(passenger, path, Seat.Waiting(waitingSeat));
            }
            return TapResult.Moved;
        }

        // A full bus at the stop leaves; the stop stays empty until the next one has pulled in.
        private void SendFullBusesAway()
        {
            if (!Buses.IsAtStop || !Buses.IsCurrentFull)
            {
                return;
            }
            int leaving = Buses.CurrentIndex;
            Buses.SendFrontAway();
            _ridersOnTheWay = true;             // before the event: the view may report at once that they are in
            BusDeparted?.Invoke(leaving);

            // Passengers and bus seats match colour for colour (the level was checked when it
            // was created), so when the last bus leaves, nobody is left behind. (A resumed level
            // wins on Play instead, see Start.)
            if (Buses.AllGone && State == SessionState.Playing)
            {
                State = SessionState.Won;
            }
        }

        // Left to right, every waiting passenger of the new bus's colour boards while seats last.
        private bool BoardWaitingPassengers()
        {
            bool tookSomeone = false;
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
                    tookSomeone = true;
                }
            }
            return tookSomeone;
        }

        // In the order they came, the passengers waiting off the board board the new bus if it is their colour,
        // or take the seats it freed. True if any of them boarded.
        private bool MoveTheQueueUp()
        {
            bool boarded = false;
            for (int i = 0; i < _queue.Count;)
            {
                var passenger = _queue[i];
                Seat seat;
                if (Buses.CanBoard(passenger.Color))
                {
                    seat = Buses.TakeSeat();
                    boarded = true;
                }
                else if (!Waiting.IsFull)
                {
                    seat = Seat.Waiting(Waiting.Sit(passenger));
                }
                else
                {
                    i++;
                    continue;
                }
                _queue.RemoveAt(i);
                PassengerLeftQueue?.Invoke(passenger, seat);
            }
            return boarded;
        }

        private void Lose(LoseReason reason)
        {
            LoseReason = reason;
            State = SessionState.Lost;
        }
    }
}
