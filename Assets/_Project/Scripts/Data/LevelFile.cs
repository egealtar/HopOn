using System;

namespace HopOn.Data
{
    /// <summary>
    /// The JSON shape of a level file, field for field. It only carries data: the game turns it
    /// into a checked Level with <see cref="CatalogLevelProvider.TryParse"/>. A field missing from
    /// the file keeps the value written here.
    /// </summary>
    /// <example>
    /// { "timeLimitSeconds": 60, "buses": "RBG", "cells": [".R.B.", "GRBG.", ".RBG.", "....."] }
    /// </example>
    [Serializable]
    public sealed class LevelFile
    {
        public float timeLimitSeconds = 90f;

        /// <summary>Bus colours in arrival order, one letter per bus: R B G Y P O.</summary>
        public string buses = "";

        /// <summary>Board rows, top row (the exit side) first. ' ' no ground, '.' floor,
        /// '#' obstacle, a colour letter: a passenger standing on floor.</summary>
        public string[] cells = Array.Empty<string>();
    }
}
