using UnityEngine;

namespace HopOn.UI
{
    /// <summary>How time is written on screen. Presentation, so it lives with the UI, not the logic.</summary>
    public static class TimeFormat
    {
        /// <summary>Whole seconds as the player sees them, rounded up: 0.2 s left still shows 0:01,
        /// so 0:00 appears only when the time is really up.</summary>
        public static int WholeSeconds(float seconds) => Mathf.CeilToInt(Mathf.Max(0f, seconds));

        /// <summary>90 -> "1:30".</summary>
        public static string Clock(float seconds)
        {
            int whole = WholeSeconds(seconds);
            return $"{whole / 60}:{whole % 60:00}";
        }
    }
}
