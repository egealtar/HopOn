namespace HopOn.Logic
{
    /// <summary>The six passenger and bus colours.</summary>
    public enum PassengerColor
    {
        Red,
        Blue,
        Green,
        Yellow,
        Purple,
        Orange,
    }

    /// <summary>The one-letter codes used in level files: R B G Y P O.</summary>
    public static class PassengerColorCodes
    {
        public static bool TryParse(char code, out PassengerColor color)
        {
            switch (char.ToUpperInvariant(code))
            {
                case 'R': color = PassengerColor.Red; return true;
                case 'B': color = PassengerColor.Blue; return true;
                case 'G': color = PassengerColor.Green; return true;
                case 'Y': color = PassengerColor.Yellow; return true;
                case 'P': color = PassengerColor.Purple; return true;
                case 'O': color = PassengerColor.Orange; return true;
                default: color = default; return false;
            }
        }
    }
}
