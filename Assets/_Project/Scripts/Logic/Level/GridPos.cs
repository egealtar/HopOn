using System;

namespace HopOn.Logic
{
    /// <summary>
    /// A cell on the board. Row 0 is the top row, the side the passengers exit from; column 0 is
    /// the left. The same order as the rows are written in a level file, so the file reads like
    /// the board looks on screen.
    /// </summary>
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int Row;
        public readonly int Col;

        public GridPos(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public GridPos Up => new GridPos(Row - 1, Col);
        public GridPos Down => new GridPos(Row + 1, Col);
        public GridPos Left => new GridPos(Row, Col - 1);
        public GridPos Right => new GridPos(Row, Col + 1);

        public bool Equals(GridPos other) => Row == other.Row && Col == other.Col;
        public override bool Equals(object obj) => obj is GridPos other && Equals(other);
        public override int GetHashCode() => (Row * 397) ^ Col;
        public static bool operator ==(GridPos a, GridPos b) => a.Equals(b);
        public static bool operator !=(GridPos a, GridPos b) => !a.Equals(b);
        public override string ToString() => $"(row {Row}, col {Col})";
    }
}
