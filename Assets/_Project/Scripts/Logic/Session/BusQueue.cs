using System.Collections.Generic;

namespace HopOn.Logic
{
    /// <summary>
    /// The buses in the order they arrive. Only the front bus takes passengers; when it is full
    /// it leaves and the next one pulls in. A list and an index rather than a Queue, because the
    /// view also shows the buses that are still to come.
    /// Only the logic changes it; views can only read it.
    /// </summary>
    public sealed class BusQueue
    {
        private readonly IReadOnlyList<PassengerColor> _colors;

        public int Count => _colors.Count;

        /// <summary>The front bus's place in the bus order. Equals <see cref="Count"/> once every bus has left.</summary>
        public int CurrentIndex { get; private set; }

        /// <summary>Seats already taken on the front bus.</summary>
        public int SeatsTaken { get; private set; }

        public bool AllGone => CurrentIndex >= Count;
        public bool IsCurrentFull => SeatsTaken >= Level.BusCapacity;

        public PassengerColor ColorAt(int busIndex) => _colors[busIndex];

        internal BusQueue(IReadOnlyList<PassengerColor> colors)
        {
            _colors = colors;
        }

        /// <summary>True when a bus is at the stop, it is this colour, and it has a free seat.</summary>
        public bool CanBoard(PassengerColor color) => !AllGone && _colors[CurrentIndex] == color && !IsCurrentFull;

        internal Seat TakeSeat() => Seat.Bus(CurrentIndex, SeatsTaken++);

        internal void SendFrontAway()
        {
            CurrentIndex++;
            SeatsTaken = 0;
        }
    }
}
