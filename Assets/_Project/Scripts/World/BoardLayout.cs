using HopOn.Logic;
using UnityEngine;

namespace HopOn.World
{
    /// <summary>
    /// Where things stand in the world: the one place that turns a grid cell into a position and a
    /// tapped point back into a cell, so what is drawn and what is tapped can never disagree.
    /// Everything stands on the ground (y = 0). Row 0, the exit row, is the farthest from the
    /// camera (the top of the screen); the waiting seats are just beyond it, the bus stop beyond those.
    /// The camera is the same on every level and so is the park. Everything below the park (the road and
    /// its buses, the curb, the floor, the waiting seats, the board, the stone ground) is laid out at scale 1
    /// and then scaled about the park's lower edge by <see cref="BoardScale"/>: a small board is drawn
    /// bigger and fills the screen, a big one smaller, and the people, the buses and the cells keep Bus
    /// Jam's proportions on every level (the Environment prefab's BelowPark object gets the same scale).
    /// </summary>
    public static class BoardLayout
    {
        // The board prefabs (tile, waiting spot, obstacle, rim) are sized for this cell size, so it is
        // not a free setting. Below the passengers' and buses' scale on purpose: the people fill
        // their cells and stand close together, as in Bus Jam.
        private const float CellSize = 0.58f;

        // The layout at scale 1, from the park down: the road (2.68 to 1.08) with the bus stop in its middle,
        // the curb (1.08 to 0.90, its shade included), a 0.08 gap, the waiting seats (0.47), one cell of floor,
        // then the board, its far side at 0.90 - 1.07.
        private const float ParkEdgeZ = 2.68f;          // where everything below the park is scaled from
        private const float BusStopZ = 1.88f;
        private const float BusDoorZ = 0.92f;           // just before the curb, above the waiting seats
        private const float BusDoorSpacing = 0.5f;
        private const float BusSpacing = 3.2f;
        private const float BusExitX = 9f;
        private const float WaitingRowZ = 0.645f;
        private const float BoardTopZ = 0.90f - (0.255f + 0.235f + CellSize);

        // A level is scaled to fill the screen, never more than MaxBoardScale (beyond it the bus, the people
        // and the cells of a small board look oversized): FitColumns cells at scale 1 across, FitDepth world
        // units below the park (the fixed camera shows that much down to its bottom margin).
        private const float FitColumns = 9f;
        private const float FitDepth = 8.5f;
        private const float MaxBoardScale = 1.45f;

        // The fixed camera's frame, at scale 1: a board area FramedWidth wide and FramedRows deep.
        private const float FramedWidth = 5.37f;
        private const float FramedTopZ = 0.29f;
        private const int FramedRows = 6;

        // Sizes the camera framing needs, measured on the prefabs.
        private const float HeadHeight = 1f;            // a standing passenger's head
        private const float BusHalfLength = 1.42f;      // the bus is 2.84 long and about 1.2 wide
        private const float BusHalfWidth = 0.6f;
        private const float BusTop = 1.57f;             // the head of a rider sitting in a bus

        /// <summary>The biggest board a level may have: the camera frames every board to fill the
        /// screen, and beyond this the cells get too small to tap well on a phone. The game rules work
        /// with any size; this is a limit for level design, checked on every shipped level by the tests.</summary>
        public const int MaxBoardWidth = 7;
        public const int MaxBoardHeight = 7;

        /// <summary>The current level's scale for everything below the park: board pieces, waiting seats,
        /// people and buses take it as their scale. Set by <see cref="FitBoard"/> when a level is shown.</summary>
        public static float BoardScale { get; private set; } = 1f;

        /// <summary>Taps are read at a passenger's waist height, so tapping a body hits its own cell.</summary>
        public static float TapHeight => 0.45f * BoardScale;

        /// <summary>Scales everything below the park for a level of this size: as big as fills the screen.</summary>
        public static void FitBoard(int boardWidth, int boardHeight)
        {
            float byWidth = FitColumns / boardWidth;
            float byDepth = FitDepth / (ParkEdgeZ - BoardTopZ + boardHeight * CellSize);
            BoardScale = Mathf.Min(MaxBoardScale, Mathf.Min(byWidth, byDepth));
        }

        /// <summary>Columns are centred on x = 0; rows run towards the camera from the board's far side.</summary>
        public static Vector3 CellToWorld(GridPos cell, int boardWidth)
        {
            return Scaled((cell.Col - (boardWidth - 1) * 0.5f) * CellSize, BoardTopZ - (cell.Row + 0.5f) * CellSize);
        }

