using System;

namespace HopOn.Logic
{
    /// <summary>
    /// The countdown of one level. Pure C# with no Unity types: whoever owns it advances it with
    /// Tick, so it can be paused, saved, restored and unit-tested without a running game.
    /// </summary>
    public sealed class LevelTimer
    {
        public float Duration { get; }
        public float Remaining { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsExpired => Remaining <= 0f;

        /// <summary>Whole seconds shown to the player, rounded up, so 0:00 appears only at the very end.</summary>
        public int DisplaySeconds => (int)Math.Ceiling(Remaining);

        /// <summary>Raised once, the moment the time runs out.</summary>
        public event Action Expired;

        /// <param name="durationSeconds">The level's full time.</param>
        /// <param name="remainingSeconds">Time left when resuming a saved level; negative means a fresh start.</param>
        public LevelTimer(float durationSeconds, float remainingSeconds = -1f)
        {
            Duration = Math.Max(0f, durationSeconds);
            Remaining = remainingSeconds < 0f ? Duration : Math.Min(remainingSeconds, Duration);
        }

        public void Start()
        {
            if (!IsExpired)
            {
                IsRunning = true;
            }
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Tick(float deltaSeconds)
        {
            if (!IsRunning || deltaSeconds <= 0f)
            {
                return;
            }
            Remaining = Math.Max(0f, Remaining - deltaSeconds);
            if (Remaining <= 0f)
            {
                IsRunning = false;
                Expired?.Invoke();
            }
        }
    }
}
