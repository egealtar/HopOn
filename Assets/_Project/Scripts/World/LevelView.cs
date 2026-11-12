using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using HopOn.Logic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HopOn.World
{
    /// <summary>
    /// The 3D picture of one level. It builds the board from the session's current state (so a
    /// fresh and a resumed level look the same), turns taps into cells for the session, and plays
    /// the session's events back as animations. It decides nothing: the session already has.
    /// Passengers walk at the same time; only boarding is put in order by three rules:
    /// a bus leaves only when the logic sent it away and all its riders are in, the next bus
    /// pulls in only after that, and a rider gets in only when its own bus is at the stop.
    /// Like Bus Jam, taps are locked while buses change (see <see cref="BusesAreChanging"/>).
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

        [Header("Passengers and buses")]
        [SerializeField] private PassengerView passengerPrefab;
        [SerializeField] private BusView busPrefab;
        [Tooltip("One material per colour, in this order: Red, Blue, Green, Yellow, Purple, Orange.")]
        [SerializeField] private Material[] colorMaterials = new Material[6];

        [Tooltip("The camera that shows the level: it frames each level and turns taps into cells. Empty: the main camera.")]
        [SerializeField] private BoardCamera boardCamera;

        private readonly Dictionary<int, PassengerView> _passengers = new Dictionary<int, PassengerView>();
        private BusView[] _buses = new BusView[0];
        private LevelSession _session;
        private Camera _camera;
        private Transform _root;
        private int _jobsRunning;
        private int _waitingSeatsInUse;
        private int _ridersInMidAir;
        private bool _frozen;

        /// <summary>Some passenger or bus has not finished its job yet (walking, waiting for its bus, driving off).</summary>
        public bool IsBusy => _jobsRunning > 0;

        /// <summary>Someone stands in every waiting seat on screen: the moment Bus Jam shows "no seats".</summary>
        public bool WaitingSeatsLookFull => _waitingSeatsInUse >= Level.WaitingSeats;

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
            Build();
            boardCamera.Frame(BoardLayout.FramedPoints(session.Board.Width, session.Board.Height));
            _session.PassengerLeftBoard += OnPassengerLeftBoard;
            _session.PassengerLeftWaiting += OnPassengerLeftWaiting;
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
            _waitingSeatsInUse = 0;
            _ridersInMidAir = 0;
            _frozen = false;
        }

        // ---- Used by passengers ----

        internal BusView Bus(int busIndex) => _buses[busIndex];
        internal void BeginWork() => _jobsRunning++;
        internal void EndWork() => _jobsRunning--;
        internal void EnteredWaitingSeat() => _waitingSeatsInUse++;
        internal void LeftWaitingSeat() => _waitingSeatsInUse--;
        internal void StartedJump() => _ridersInMidAir++;
        internal void Landed() => _ridersInMidAir--;

        /// <summary>Tags a tween as part of this level, so Freeze pauses it and Clear kills it.</summary>
        internal Tween Track(Tween tween)
        {
            return tween.SetId(this);
        }

        // ---- Input ----

        private void Update()
        {
            if (_session == null || _frozen || BusesAreChanging() || !Input.GetMouseButtonDown(0) || IsPointerOverUI())
            {
                return;
            }
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
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
            if (_session.Tap(cell) == TapResult.Blocked && tapped != null)
            {
                _passengers[tapped.Id].Shake();
            }
        }

        // From the tap that fills a bus until the bus the logic now has at the stop has pulled in on
        // screen. The logic has already put the waiting passengers on that bus and freed their seats,
        // but on screen they stand up only when it arrives. Unlocking at that same moment means a
        // passenger tapped then reaches the waiting seats after they have left them.
        private bool BusesAreChanging()
        {
            BusQueue buses = _session.Buses;
            return !buses.AllGone && !_buses[buses.CurrentIndex].IsAtStop;
        }

        // A tap on a button must not also tap the board behind it. A touch has its own pointer id.
        private static bool IsPointerOverUI()
        {
            EventSystem events = EventSystem.current;
            if (events == null)
            {
                return false;
            }
            if (Input.touchCount > 0)
            {
                return events.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            }
            return events.IsPointerOverGameObject();
        }

        // ---- Session events: each one becomes a job for one passenger or one bus ----

        private void OnPassengerLeftBoard(Passenger passenger, IReadOnlyList<GridPos> path, Seat seat)
        {
            _passengers[passenger.Id].LeaveBoard(path, _session.Board.Width, seat);
        }

        private void OnPassengerLeftWaiting(Passenger passenger, int waitingSeat, Seat busSeat)
        {
            _passengers[passenger.Id].LeaveWaitingSeat(busSeat);
        }

        // The bus counts as busy from this moment, not only while it drives, so there is no frame
        // between "last rider in" and "bus starts to leave" in which the level looks finished.
        private void OnBusDeparted(int busIndex)
        {
            _buses[busIndex].DepartOrdered = true;
            BeginWork();
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
                        Place(floorTilePrefab, at);
                    }
                    else if (type == CellType.Obstacle)
                    {
                        Place(obstaclePrefab, at);
                    }

                    Passenger passenger = board.PassengerAt(cell);
                    if (passenger != null)
                    {
                        MakePassenger(passenger, at);
                    }
                }
            }

            for (int seat = 0; seat < Level.WaitingSeats; seat++)
            {
                Vector3 at = BoardLayout.WaitingSeat(seat);
                Place(waitingSpotPrefab, at);
                Passenger waiting = _session.Waiting.At(seat);
                if (waiting != null)
                {
                    MakePassenger(waiting, at).StartInWaitingSeat();
                }
            }

            BusQueue buses = _session.Buses;
            _buses = new BusView[buses.Count];
            for (int i = buses.CurrentIndex; i < buses.Count; i++)
            {
                BusView bus = Instantiate(busPrefab, _root);
                bus.name = $"Bus {i}";
                bus.transform.localPosition = BoardLayout.BusSlot(i - buses.CurrentIndex);
                bus.Paint(colorMaterials[(int)buses.ColorAt(i)]);
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
                for (int seat = 0; seat < buses.SeatsTaken; seat++)
                {
                    PassengerView rider = Instantiate(passengerPrefab, _root);
                    rider.Setup(this, colorMaterials[(int)buses.ColorAt(buses.CurrentIndex)]);
                    rider.StartInBus(front, seat);
                }
            }
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

        // A board piece from its prefab; the prefab keeps its own rotation and scale.
        private void Place(GameObject prefab, Vector3 at)
        {
            GameObject piece = Instantiate(prefab, _root);
            piece.transform.localPosition = at;
        }
    }
}
