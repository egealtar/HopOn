using System.Collections.Generic;

namespace HopOn.Logic
{
    /// <summary>
    /// Who stands where during play. Starts as a copy of the level's passengers. A passenger is
    /// removed the moment it is tapped, so the next tap can already walk through its cell.
    /// Only the logic changes the board (Remove is internal); views can only read it.
    /// </summary>
    public sealed class Board
    {
        private readonly Level _level;
        private readonly Passenger[] _cells;    // null: nobody stands there

        public int Width => _level.Width;
        public int Height => _level.Height;

        internal Board(Level level)
        {
            _level = level;
            _cells = new Passenger[level.Width * level.Height];
            int nextId = 0;
            foreach (var p in level.AllPositions())
            {
                var color = level.PassengerAt(p);
                if (color.HasValue)
                {
                    _cells[Index(p)] = new Passenger(nextId++, color.Value);
                }
            }
        }

        private Board(Level level, Passenger[] cells)
        {
            _level = level;
            _cells = cells;
        }

        /// <summary>A separate board with the same passengers on it, for looking ahead (see <see cref="LevelSolver"/>).</summary>
        internal Board Clone() => new Board(_level, (Passenger[])_cells.Clone());

        public CellType CellAt(GridPos p) => _level.CellAt(p);
        public bool IsExit(GridPos p) => _level.IsExit(p);

        /// <summary>The passenger standing here, or null.</summary>
        public Passenger PassengerAt(GridPos p) => _level.InBounds(p) ? _cells[Index(p)] : null;

        /// <summary>
        /// The shortest walk from <paramref name="from"/> to any exit over empty ground, found by
        /// breadth-first search: cells are reached in order of distance, so the first exit reached
        /// is the nearest. The path includes both ends; a passenger already on an exit gets just
        /// its own cell. The view walks the character along this same path, so the route is never
        /// worked out twice. False when every way out is blocked.
        /// </summary>
        public bool TryFindPathToExit(GridPos from, out List<GridPos> path)
        {
            path = null;
            if (CellAt(from) != CellType.Floor)
            {
                return false;
            }

            // For every cell reached so far: the cell we stepped from to get there.
            var cameFrom = new Dictionary<GridPos, GridPos> { [from] = from };
            var queue = new Queue<GridPos>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                if (IsExit(cell))
                {
                    path = TracePath(from, cell, cameFrom);
                    return true;
                }
                // Up first: when two ways out are equally short, heading up looks the most natural.
                foreach (var next in new[] { cell.Up, cell.Left, cell.Right, cell.Down })
                {
                    if (IsEmptyFloor(next) && !cameFrom.ContainsKey(next))
                    {
                        cameFrom[next] = cell;
                        queue.Enqueue(next);
                    }
                }
            }
            return false;
        }

        internal void Remove(GridPos p)
        {
            _cells[Index(p)] = null;
        }

        // CellAt returns Void outside the board, so _cells is only read for cells inside it.
        private bool IsEmptyFloor(GridPos p) => CellAt(p) == CellType.Floor && _cells[Index(p)] == null;

        // Walks back from the exit to the start, then reverses: start first, exit last.
        private static List<GridPos> TracePath(GridPos from, GridPos exit, Dictionary<GridPos, GridPos> cameFrom)
        {
            var path = new List<GridPos>();
            for (var cell = exit; cell != from; cell = cameFrom[cell])
            {
                path.Add(cell);
            }
            path.Add(from);
            path.Reverse();
            return path;
        }

        private int Index(GridPos p) => p.Row * Width + p.Col;
    }
}
