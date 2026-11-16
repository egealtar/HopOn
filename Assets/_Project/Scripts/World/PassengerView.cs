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
        [SerializeField] private float walkSpeed = 5f;
        [Tooltip("How a walk speeds up and slows down: time (0 to 1) to distance walked (0 to 1). The walk still takes as long as at walkSpeed; this only shapes it. Default: off at half speed, a steady pace from 12%, a soft stop over the last 25%.")]
        [SerializeField] private AnimationCurve walkEase = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.48f),
            new Keyframe(0.12f, 0.1f, 1.19f, 1.19f),
            new Keyframe(0.75f, 0.85f, 1.19f, 1.19f),
            new Keyframe(1f, 1f, 0f, 0f));
        [SerializeField] private float jumpSeconds = 0.25f;

        private LevelView _level;
        private bool _inWaitingSeat;                    // standing in a waiting seat right now, on screen
        private int _waitingSeat;
        private bool _inQueue;                          // standing at its place in the line off the board
        private (int col, int place) _queueSpot;

        /// <summary>On its way to a waiting seat, it has crossed the line in front of the seats (the level view
        /// judges the line).</summary>
        public bool PastTheLine { get; set; }

        /// <summary>It has reached its waiting seat on screen.</summary>
        public bool InWaitingSeatOnScreen { get; private set; }

        public void Setup(LevelView level, Material color)
        {
            _level = level;
            foreach (Renderer part in bodyParts)
            {
                part.sharedMaterial = color;
            }
        }

        /// <summary>Starts out in a waiting seat (a resumed level).</summary>
        public void StartInWaitingSeat(int seat)
        {
            _level.TakeWaitingSeatTicket(seat);         // the first in its line
            _waitingSeat = seat;
            _inWaitingSeat = true;
            PastTheLine = true;
            InWaitingSeatOnScreen = true;
        }

        /// <summary>Starts out at its place in the line off the board (a resumed level).</summary>
        public void StartInQueue((int col, int place) spot)
        {
            _queueSpot = spot;
            _inQueue = true;
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

        /// <summary>The logic gave this passenger in the line off the board a place: a bus seat or a waiting seat.</summary>
        public void LeaveQueue(Seat seat)
        {
            StartCoroutine(LeaveQueueRoutine(seat));
        }

        /// <summary>Too late for its bus (the level is lost): it stops where it is and stands.</summary>
        public void Stop()
        {
            Freeze();
            transform.DOPause();
        }

        /// <summary>No way out: a short shake, and it stays where it is.</summary>
        public void Shake()
        {
            transform.DOComplete();
            _level.Track(transform.DOPunchPosition(new Vector3(0.2f * BoardLayout.BoardScale, 0f, 0f), 0.35f, 7));
        }

        /// <summary>The level is over: no job goes on to its next step (DOTween itself is paused by the level
        /// view), and a passenger caught walking stops and stands normally instead of freezing mid-step.</summary>
        public void Freeze()
        {
            StopAllCoroutines();
            animator.SetBool(WalkingParameter, false);
        }

        private IEnumerator LeaveBoardRoutine(IReadOnlyList<GridPos> path, int boardWidth, Seat seat)
        {
            _level.BeginWork();
            if (seat.OnBus)
            {
                yield return Walk(WayOut(path, boardWidth, BoardLayout.BusDoor(seat.Index)));
                yield return GetOnBus(seat);
            }
            else if (seat.Queued)
            {
                // Nowhere to go yet: it waits just off the board, facing the stop, until a bus frees a seat
                // (LeaveQueue). Waiting there is not a job: the level is not busy because of it.
                Vector3 spot = _level.TakeQueueSpot(path[path.Count - 1], out _queueSpot);
                yield return Walk(WayOut(path, boardWidth, spot));
                yield return _level.Track(transform.DORotate(Vector3.zero, TurnSeconds)).WaitForCompletion();
                _inQueue = true;
            }
            else
            {
                yield return GoToWaitingSeat(seat.Index, end => WayOut(path, boardWidth, end));
            }
            _level.EndWork();
        }

        private IEnumerator LeaveQueueRoutine(Seat seat)
        {
            _level.BeginWork();
            while (!_inQueue)
            {
                yield return null;                      // still on its way to its place in the line
            }
            _inQueue = false;
            _level.LeftQueueSpot(_queueSpot);
            if (seat.OnBus)
            {
                yield return Walk(BoardLayout.BusDoor(seat.Index));
                yield return GetOnBus(seat);
            }
            else
            {
                yield return GoToWaitingSeat(seat.Index, end => new[] { end });
            }
            _level.EndWork();
        }

        // Its turn in the seat's line comes when everyone sent there before it has stood up. If that has not
        // happened by the time it gets there, it waits in front of the seat. routeTo gives the walk to a point.
        private IEnumerator GoToWaitingSeat(int seat, System.Func<Vector3, Vector3[]> routeTo)
        {
            int ticket = _level.TakeWaitingSeatTicket(seat);
            _waitingSeat = seat;
            if (_level.IsWaitingSeatTurn(seat, ticket))
            {
                yield return Walk(routeTo(BoardLayout.WaitingSeat(seat)));
            }
            else
            {
                yield return Walk(routeTo(BoardLayout.InFrontOfWaitingSeat(seat)));
                while (!_level.IsWaitingSeatTurn(seat, ticket))
                {
                    yield return null;
                }
                yield return Walk(BoardLayout.WaitingSeat(seat));
            }
            // In its seat on screen (the result waits for that), then it turns to face the bus stop.
            InWaitingSeatOnScreen = true;
            yield return _level.Track(transform.DORotate(Vector3.zero, TurnSeconds)).WaitForCompletion();
            _inWaitingSeat = true;
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
                // Stands up only now that its own bus is here, as in Bus Jam; whoever got this seat next
                // may sit down now (it waits in front of the seat until then).
                _inWaitingSeat = false;
                InWaitingSeatOnScreen = false;
                _level.LeftWaitingSeat(_waitingSeat);
                yield return Walk(BoardLayout.BusDoor(seat.Index));
            }

            // Jump in, turning to the seat's direction on the way so it does not snap on landing.
            Transform busSeat = bus.Seat(seat.Index);
            _level.StartedJump();
            _level.Track(transform.DORotateQuaternion(busSeat.rotation, jumpSeconds));
            Tween jump = transform.DOJump(busSeat.position, JumpHeight * BoardLayout.BoardScale, 1, jumpSeconds)
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
            transform.localScale = Vector3.one;         // the bus already has the level's scale
            animator.SetBool(SittingParameter, true);
        }

        // One walk, so it does not stop at the edge of the board: path[0] is the cell it stands on, so the walk
        // starts from the next cell and ends at its spot by the bus, its waiting seat, or wherever it stops.
        private static Vector3[] WayOut(IReadOnlyList<GridPos> path, int boardWidth, Vector3 end)
        {
            var points = new Vector3[path.Count];
            for (int i = 1; i < path.Count; i++)
            {
                points[i - 1] = BoardLayout.CellToWorld(path[i], boardWidth);
            }
            points[path.Count - 1] = end;
            return points;
        }

        // How long a walk from here through the points takes (its ease shapes it, not its length).
        private float WalkSeconds(Vector3[] points)
        {
            float distance = 0f;
            Vector3 from = transform.position;
            foreach (Vector3 point in points)
            {
                distance += Vector3.Distance(from, point);
                from = point;
            }
            // walkSpeed is for scale 1: a scaled level's cells are as many per second
            return distance / (walkSpeed * BoardLayout.BoardScale);
        }

        private IEnumerator Walk(params Vector3[] points)
        {
            float seconds = WalkSeconds(points);
            if (seconds < 0.01f / (walkSpeed * BoardLayout.BoardScale))
            {
                yield break;
            }
            animator.SetBool(WalkingParameter, true);
            // Catmull-Rom: a smooth curve through the cells, so corners are rounded, not sharp turns.
            Tween walk = transform.DOPath(points, seconds, PathType.CatmullRom)
                .SetEase(walkEase)
                .SetLookAt(0.01f);                      // face the way it walks
            yield return _level.Track(walk).WaitForCompletion();
            animator.SetBool(WalkingParameter, false);
        }
    }
}
