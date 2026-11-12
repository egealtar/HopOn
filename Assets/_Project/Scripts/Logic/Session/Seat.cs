namespace HopOn.Logic
{
    /// <summary>Where a passenger sits after leaving the board: a seat on a bus, or a waiting seat.</summary>
    public readonly struct Seat
    {
        public readonly bool OnBus;

        /// <summary>The bus's place in the level's bus order. Bus seats only; -1 for a waiting seat.</summary>
        public readonly int BusIndex;

        /// <summary>The seat on that bus (0 to capacity - 1), or the waiting seat (0 to slots - 1).</summary>
        public readonly int Index;

        private Seat(bool onBus, int busIndex, int index)
        {
            OnBus = onBus;
            BusIndex = busIndex;
            Index = index;
        }

        public static Seat Bus(int busIndex, int seat) => new Seat(true, busIndex, seat);
        public static Seat Waiting(int slot) => new Seat(false, -1, slot);

        public override string ToString() => OnBus ? $"bus {BusIndex} seat {Index}" : $"waiting seat {Index}";
    }
}
