using HopOn.Logic;
using NUnit.Framework;

namespace HopOn.Tests
{
    /// <summary>Builds small levels from text rows, the same way level files are read, so every
    /// test level is one the game would accept.</summary>
    internal static class TestLevels
    {
        public static Level Make(string buses, params string[] rows)
        {
            return MakeTimed(60f, buses, rows);
        }

        public static Level MakeTimed(float seconds, string buses, params string[] rows)
        {
            bool ok = Level.TryCreate("test", rows, buses, seconds, out var level, out var errors);
            Assert.IsTrue(ok, "Test level is not valid: " + string.Join(" ", errors));
            return level;
        }

        /// <summary>A session that is already playing (Play was pressed).</summary>
        public static LevelSession Playing(string buses, params string[] rows)
        {
            var session = new LevelSession(Make(buses, rows));
            session.Start();
            return session;
        }
    }
}
