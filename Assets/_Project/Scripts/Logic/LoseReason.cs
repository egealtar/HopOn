namespace HopOn.Logic
{
    /// <summary>Why a level was lost. A game rule, so it lives with the logic; the UI only displays it.</summary>
    public enum LoseReason
    {
        TimeUp,
        NoSeats,
    }
}
