using System;

namespace HopOn.Logic
{
    /// <summary>
    /// The waiting seats beside the bus stop. A passenger whose bus is not at the stop takes the
    /// first free seat from the left and stays in that seat (nobody slides over) until a bus of
    /// its colour arrives. Only the logic changes it; views can only read it.
    /// </summary>
    public sealed class WaitingArea
    {
        private readonly Passenger[] _seats = new Passenger[Level.WaitingSeats];   // null: free seat

        public int SeatCount => _seats.Length;
        public int Occupied { get; private set; }
        public bool IsFull => Occupied == _seats.Length;

        /// <summary>The passenger in this seat, or null.</summary>
        public Passenger At(int seat) => _seats[seat];

        internal WaitingArea()
        {
        }

        internal WaitingArea Clone()
        {
            var copy = new WaitingArea { Occupied = Occupied };
            Array.Copy(_seats, copy._seats, _seats.Length);
            return copy;
        }

        /// <summary>Seats the passenger in the first free seat and returns that seat.</summary>
        internal int Sit(Passenger passenger)
        {
            for (int seat = 0; seat < _seats.Length; seat++)
            {
                if (_seats[seat] == null)
                {
                    _seats[seat] = passenger;
                    Occupied++;
                    return seat;
                }
            }
            // The session checks IsFull before seating anyone, so this is a bug, not a game state.
            throw new InvalidOperationException("No free waiting seat.");
        }

        internal void StandUp(int seat)
        {
            _seats[seat] = null;
            Occupied--;
        }
    }
}
