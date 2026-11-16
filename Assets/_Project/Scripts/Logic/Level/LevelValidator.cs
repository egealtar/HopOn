using System.Collections.Generic;

namespace HopOn.Logic
{
    /// <summary>
    /// The checks that <see cref="Level.TryCreate"/> runs on every level: they refuse a level that is broken by
    /// construction (no time, no bus, colour counts that do not match the bus seats, no exit, a passenger walled
    /// in for good). Whether a level can be finished is <see cref="LevelSolver"/>'s question, asked by the level
    /// editor and the tests. Returns every problem at once, not just the first.
    /// </summary>
    internal static class LevelValidator
    {
        public static List<string> Validate(Level level)
        {
            var errors = new List<string>();
            if (!(level.TimeLimitSeconds > 0f) || float.IsInfinity(level.TimeLimitSeconds))     // NaN fails every comparison
            {
                errors.Add("The time limit must be more than 0 seconds.");
            }
            if (level.Buses.Count == 0)
            {
                errors.Add("The level has no buses.");
            }
            CheckColourCounts(level, errors);
            CheckEveryoneCanLeave(level, errors);
            return errors;
        }

        // Every passenger needs a seat on a bus of its colour, and every bus must fill up,
        // otherwise the level can never be finished.
        private static void CheckColourCounts(Level level, List<string> errors)
        {
            var passengers = new Dictionary<PassengerColor, int>();
            foreach (var p in level.AllPositions())
            {
                var color = level.PassengerAt(p);
                if (color.HasValue)
                {
                    passengers.TryGetValue(color.Value, out int count);
                    passengers[color.Value] = count + 1;
                }
            }
            if (passengers.Count == 0)
            {
                errors.Add("The level has no passengers.");
            }

            var buses = new Dictionary<PassengerColor, int>();
            foreach (var color in level.Buses)
            {
                buses.TryGetValue(color, out int count);
                buses[color] = count + 1;
            }

            var colours = new HashSet<PassengerColor>(passengers.Keys);
            colours.UnionWith(buses.Keys);
            foreach (var color in colours)
            {
                passengers.TryGetValue(color, out int have);
                buses.TryGetValue(color, out int busCount);
                int seats = busCount * Level.BusCapacity;
                if (have != seats)
                {
                    errors.Add($"{color}: {have} passengers but {seats} bus seats ({busCount} buses x {Level.BusCapacity}).");
                }
            }
        }

        // Plays the board with the game's own path search: whoever has a free way out leaves,
        // which can open the way for others, until nobody else can move. Anyone still standing
        // is walled in and could never leave.
        private static void CheckEveryoneCanLeave(Level level, List<string> errors)
        {
            bool hasExit = false;
            for (int col = 0; col < level.Width; col++)
            {
                if (level.IsExit(new GridPos(0, col)))
                {
                    hasExit = true;
                }
            }
            if (!hasExit)
            {
                errors.Add("No exit: the top row needs at least one walkable cell.");
                return;
            }

            var board = new Board(level);
            bool someoneLeft = true;
            while (someoneLeft)
            {
                someoneLeft = false;
                foreach (var p in level.AllPositions())
                {
                    if (board.PassengerAt(p) != null && board.TryFindPathToExit(p, out _))
                    {
                        board.Remove(p);
                        someoneLeft = true;
                    }
                }
            }

            foreach (var p in level.AllPositions())
            {
                if (board.PassengerAt(p) != null)
                {
                    errors.Add($"The passenger at {p} is walled in and can never reach an exit.");
                }
            }
        }
    }
}
