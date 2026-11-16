namespace HopOn.Logic
{
    /// <summary>Where a passenger goes after leaving the board: a seat on a bus, a waiting seat, or the line just
    /// off the board, where it waits for a waiting seat to free up.</summary>
    public readonly struct Seat
    {
        public readonly bool OnBus;

        /// <summary>No seat yet: it waits in the line off the board (see <see cref="LevelSession.Queue"/>).
        /// Index is its place in that line.</summary>
        public readonly bool Queued;

        /// <summary>The bus's place in the level's bus order. Bus seats only; -1 otherwise.</summary>
        public readonly int BusIndex;

        /// <summary>The seat on that bus (0 to capacity - 1), the waiting seat (0 to slots - 1), or the place in the line.</summary>
        public readonly int Index;

        private Seat(bool onBus, int busIndex, int index, bool queued = false)
        {
            OnBus = onBus;
            BusIndex = busIndex;
            Index = index;
            Queued = queued;
        }

        public static Seat Bus(int busIndex, int seat) => new Seat(true, busIndex, seat);
        public static Seat Waiting(int slot) => new Seat(false, -1, slot);
        public static Seat InQueue(int place) => new Seat(false, -1, place, true);

        public override string ToString() => OnBus ? $"bus {BusIndex} seat {Index}" : Queued ? $"in the line ({Index})" : $"waiting seat {Index}";
    }
}
