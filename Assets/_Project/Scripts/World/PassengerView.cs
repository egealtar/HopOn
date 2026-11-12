using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using HopOn.Logic;
using UnityEngine;

namespace HopOn.World
{
    /// <summary>
    /// One passenger on screen. The logic has already decided where it goes; this class only gets
    /// it there, one step after another, and waits where the picture must wait (its bus is not at
    /// the stop yet). Each job runs as a coroutine: a method that can pause with "yield return"
    /// and carry on in a later frame.
    /// Two kinds of motion: DOTween moves the whole passenger along its route, and the Animator on
    /// the Visual child moves the arms and legs (Idle, Walk, Sit) according to two switches.
    /// </summary>
    public sealed class PassengerView : MonoBehaviour
    {
        private const float JumpHeight = 0.6f;
        private const float TurnSeconds = 0.15f;

        private static readonly int WalkingParameter = Animator.StringToHash("Walking");
        private static readonly int SittingParameter = Animator.StringToHash("Sitting");

        [SerializeField] private Animator animator;
        [SerializeField] private Renderer[] bodyParts;

        [Header("Motion")]
        [Tooltip("Board cells per second. If you change it a lot, change the Walk state's Speed in the Animator too, so the legs keep pace.")]
        [SerializeField] private float walkSpeed = 8f;
        [SerializeField] private float jumpSeconds = 0.25f;

        private LevelView _level;
        private bool _inWaitingSeat;                    // standing in a waiting seat right now, on screen

        public void Setup(LevelView level, Material color)
        {
            _level = level;
            foreach (Renderer part in bodyParts)
            {
                part.sharedMaterial = color;
            }
        }

        /// <summary>Starts out in a waiting seat (a resumed level).</summary>
        public void StartInWaitingSeat()
        {
            _inWaitingSeat = true;
            _level.EnteredWaitingSeat();
        }

        /// <summary>Starts out already sitting in a bus (a resumed level).</summary>
        public void StartInBus(BusView bus, int seat)
        {
            SitIn(bus, seat);
        }

        /// <summary>The logic let this passenger leave the board: walk the path, then go to the seat.</summary>
        public void LeaveBoard(IReadOnlyList<GridPos> path, int boardWidth, Seat seat)
        {
            StartCoroutine(LeaveBoardRoutine(path, boardWidth, seat));
        }

        /// <summary>The logic put this waiting passenger on the bus that just pulled in.</summary>
        public void LeaveWaitingSeat(Seat busSeat)
        {
            StartCoroutine(LeaveWaitingSeatRoutine(busSeat));
        }

        /// <summary>No way out: a short shake, and it stays where it is.</summary>
        public void Shake()
        {
            transform.DOComplete();
            _level.Track(transform.DOPunchPosition(new Vector3(0.2f, 0f, 0f), 0.35f, 7));
        }

        /// <summary>The level is over: arms and legs stop, and no job goes on to its next step
        /// (DOTween itself is paused by the level view).</summary>
        public void Freeze()
        {
            StopAllCoroutines();
            animator.speed = 0f;
        }

        private IEnumerator LeaveBoardRoutine(IReadOnlyList<GridPos> path, int boardWidth, Seat seat)
        {
            _level.BeginWork();
            // One walk, so it does not stop at the edge of the board: path[0] is the cell it stands
            // on, so the walk starts from the next cell and ends at its spot by the bus or its waiting seat.
            var points = new Vector3[path.Count];
            for (int i = 1; i < path.Count; i++)
            {
                points[i - 1] = BoardLayout.CellToWorld(path[i], boardWidth);
            }
            points[path.Count - 1] = seat.OnBus ? BoardLayout.BusDoor(seat.Index) : BoardLayout.WaitingSeat(seat.Index);
            yield return Walk(points);

            if (seat.OnBus)
            {
                yield return GetOnBus(seat);
            }
            else
            {
                // Seated only once it faces the bus stop, so a freeze never catches it mid-turn.
                yield return _level.Track(transform.DORotate(Vector3.zero, TurnSeconds)).WaitForCompletion();
                _inWaitingSeat = true;
                _level.EnteredWaitingSeat();
            }
            _level.EndWork();
        }

        private IEnumerator LeaveWaitingSeatRoutine(Seat busSeat)
        {
            _level.BeginWork();
            while (!_inWaitingSeat)
            {
                yield return null;                      // still on its way to the waiting seat
            }
            yield return GetOnBus(busSeat);
            _level.EndWork();
        }

        private IEnumerator GetOnBus(Seat seat)
        {
            BusView bus = _level.Bus(seat.BusIndex);
            while (!bus.IsAtStop)
            {
                yield return null;                      // its bus has not pulled in yet: wait at the curb, or stay seated
            }
            if (_inWaitingSeat)
            {
                // Stands up only now that its own bus is here, as in Bus Jam. Taps were locked until
                // this moment (see LevelView), so whoever gets this seat next sets off only now, and
                // walking here takes longer than standing up.
                _inWaitingSeat = false;
                _level.LeftWaitingSeat();
                yield return Walk(BoardLayout.BusDoor(seat.Index));
            }

            // Jump in, turning to the seat's direction on the way so it does not snap on landing.
            Transform busSeat = bus.Seat(seat.Index);
            _level.StartedJump();
            _level.Track(transform.DORotateQuaternion(busSeat.rotation, jumpSeconds));
            Tween jump = transform.DOJump(busSeat.position, JumpHeight, 1, jumpSeconds)
                .SetEase(Ease.Linear);                  // DOTween's default ease would make the jump float
            yield return _level.Track(jump).WaitForCompletion();
            SitIn(bus, seat.Index);
            _level.Landed();
            bus.Seated++;
        }

        // Becomes part of the bus, so it rides away with it.
        private void SitIn(BusView bus, int seat)
        {
            transform.SetParent(bus.Seat(seat), false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            animator.SetBool(SittingParameter, true);
        }

        private IEnumerator Walk(params Vector3[] points)
        {
            float distance = 0f;
            Vector3 from = transform.position;
            foreach (Vector3 point in points)
            {
                distance += Vector3.Distance(from, point);
                from = point;
            }
            if (distance < 0.01f)
            {
                yield break;
            }
            animator.SetBool(WalkingParameter, true);
            // Catmull-Rom: a smooth curve through the cells, so corners are rounded, not sharp turns.
            Tween walk = transform.DOPath(points, distance / walkSpeed, PathType.CatmullRom)
                .SetEase(Ease.Linear)
                .SetLookAt(0.01f);                      // face the way it walks
            yield return _level.Track(walk).WaitForCompletion();
            animator.SetBool(WalkingParameter, false);
        }
    }
}
