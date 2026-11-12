using System.Collections.Generic;

namespace HopOn.Logic
{
    public enum CellType
    {
        /// <summary>No ground: outside the board's shape (its notches and bumps).</summary>
        Void,
        /// <summary>Walkable ground; may hold a passenger at the start.</summary>
        Floor,
        /// <summary>Ground that nobody can walk through.</summary>
        Obstacle,
    }

    /// <summary>
    /// A level as the game sees it: board shape, starting passengers, bus order and time limit.
    /// Immutable and always valid: the only way to get one is <see cref="TryCreate"/>, which reads
    /// the rows of a level file and runs every check on them.
    /// Row 0 is the top row (the exit side) and the first row in the file, so a file reads like
    /// the board looks on screen.
    /// </summary>
    public sealed class Level
    {
        /// <summary>Seats on every bus. A rule of the game, the same in every level.</summary>
        public const int BusCapacity = 3;

        /// <summary>Waiting seats beside the bus stop. The scene has exactly this many.</summary>
        public const int WaitingSeats = 5;

        public const char VoidCode = ' ';
        public const char FloorCode = '.';
        public const char ObstacleCode = '#';

        private readonly CellType[] _cells;
        private readonly PassengerColor?[] _passengers;

        public string Id { get; }
        public int Width { get; }
        public int Height { get; }
        public float TimeLimitSeconds { get; }
        public IReadOnlyList<PassengerColor> Buses { get; }

        private Level(string id, int width, int height, CellType[] cells, PassengerColor?[] passengers,
                      IReadOnlyList<PassengerColor> buses, float timeLimitSeconds)
        {
            Id = id;
            Width = width;
            Height = height;
            _cells = cells;
            _passengers = passengers;
            Buses = buses;
            TimeLimitSeconds = timeLimitSeconds;
        }

        public bool InBounds(GridPos p) => p.Row >= 0 && p.Row < Height && p.Col >= 0 && p.Col < Width;

        public CellType CellAt(GridPos p) => InBounds(p) ? _cells[Index(p)] : CellType.Void;

        /// <summary>The colour of the passenger standing here when the level starts, or null.</summary>
        public PassengerColor? PassengerAt(GridPos p) => InBounds(p) ? _passengers[Index(p)] : null;

        /// <summary>Exit cells: walkable cells in the top row.</summary>
        public bool IsExit(GridPos p) => p.Row == 0 && CellAt(p) == CellType.Floor;

        public IEnumerable<GridPos> AllPositions()
        {
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    yield return new GridPos(row, col);
                }
            }
        }

        private int Index(GridPos p) => p.Row * Width + p.Col;

        /// <summary>
        /// Builds a level from a level file's text and checks it. Returns false with every problem
        /// found, so a broken level is caught in the editor or a test, never by a player:
        /// first the spelling (unknown letters), then the game rules (<see cref="LevelValidator"/>).
        /// Rows may differ in length; a short row is padded with Void ("no ground").
        /// </summary>
        /// <param name="rows">Board rows, top row first.</param>
        /// <param name="buses">Bus colours in arrival order, one letter per bus, such as "RBG".</param>
        public static bool TryCreate(string id, IReadOnlyList<string> rows, string buses, float timeLimitSeconds,
                                     out Level level, out List<string> errors)
        {
            errors = new List<string>();
            level = null;

            int height = rows?.Count ?? 0;
            int width = 0;
            for (int r = 0; r < height; r++)
            {
                int length = rows[r]?.Length ?? 0;
                if (length > width)
                {
                    width = length;
                }
            }
            if (height == 0 || width == 0)
            {
                errors.Add("The board is empty: 'cells' needs at least one row.");
                return false;
            }

            var cells = new CellType[width * height];
            var passengers = new PassengerColor?[width * height];
            for (int r = 0; r < height; r++)
            {
                string row = rows[r] ?? string.Empty;
                for (int c = 0; c < width; c++)
                {
                    char code = c < row.Length ? row[c] : VoidCode;
                    int i = r * width + c;
                    if (code == VoidCode)
                    {
                        cells[i] = CellType.Void;
                    }
                    else if (code == FloorCode)
                    {
                        cells[i] = CellType.Floor;
                    }
                    else if (code == ObstacleCode)
                    {
                        cells[i] = CellType.Obstacle;
                    }
                    else if (PassengerColorCodes.TryParse(code, out var color))
                    {
                        cells[i] = CellType.Floor;
                        passengers[i] = color;
                    }
                    else
                    {
                        errors.Add($"Unknown cell '{code}' at row {r}, column {c}. Use space, '.', '#' or R B G Y P O.");
                    }
                }
            }

            var busColors = new List<PassengerColor>();
            string busCodes = buses ?? string.Empty;
            for (int b = 0; b < busCodes.Length; b++)
            {
                if (PassengerColorCodes.TryParse(busCodes[b], out var color))
                {
                    busColors.Add(color);
                }
                else
                {
                    errors.Add($"Unknown colour '{busCodes[b]}' for bus {b + 1}. Use R B G Y P O.");
                }
            }

            if (errors.Count > 0)
            {
                return false;
            }
            var candidate = new Level(id, width, height, cells, passengers, busColors.AsReadOnly(), timeLimitSeconds);
            errors.AddRange(LevelValidator.Validate(candidate));
            if (errors.Count > 0)
            {
                return false;
            }
            level = candidate;
            return true;
        }
    }
}
