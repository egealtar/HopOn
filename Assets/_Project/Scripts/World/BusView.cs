using UnityEngine;

namespace HopOn.World
{
    /// <summary>
    /// One bus on screen. It keeps what the bus line in <see cref="LevelView"/> needs to play the
    /// logic back in order (is it at the stop, has the logic sent it away, how many riders are in),
    /// plus its look: the parts painted in the bus's colour and the three seats riders sit in.
    /// </summary>
    public sealed class BusView : MonoBehaviour
    {
        [SerializeField] private Renderer[] paintedParts;
        [SerializeField] private Transform[] seats;

        /// <summary>At the stop, doors open: its riders may get in.</summary>
        public bool IsAtStop { get; set; }

        /// <summary>The logic says this bus is full and leaves (it decided that the moment it happened).</summary>
        public bool DepartOrdered { get; set; }

        /// <summary>Riders who have actually sat down on screen. The bus drives off only when all are in.</summary>
        public int Seated { get; set; }

        /// <summary>Riders who have crossed the line in front of the waiting seats on screen (or came from a
        /// waiting seat, beyond it already): once all of a bus the logic sent away have, they made it.</summary>
        public int Arrived { get; set; }

        public void Paint(Material color)
        {
            foreach (Renderer part in paintedParts)
            {
                part.sharedMaterial = color;
            }
        }

        /// <summary>Where the rider in bus seat <paramref name="index"/> sits (the logic's seat number).</summary>
        public Transform Seat(int index) => seats[index];
    }
}
