namespace HopOn.UI
{
    /// <summary>How time is written on screen. Presentation, so it lives with the UI, not the timer.</summary>
    public static class TimeFormat
    {
        /// <summary>90 -> "1:30".</summary>
        public static string Clock(int seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            return $"{seconds / 60}:{seconds % 60:00}";
        }
    }
}
