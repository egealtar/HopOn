using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using HopOn.Logic;
using HopOn.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HopOn.World
{
    /// <summary>
    /// The 3D picture of one level. It builds the board from the session's current state (so a
    /// fresh and a resumed level look the same), turns taps into cells for the session, and plays
    /// the session's events back as animations. It decides nothing: the session already has.
    /// Passengers walk at the same time; only the places they share are put in order:
    /// a bus leaves only when the logic sent it away and all its riders are in, the next bus
    /// pulls in only after that, a rider gets in only when its own bus is at the stop, and a
    /// waiting seat is sat in by its passengers in the order the logic gave it out (whoever got it
    /// next waits in front of it until the one before has stood up). So the logic may hand out a seat
    /// the picture has not emptied yet. The one thing judged here is the line in front of the waiting
    /// seats: who crosses it first, a bus's riders or the passenger for the last seat (see LevelSession).
    /// It runs after the EventSystem in each frame (execution order 100), because the EventSystem
    /// only learns about a new touch in its own Update, and the UI check below needs that.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class LevelView : MonoBehaviour
    {
        // The next bus can be boarded 0.5 s after the last rider of the full one sits down.
        private const float BusLeaveSeconds = 0.4f;
        private const float BusArriveSeconds = 0.4f;
        private const float NextBusDelaySeconds = 0.1f;
        private const float BusArriveOvershoot = 1.2f;      // a small brake bounce; more and it hits the leaving bus

        [Header("Board pieces")]
        [SerializeField] private GameObject floorTilePrefab;
        [SerializeField] private GameObject obstaclePrefab;
        [SerializeField] private GameObject waitingSpotPrefab;
        [Tooltip("One cell side of the rim around the board (a strip with round ends): placed on every side that has no ground beyond it.")]
        [SerializeField] private GameObject rimPrefab;
        [Tooltip("Everything below the park in the Environment (road, curb, floor, stone ground), its pivot on the park's lower edge: scaled with the level.")]
        [SerializeField] private Transform belowPark;

        [Header("Passengers and buses")]
        [SerializeField] private PassengerView passengerPrefab;
        [SerializeField] private BusView busPrefab;
        [Tooltip("One material per colour, in this order: Red, Blue, Green, Yellow, Purple, Orange.")]
        [SerializeField] private Material[] colorMaterials = new Material[6];
        [Tooltip("The same colours for the buses, in the same order. They carry the bus's detail map, which only fits the bus's UVs, so the passengers keep their own.")]
        [SerializeField] private Material[] busMaterials = new Material[6];

        [Tooltip("The camera that shows the level: it frames each level and turns taps into cells. Empty: the main camera.")]
        [SerializeField] private BoardCamera boardCamera;

        private readonly Dictionary<int, PassengerView> _passengers = new Dictionary<int, PassengerView>();
        private BusView[] _buses = new BusView[0];
        private LevelSession _session;
        private Camera _camera;
        private Transform _root;
        private int _jobsRunning;
        private readonly int[] _seatTicketsGiven = new int[Level.WaitingSeats];  // per waiting seat: passengers sent to it
        private readonly int[] _seatTurnsDone = new int[Level.WaitingSeats];     // per waiting seat: passengers who have left it
        private int _ridersInMidAir;
        private bool _frozen;

        // Walking towards the line in front of the waiting seats (BoardLayout.LineZ), not across it yet: riders for
        // a bus, and passengers for a waiting seat. Checked once a frame, riders first, so a tie goes to the rider.
        private readonly List<(PassengerView passenger, BusView bus)> _ridersBeforeTheLine = new List<(PassengerView, BusView)>();
        private readonly List<PassengerView> _seatGoersBeforeTheLine = new List<PassengerView>();

        // The line off the board: per column of the board, which places are taken.
        private readonly List<(int col, int place)> _queueSpotsTaken = new List<(int, int)>();

        /// <summary>Some passenger or bus has not finished its job yet (walking, waiting for its bus, driving off).</summary>
        public bool IsBusy => _jobsRunning > 0;

        /// <summary>Someone stands in every waiting seat on screen: the moment Bus Jam shows "no seats".</summary>
        public bool WaitingSeatsLookFull => EveryWaitingPassenger(p => p.InWaitingSeatOnScreen);

        /// <summary>A rider is jumping into a bus right now; a freeze would leave it hanging in the air.</summary>
        public bool SomeoneInMidAir => _ridersInMidAir > 0;

        private void Awake()
        {
            if (boardCamera == null)
            {
                boardCamera = Camera.main.GetComponent<BoardCamera>();
            }
            _camera = boardCamera.GetComponent<Camera>();
        }

        private void OnDestroy()
        {
            Clear();
        }

        public void Show(LevelSession session)
        {
            Clear();
            _session = session;
            BoardLayout.FitBoard(session.Board.Width, session.Board.Height);
            belowPark.localScale = Vector3.one * BoardLayout.BoardScale;
            Build();
            boardCamera.Frame(BoardLayout.FramedPoints());
            _session.PassengerLeftBoard += OnPassengerLeftBoard;
            _session.PassengerLeftWaiting += OnPassengerLeftWaiting;
            _session.PassengerLeftQueue += OnPassengerLeftQueue;
            _session.BusDeparted += OnBusDeparted;
            StartCoroutine(RunBusLine());
        }

        /// <summary>Stops everything where it is and ignores taps: the level is over.</summary>
        public void Freeze()
        {
            _frozen = true;
            StopAllCoroutines();                        // the bus line must not start new moves either
            DOTween.Pause(this);
            foreach (PassengerView passenger in _root.GetComponentsInChildren<PassengerView>())
            {
                passenger.Freeze();
            }
        }

        public void Clear()
        {
            if (_session != null)
            {
                _session.PassengerLeftBoard -= OnPassengerLeftBoard;
                _session.PassengerLeftWaiting -= OnPassengerLeftWaiting;
                _session.PassengerLeftQueue -= OnPassengerLeftQueue;
                _session.BusDeparted -= OnBusDeparted;
                _session = null;
            }
            StopAllCoroutines();
            DOTween.Kill(this);
            if (_root != null)
            {
                _root.gameObject.SetActive(false);      // stops the passengers' coroutines at once
                Destroy(_root.gameObject);
                _root = null;
            }
            _passengers.Clear();
            _buses = new BusView[0];
            _jobsRunning = 0;
            System.Array.Clear(_seatTicketsGiven, 0, _seatTicketsGiven.Length);
            System.Array.Clear(_seatTurnsDone, 0, _seatTurnsDone.Length);
            _ridersInMidAir = 0;
            _ridersBeforeTheLine.Clear();
            _seatGoersBeforeTheLine.Clear();
            _queueSpotsTaken.Clear();
            _frozen = false;
        }

        // ---- Used by passengers ----

        internal BusView Bus(int busIndex) => _buses[busIndex];
        internal void BeginWork() => _jobsRunning++;
        internal void EndWork() => _jobsRunning--;

        /// <summary>A place in the waiting seat's line, taken when the logic gives the passenger that seat.</summary>
        internal int TakeWaitingSeatTicket(int seat) => _seatTicketsGiven[seat]++;

        /// <summary>Everyone sent to this seat before the ticket's holder has stood up from it.</summary>
        internal bool IsWaitingSeatTurn(int seat, int ticket) => _seatTurnsDone[seat] >= ticket;

        internal void LeftWaitingSeat(int seat)
        {
            _seatTurnsDone[seat]++;
        }
        internal void StartedJump() => _ridersInMidAir++;
        internal void Landed() => _ridersInMidAir--;

        /// <summary>The first free place off the board above this exit cell, for a passenger with nowhere to go.</summary>
        internal Vector3 TakeQueueSpot(GridPos exitCell, out (int col, int place) spot)
        {
            int place = 0;
            while (_queueSpotsTaken.Contains((exitCell.Col, place)))
            {
                place++;
            }
            spot = (exitCell.Col, place);
            _queueSpotsTaken.Add(spot);
            return BoardLayout.QueueSpot(exitCell, _session.Board.Width, place);
        }

        internal void LeftQueueSpot((int col, int place) spot)
        {
            _queueSpotsTaken.Remove(spot);
        }

        // ---- The line in front of the waiting seats ----

        // Who crossed the line this frame. Riders first: a rider and a passenger for the last seat crossing in the
        // same frame is a race the rider wins.
        private void CheckTheLine()
        {
            float line = BoardLayout.LineZ;
            for (int i = _ridersBeforeTheLine.Count - 1; i >= 0; i--)
            {
                (PassengerView rider, BusView bus) = _ridersBeforeTheLine[i];
                if (rider.transform.position.z >= line)
                {
                    _ridersBeforeTheLine.RemoveAt(i);
                    RiderIn(bus);
                }
            }
            bool someoneCrossed = false;
            for (int i = _seatGoersBeforeTheLine.Count - 1; i >= 0; i--)
            {
                PassengerView passenger = _seatGoersBeforeTheLine[i];
                if (passenger.transform.position.z >= line)
                {
                    _seatGoersBeforeTheLine.RemoveAt(i);
                    passenger.PastTheLine = true;
                    someoneCrossed = true;
                }
            }
            if (someoneCrossed && EveryWaitingPassenger(p => p.PastTheLine))
            {
                _session.WaitingSeatsFilled();
                if (_session.State == SessionState.Lost)
                {
                    // Too late: riders still before the line stop where they are, they do not get on any more.
                    foreach ((PassengerView rider, BusView _) in _ridersBeforeTheLine)
                    {
                        rider.Stop();
                    }
                    _ridersBeforeTheLine.Clear();
                }
            }
        }

        // A rider of this bus is across the line (or was already, coming from a waiting seat). Once all of a bus
        // the logic sent away are, they made it.
        private void RiderIn(BusView bus)
        {
            bus.Arrived++;
            if (bus.DepartOrdered && bus.Arrived >= Level.BusCapacity)
            {
                _session.BusBoarded();
            }
        }

        // Every waiting seat is taken in the logic and each of those passengers passes the test on screen.
        private bool EveryWaitingPassenger(System.Func<PassengerView, bool> test)
        {
            if (_session == null || !_session.Waiting.IsFull)
            {
                return false;
            }
            for (int seat = 0; seat < _session.Waiting.SeatCount; seat++)
            {
                if (!test(_passengers[_session.Waiting.At(seat).Id]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Tags a tween as part of this level, so Freeze pauses it and Clear kills it.</summary>
        internal Tween Track(Tween tween)
        {
            return tween.SetId(this);
        }

        // ---- Input ----

        private void Update()
        {
            if (_session == null || _frozen)
            {
                return;
            }
            CheckTheLine();
            // Every finger that touched down this frame is a tap, so fast taps with two thumbs all count. The
            // mouse (the editor) only when there is no touch: on a phone Unity also turns the first finger
            // into a mouse click.
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began && !IsPointerOverUI(touch.fingerId))
                    {
                        TapAt(touch.position);
                    }
                }
            }
            else if (Input.GetMouseButtonDown(0) && !IsPointerOverUI(PointerInputModule.kMouseLeftId))
            {
                TapAt(Input.mousePosition);
            }
        }

        private void TapAt(Vector2 screenPoint)
        {
            if (_session == null || _frozen)
            {
                return;                                 // an earlier finger this frame ended the level
            }
            Ray ray = _camera.ScreenPointToRay(screenPoint);
            var tapPlane = new Plane(Vector3.up, new Vector3(0f, BoardLayout.TapHeight, 0f));
            if (!tapPlane.Raycast(ray, out float distance))
            {
                return;
            }
            Board board = _session.Board;
            if (!BoardLayout.TryWorldToCell(ray.GetPoint(distance), board.Width, board.Height, out GridPos cell))
            {
                return;
            }
            Passenger tapped = board.PassengerAt(cell);
            TapResult result = _session.Tap(cell);
            if (tapped == null || result == TapResult.Ignored)
            {
                return;
            }
            if (result == TapResult.Moved)
            {
                Haptics.Light();                        // felt only when the passenger sets off
            }
            else
            {
                _passengers[tapped.Id].Shake();
            }
        }

        // A tap on a button must not also tap the board behind it. Each touch has its own pointer id.
        private static bool IsPointerOverUI(int pointerId)
        {
            EventSystem events = EventSystem.current;
            return events != null && events.IsPointerOverGameObject(pointerId);
        }

        // ---- Session events: each one becomes a job for one passenger or one bus ----

        private void OnPassengerLeftBoard(Passenger passenger, IReadOnlyList<GridPos> path, Seat seat)
        {
            PassengerView view = _passengers[passenger.Id];
            HeadForTheLine(view, seat);
            view.LeaveBoard(path, _session.Board.Width, seat);
        }

        private void OnPassengerLeftWaiting(Passenger passenger, int waitingSeat, Seat busSeat)
        {
            PassengerView view = _passengers[passenger.Id];
            BusView bus = _buses[busSeat.BusIndex];
            if (_seatGoersBeforeTheLine.Remove(view))
            {
                _ridersBeforeTheLine.Add((view, bus));  // still on its way to the seat: it crosses as this bus's rider
            }
            else
            {
                RiderIn(bus);                           // from its seat, beyond the line already
            }
            view.LeaveWaitingSeat(busSeat);
        }

        private void OnPassengerLeftQueue(Passenger passenger, Seat seat)
        {
            PassengerView view = _passengers[passenger.Id];
            HeadForTheLine(view, seat);
            view.LeaveQueue(seat);
        }

        // From the board or the line off it, a passenger with a seat walks across the line in front of the seats.
        private void HeadForTheLine(PassengerView view, Seat seat)
        {
            if (seat.OnBus)
            {
                _ridersBeforeTheLine.Add((view, _buses[seat.BusIndex]));
            }
            else if (!seat.Queued)
            {
                view.PastTheLine = false;
                _seatGoersBeforeTheLine.Add(view);
            }
        }

        // The bus counts as busy from this moment, not only while it drives, so there is no frame
        // between "last rider in" and "bus starts to leave" in which the level looks finished.
        // Filled from the waiting seats, its riders are all beyond the line already.
        private void OnBusDeparted(int busIndex)
        {
            BusView bus = _buses[busIndex];
            bus.DepartOrdered = true;
            BeginWork();
            if (bus.Arrived >= Level.BusCapacity)
            {
                _session.BusBoarded();
            }
        }

        // ---- The bus line ----

        // The front bus waits at the stop until the logic has sent it away and all its riders are
        // in; then it drives off while the line moves up and the next bus pulls in.
        private IEnumerator RunBusLine()
        {
            for (int i = _session.Buses.CurrentIndex; i < _buses.Length; i++)
            {
                BusView bus = _buses[i];
                while (!bus.DepartOrdered || bus.Seated < Level.BusCapacity)
                {
                    yield return null;
                }

                bus.IsAtStop = false;
                Tween leave = Track(bus.transform.DOMove(BoardLayout.BusExit, BusLeaveSeconds).SetEase(Ease.InQuad));
                Tween arrive = null;
                for (int next = i + 1; next < _buses.Length; next++)
                {
                    Tween move = Track(_buses[next].transform.DOMove(BoardLayout.BusSlot(next - i - 1), BusArriveSeconds)
                        .SetEase(Ease.OutBack, BusArriveOvershoot)
                        .SetDelay(NextBusDelaySeconds));
                    if (next == i + 1)
                    {
                        arrive = move;
                    }
                }
                yield return leave.WaitForCompletion();
                bus.gameObject.SetActive(false);
                if (arrive != null)
                {
                    yield return arrive.WaitForCompletion();
                    _buses[i + 1].IsAtStop = true;
                    _session.BusArrived();      // only now does it take the waiting passengers of its colour
                }
                EndWork();
            }
        }

        // ---- Building the level ----

        private void Build()
        {
            // BoardLayout positions are world positions (walks and bus moves use them directly),
            // so the level sits at the world origin wherever this object happens to be.
            _root = new GameObject("Level").transform;
            _root.SetParent(transform, false);
            _root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Board board = _session.Board;

            for (int row = 0; row < board.Height; row++)
            {
                for (int col = 0; col < board.Width; col++)
                {
                    var cell = new GridPos(row, col);
                    Vector3 at = BoardLayout.CellToWorld(cell, board.Width);
                    CellType type = board.CellAt(cell);
                    if (type == CellType.Floor)
                    {
                        Place(floorTilePrefab, at, BoardLayout.BoardScale);
                    }
                    else if (type == CellType.Obstacle)
                    {
                        Place(obstaclePrefab, at, BoardLayout.BoardScale);
                    }

                    Passenger passenger = board.PassengerAt(cell);
                    if (passenger != null)
                    {
                        MakePassenger(passenger, at).transform.localScale = Vector3.one * BoardLayout.BoardScale;
                    }
                }
            }

            BuildRim(board);

            for (int seat = 0; seat < Level.WaitingSeats; seat++)
            {
                Vector3 at = BoardLayout.WaitingSeat(seat);
                Place(waitingSpotPrefab, at, BoardLayout.BoardScale);
                Passenger waiting = _session.Waiting.At(seat);
                if (waiting != null)
                {
                    PassengerView view = MakePassenger(waiting, at);
                    view.transform.localScale = Vector3.one * BoardLayout.BoardScale;
                    view.StartInWaitingSeat(seat);
                }
            }

            // A resumed level may have passengers waiting off the board. Which column each left from is not saved,
            // so they stand side by side from the left.
            for (int i = 0; i < _session.Queue.Count; i++)
            {
                var exitCell = new GridPos(0, Mathf.Min(i, board.Width - 1));
                Vector3 at = TakeQueueSpot(exitCell, out var spot);
                PassengerView view = MakePassenger(_session.Queue[i], at);
                view.transform.localScale = Vector3.one * BoardLayout.BoardScale;
                view.StartInQueue(spot);
            }

            BusQueue buses = _session.Buses;
            _buses = new BusView[buses.Count];
            for (int i = buses.CurrentIndex; i < buses.Count; i++)
            {
                BusView bus = Instantiate(busPrefab, _root);
                bus.name = $"Bus {i}";
                bus.transform.localPosition = BoardLayout.BusSlot(i - buses.CurrentIndex);
                bus.transform.localScale = Vector3.one * BoardLayout.BoardScale;
                bus.Paint(busMaterials[(int)buses.ColorAt(i)]);
                _buses[i] = bus;
            }
            if (!buses.AllGone)
            {
                // The front bus starts at the stop. In a resumed level it may already have riders:
                // the logic only counts them, but they all have the bus's colour, so they can be
                // put back in their seats.
                BusView front = _buses[buses.CurrentIndex];
                front.IsAtStop = true;
                front.Seated = buses.SeatsTaken;
                front.Arrived = buses.SeatsTaken;
                for (int seat = 0; seat < buses.SeatsTaken; seat++)
                {
                    PassengerView rider = Instantiate(passengerPrefab, _root);
                    rider.Setup(this, colorMaterials[(int)buses.ColorAt(buses.CurrentIndex)]);
                    rider.StartInBus(front, seat);
                }
            }
        }

        // The rim follows the board's shape: one strip on every cell side with no ground beyond it. The strips
        // have round ends, so two that meet at a corner overlap into a round corner. The waiting floor above
        // row 0 runs across the whole screen and counts as ground, so the rim also runs along its lower edge,
        // out past the board's sides.
        private void BuildRim(Board board)
        {
            const int FloorPastBoard = 6;                   // floor cells beyond each side of the board, past any screen
            for (int row = -1; row < board.Height; row++)
            {
                for (int col = -FloorPastBoard; col < board.Width + FloorPastBoard; col++)
                {
                    var cell = new GridPos(row, col);
                    if (!HasGround(board, cell))
                    {
                        continue;
                    }
                    foreach (GridPos beyond in new[] { cell.Up, cell.Down, cell.Left, cell.Right })
                    {
                        if (HasGround(board, beyond))
                        {
                            continue;
                        }
                        // halfway between the two cells; a left or right side runs towards the camera
                        Vector3 side = (BoardLayout.CellToWorld(cell, board.Width) + BoardLayout.CellToWorld(beyond, board.Width)) * 0.5f;
                        Quaternion turn = beyond.Row == cell.Row ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
                        Instantiate(rimPrefab, side, turn, _root).transform.localScale *= BoardLayout.BoardScale;
                    }
                }
            }
        }

        private static bool HasGround(Board board, GridPos cell)
        {
            return cell.Row < 0 || board.CellAt(cell) != CellType.Void;
        }

        private PassengerView MakePassenger(Passenger passenger, Vector3 at)
        {
            PassengerView view = Instantiate(passengerPrefab, _root);
            view.name = $"Passenger #{passenger.Id}";
            view.transform.localPosition = at;
            view.Setup(this, colorMaterials[(int)passenger.Color]);
            _passengers.Add(passenger.Id, view);
            return view;
        }

        // A piece from its prefab; the prefab keeps its own rotation, its scale times the given one.
        private void Place(GameObject prefab, Vector3 at, float scale = 1f)
        {
            GameObject piece = Instantiate(prefab, _root);
            piece.transform.localPosition = at;
            piece.transform.localScale *= scale;
        }
    }
}