        /// <summary>The cell under a world point, or false when the point is off the board.</summary>
        public static bool TryWorldToCell(Vector3 point, int boardWidth, int boardHeight, out GridPos cell)
        {
            float x = point.x / BoardScale;
            float z = ParkEdgeZ + (point.z - ParkEdgeZ) / BoardScale;
            int col = Mathf.RoundToInt(x / CellSize + (boardWidth - 1) * 0.5f);
            int row = Mathf.RoundToInt((BoardTopZ - z) / CellSize - 0.5f);
            cell = new GridPos(row, col);
            return row >= 0 && row < boardHeight && col >= 0 && col < boardWidth;
        }

        public static Vector3 WaitingSeat(int seat)
        {
            return Scaled((seat - (Level.WaitingSeats - 1) * 0.5f) * CellSize, WaitingRowZ);
        }

        /// <summary>Where a passenger waits for its waiting seat while the one before is still in it: in
        /// front of the seat, halfway across the floor between the waiting seats and the board.</summary>
        public static Vector3 InFrontOfWaitingSeat(int seat)
        {
            return Scaled((seat - (Level.WaitingSeats - 1) * 0.5f) * CellSize, WaitingRowZ - 0.235f - 0.5f * CellSize);
        }

        /// <summary>The line on the floor in front of the waiting seats, a little past where a passenger waits for
        /// its seat: a rider for the bus and a passenger for a waiting seat count as there when they cross it, so a
        /// race between them is judged at one place (see LevelSession).</summary>
        public static float LineZ => Scaled(0f, WaitingRowZ - 0.235f - 0.5f * CellSize + 0.03f).z;

        /// <summary>Where a passenger with nowhere to go waits for a waiting seat: just off the board, straight above
        /// the cell it left the board from, a little further back if someone already waits there.</summary>
        public static Vector3 QueueSpot(GridPos exitCell, int boardWidth, int placeInColumn)
        {
            float x = (exitCell.Col - (boardWidth - 1) * 0.5f) * CellSize;
            return Scaled(x, BoardTopZ + 0.15f - placeInColumn * 0.14f);
        }

        /// <summary>Where the rider for bus seat <paramref name="seat"/> waits and steps in: on the
        /// curb under its own seat (seat 0 is the front, on the right), so riders do not pile up.</summary>
        public static Vector3 BusDoor(int seat)
        {
            return Scaled((1 - seat) * BusDoorSpacing, BusDoorZ);
        }

        /// <summary>Places in the bus line: 0 is at the stop, 1 waits just to its left, and so on.</summary>
        public static Vector3 BusSlot(int place)
        {
            return Scaled(-place * BusSpacing, BusStopZ);
        }

        /// <summary>Where a full bus drives off to, beyond the right edge of the screen.</summary>
        public static Vector3 BusExit => Scaled(BusExitX, BusStopZ);

        /// <summary>The points the camera keeps on screen, the same on every level (everything below the park
        /// scales, the camera does not move), all at scale 1: a board area FramedWidth wide and FramedRows
        /// deep, the heads of the passengers at both ends of the waiting row, and the top of the bus at the
        /// stop with its riders.</summary>
        public static Vector3[] FramedPoints()
        {
            const float boardHalf = FramedWidth * 0.5f;
            const float nearSide = FramedTopZ - FramedRows * CellSize;
            const float waitingHalf = Level.WaitingSeats * 0.5f * CellSize;
            const float busBack = BusStopZ + BusHalfWidth;
            return new[]
            {
                new Vector3(-boardHalf, 0f, FramedTopZ),
                new Vector3(boardHalf, 0f, FramedTopZ),
                new Vector3(-boardHalf, 0f, nearSide),
                new Vector3(boardHalf, 0f, nearSide),
                new Vector3(-waitingHalf, HeadHeight, WaitingRowZ),
                new Vector3(waitingHalf, HeadHeight, WaitingRowZ),
                new Vector3(-BusHalfLength, BusTop, busBack),
                new Vector3(BusHalfLength, BusTop, busBack),
            };
        }

        // A point of the scale-1 layout on the ground, scaled about the park's lower edge.
        private static Vector3 Scaled(float x, float z)
        {
            return new Vector3(x * BoardScale, 0f, ParkEdgeZ + (z - ParkEdgeZ) * BoardScale);
        }
    }
}
