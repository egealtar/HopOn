using HopOn.Logic;
using UnityEngine;

namespace HopOn.World
{
    /// <summary>
    /// Where things stand in the world: the one place that turns a grid cell into a position and a
    /// tapped point back into a cell, so what is drawn and what is tapped can never disagree.
    /// Everything stands on the ground (y = 0). Row 0, the exit row, is the farthest from the
    /// camera (the top of the screen); the waiting seats are just beyond it, the bus stop beyond those.
    /// </summary>
    public static class BoardLayout
    {
        // The board prefabs (tile, obstacle) are made one unit wide, so this is not a free setting.
        private const float CellSize = 1f;

        /// <summary>The biggest board a level may have: the camera frames every board to fill the
        /// screen, and beyond this the cells get too small to tap well on a phone. The game rules work
        /// with any size; this is a limit for level design, checked on every shipped level by the tests.</summary>
        public const int MaxBoardWidth = 7;
        public const int MaxBoardHeight = 7;

        /// <summary>Taps are read at a passenger's waist height, so tapping a body hits its own cell.</summary>
        public const float TapHeight = 0.45f;

        private const float WaitingRowZ = 1.6f;
        private const float BusDoorZ = 2.35f;           // on the sidewalk, just before the curb at 2.5
        private const float BusDoorSpacing = 0.4f;
        private const float BusStopZ = 3.3f;
        private const float BusSpacing = 2.6f;
        private const float BusExitX = 9f;

        // Sizes the camera framing needs, measured on the prefabs.
        private const float HeadHeight = 1f;            // a standing passenger's head
        private const float BusHalfLength = 1.14f;      // the bus is 2.28 long and about 1 wide
        private const float BusHalfWidth = 0.5f;
        private const float BusTop = 1.4f;              // the head of a rider sitting in a bus

        /// <summary>Columns are centred on x = 0; rows run towards the camera.</summary>
        public static Vector3 CellToWorld(GridPos cell, int boardWidth)
        {
            return new Vector3((cell.Col - (boardWidth - 1) * 0.5f) * CellSize, 0f, -cell.Row * CellSize);
        }

        /// <summary>The cell under a world point, or false when the point is off the board.</summary>
        public static bool TryWorldToCell(Vector3 point, int boardWidth, int boardHeight, out GridPos cell)
        {
            int col = Mathf.RoundToInt(point.x / CellSize + (boardWidth - 1) * 0.5f);
            int row = Mathf.RoundToInt(-point.z / CellSize);
            cell = new GridPos(row, col);
            return row >= 0 && row < boardHeight && col >= 0 && col < boardWidth;
        }

        public static Vector3 WaitingSeat(int seat)
        {
            return new Vector3((seat - (Level.WaitingSeats - 1) * 0.5f) * CellSize, 0f, WaitingRowZ);
        }

        /// <summary>Where the rider for bus seat <paramref name="seat"/> waits and steps in: on the
        /// curb under its own seat (seat 0 is the front, on the right), so riders do not pile up.</summary>
        public static Vector3 BusDoor(int seat)
        {
            return new Vector3((1 - seat) * BusDoorSpacing, 0f, BusDoorZ);
        }

        /// <summary>Places in the bus line: 0 is at the stop, 1 waits just to its left, and so on.</summary>
        public static Vector3 BusSlot(int place)
        {
            return new Vector3(-place * BusSpacing, 0f, BusStopZ);
        }

        /// <summary>Where a full bus drives off to, beyond the right edge of the screen.</summary>
        public static Vector3 BusExit => new Vector3(BusExitX, 0f, BusStopZ);

        /// <summary>The points the camera must keep on screen for a board of this size: the board's
        /// four corners on the ground, the heads of the passengers at both ends of the waiting row,
        /// and the top of the bus at the stop with its riders. Only what is really there, so the
        /// camera comes as near as it can.</summary>
        public static Vector3[] FramedPoints(int boardWidth, int boardHeight)
        {
            float boardHalf = boardWidth * 0.5f * CellSize;
            float farSide = 0.5f * CellSize;                                   // row 0's far side
            float nearSide = -(boardHeight - 0.5f) * CellSize;                 // the last row's near side
            float waitingHalf = WaitingSeat(Level.WaitingSeats - 1).x + 0.5f * CellSize;
            float busBack = BusStopZ + BusHalfWidth;
            return new[]
            {
                new Vector3(-boardHalf, 0f, farSide),
                new Vector3(boardHalf, 0f, farSide),
                new Vector3(-boardHalf, 0f, nearSide),
                new Vector3(boardHalf, 0f, nearSide),
                new Vector3(-waitingHalf, HeadHeight, WaitingRowZ),
                new Vector3(waitingHalf, HeadHeight, WaitingRowZ),
                new Vector3(-BusHalfLength, BusTop, busBack),
                new Vector3(BusHalfLength, BusTop, busBack),
            };
        }
    }
}
