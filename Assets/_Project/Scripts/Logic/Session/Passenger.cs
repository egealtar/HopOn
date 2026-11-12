namespace HopOn.Logic
{
    /// <summary>
    /// One passenger. The id stays the same for the whole level: passengers are numbered in
    /// reading order (left to right, top to bottom) when the level starts, so the view can keep
    /// one character per id.
    /// </summary>
    public sealed class Passenger
    {
        public int Id { get; }
        public PassengerColor Color { get; }

        public Passenger(int id, PassengerColor color)
        {
            Id = id;
            Color = color;
        }

        public override string ToString() => $"#{Id} {Color}";
    }
}
