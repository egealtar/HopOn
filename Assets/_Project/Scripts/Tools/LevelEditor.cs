using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HopOn.Data;
using HopOn.Logic;
using HopOn.World;
using UnityEngine;

namespace HopOn.Tools
{
    /// <summary>
    /// The level editor scene (Scenes/LevelEditor): press Play in the Unity Editor. The level being edited
    /// is shown with the game's own board view and camera, so it looks exactly as it will in the game.
    /// The panel at the top: pick a level from the catalog or start a new one, resize the board (every
    /// cell keeps its row and column), set the bus order and the timer, save over the level's file or as
    /// a new level added to the catalog, and play it right here to test it. The palette at the bottom:
    /// pick a brush and click or drag on the board to paint. The game's rules are checked on every
    /// change and listed; a level that breaks one is not saved and cannot be tested. A level that passes
    /// them is then played through by <see cref="LevelSolver"/>, which says whether it can be finished.
    /// Every edit can be undone and redone (the buttons, or Ctrl/Cmd + Z and Ctrl/Cmd + Shift + Z or Ctrl + Y).
    /// Saving writes the level's JSON into the project, so it works in the Unity Editor only.
    /// Edits that are not saved are lost when Play mode ends.
    /// A button only notes what was clicked; the change is made in Update. So the panel never changes
    /// between the layout and the drawing of one frame, which IMGUI does not allow.
    /// </summary>
    public sealed class LevelEditor : MonoBehaviour
    {
        // Everything an edit can change: what undo and redo put back.
        private readonly struct Snapshot
        {
            public readonly char[,] Cells;
            public readonly string Buses;
            public readonly float TimeLimit;

            public Snapshot(char[,] cells, string buses, float timeLimit)
            {
                Cells = cells;
                Buses = buses;
                TimeLimit = timeLimit;
            }
        }

        private const string LevelFolder = "Assets/_Project/Data/Levels";
        private const int MinSize = 3;
        private const float MinTime = 5f;
        private const int MaxUndoSteps = 200;
        private const int MaxProblemsShown = 3;
        private const int FinishCheckStates = 20000;    // under two seconds on a full board; the shipped levels need 1400 at most
        private const float Width = 1080f;              // the panel is laid out on a 1080-wide screen and scaled
        private const float TopHeight = 520f;
        private const float BottomHeight = 300f;

        private static readonly char[] Brushes = { '.', ' ', '#', 'R', 'B', 'G', 'Y', 'P', 'O' };
        private static readonly string[] BrushNames = { "Floor", "Hole", "Block", "Red", "Blue", "Green", "Yellow", "Purple", "Orange" };
        private const string Colours = "RBGYPO";

        [SerializeField] private LevelView levelView;
        [SerializeField] private LevelCatalog catalog;
        [SerializeField] private Camera boardCamera;

        private string _id;
        private char[,] _cells;                         // [row, col]; row 0 is the exit row, at the top of the screen
        private string _buses = "";
        private float _timeLimit = 60f;
        private int _busSelected = -1;                  // index in _buses, or -1: none
        private char _brush = 'R';
        private LevelSession _test;                     // not null while the level is being played to test it
        private List<string> _problems = new List<string>();
        private string _status = "";
        private System.Action _clicked;                 // what the panel asked for, done in the next Update

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private bool _strokeOnBoard;                    // the mouse went down on the board and is still held
        private bool _strokeRecorded;                   // this drag already has its undo step

        private Level _level;                           // the level as it is now, or null while it breaks a rule
        private string _boardText = "";                 // cells and buses as one text: what the finish check depends on
        private string _savedText;                      // SaveText when the level was loaded or last saved

        // The finish check: the answer per board text (null: the solver failed), and the one being worked out.
        private readonly Dictionary<string, SolveResult> _solved = new Dictionary<string, SolveResult>();
        private Task<SolveResult> _solving;
        private string _solvingText;

        private GUIStyle _button, _label, _small;

        private int Rows => _cells.GetLength(0);
        private int Cols => _cells.GetLength(1);
        private string SaveText => _boardText + "|" + _timeLimit;
        private bool HasUnsavedChanges => SaveText != _savedText;

        private void Start()
        {
            if (catalog.Levels.Count == 0 || catalog.Levels[0] == null || !Load(0))
            {
                NewLevel();
            }
        }

        private void Update()
        {
            System.Action clicked = _clicked;
            _clicked = null;
            clicked?.Invoke();
            RunFinishCheck();

            if (_test != null)
            {
                _test.Tick(Time.deltaTime);             // the level view plays the taps; this keeps the time
                if (_test.State == SessionState.Won || _test.State == SessionState.Lost)
                {
                    _status = _test.State == SessionState.Won ? "Test: won." : $"Test: lost ({_test.LoseReason}).";
                }
                return;
            }

            // A drag paints only if it began on the board, and is one undo step however many cells it crosses.
            if (Input.GetMouseButtonDown(0))
            {
                _strokeOnBoard = TryCellUnderMouse(out _);
                _strokeRecorded = false;
            }
            if (!Input.GetMouseButton(0))
            {
                _strokeOnBoard = false;
            }
            if (_strokeOnBoard && TryCellUnderMouse(out GridPos cell) && _cells[cell.Row, cell.Col] != _brush)
            {
                if (!_strokeRecorded)
                {
                    Record();
                    _strokeRecorded = true;
                }
                _cells[cell.Row, cell.Col] = _brush;
                Refresh();
            }
        }

        // ---- Editing ----

        // False when the file cannot be read: the level being edited stays as it is.
        private bool Load(int index)
        {
            TextAsset asset = catalog.Levels[index];
            LevelFile file = null;
            try
            {
                file = JsonUtility.FromJson<LevelFile>(asset.text);
            }
            catch (System.ArgumentException)
            {
                // not JSON: reported below
            }
            if (file == null || file.cells == null)
            {
                _status = $"Could not read {asset.name}: the file is not a level.";
                return false;
            }
            int rows = Mathf.Max(file.cells.Length, 1);
            int cols = 1;
            foreach (string row in file.cells)
            {
                cols = Mathf.Max(cols, row.Length);
            }
            _cells = new char[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    _cells[r, c] = r < file.cells.Length && c < file.cells[r].Length ? file.cells[r][c] : Level.VoidCode;
                }
            }
            _id = asset.name;
            _buses = file.buses ?? "";
            _timeLimit = file.timeLimitSeconds;
            _status = $"Loaded {_id}.";
            StartFresh();
            return true;
        }

        private void NewLevel()
        {
            _id = NextFreeId();
            _cells = new char[5, 5];
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    _cells[r, c] = Level.FloorCode;
                }
            }
            _buses = "";
            _timeLimit = 60f;
            _status = $"New level {_id}: paint passengers, add buses, then save.";
            StartFresh();
        }

        // A level was just loaded or started: nothing to undo, nothing unsaved.
        private void StartFresh()
        {
            _undo.Clear();
            _redo.Clear();
            _busSelected = -1;
            Refresh();
            _savedText = SaveText;
        }

        // Loading or starting a level throws the current one away: with unsaved edits, only if the designer agrees.
        private void LeaveLevel(System.Action next)
        {
#if UNITY_EDITOR
            if (HasUnsavedChanges && !UnityEditor.EditorUtility.DisplayDialog("Unsaved changes",
                    $"{_id} has changes that are not saved. Throw them away?", "Throw away", "Keep editing"))
            {
                return;
            }
#endif
            next();
        }

        // Adds or removes a column on the right or a row at the bottom; every other cell stays where it was.
        private void Resize(int rows, int cols)
        {
            rows = Mathf.Clamp(rows, MinSize, BoardLayout.MaxBoardHeight);
            cols = Mathf.Clamp(cols, MinSize, BoardLayout.MaxBoardWidth);
            if (rows == Rows && cols == Cols)
            {
                return;
            }
            Record();
            var cells = new char[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    cells[r, c] = r < Rows && c < Cols ? _cells[r, c] : Level.FloorCode;
                }
            }
            _cells = cells;
            Refresh();
        }

        // One bus per three passengers of a colour, colours in the order they first appear from the top.
        private void AutoBuses()
        {
            var counts = new Dictionary<char, int>();
            var order = new List<char>();
            foreach (string row in RowStrings())
            {
                foreach (char code in row)
                {
                    if (Colours.IndexOf(code) < 0)
                    {
                        continue;
                    }
                    if (!counts.ContainsKey(code))
                    {
                        counts[code] = 0;
                        order.Add(code);
                    }
                    counts[code]++;
                }
            }
            var buses = new StringBuilder();
            foreach (char code in order)
            {
                buses.Append(code, Mathf.CeilToInt(counts[code] / (float)Level.BusCapacity));
            }
            SetBuses(buses.ToString(), -1);
        }

        private void SetTime(float seconds)
        {
            seconds = Mathf.Max(MinTime, seconds);
            if (seconds != _timeLimit)
            {
                Record();
                _timeLimit = seconds;
                Refresh();
            }
        }

        // ---- The bus order ----

        // A colour button: the new bus goes in after the selected one and becomes the selection, so a few
        // in a row land in the order they were clicked. With none selected it goes to the end.
        private void AddBus(char code)
        {
            if (_busSelected >= 0)
            {
                SetBuses(_buses.Insert(_busSelected + 1, code.ToString()), _busSelected + 1);
            }
            else
            {
                SetBuses(_buses + code, -1);
            }
        }

        private void MoveBus(int by)
        {
            int to = _busSelected + by;
            if (_busSelected < 0 || to < 0 || to >= _buses.Length)
            {
                return;
            }
            char[] buses = _buses.ToCharArray();
            (buses[_busSelected], buses[to]) = (buses[to], buses[_busSelected]);
            SetBuses(new string(buses), to);
        }

        // The selection stays in place, on the bus that moved up into the gap.
        private void RemoveBus()
        {
            if (_busSelected >= 0)
            {
                SetBuses(_buses.Remove(_busSelected, 1), Mathf.Min(_busSelected, _buses.Length - 2));
            }
        }

        private void SetBuses(string buses, int selected)
        {
            _busSelected = selected;
            if (buses != _buses)
            {
                Record();
                _buses = buses;
                Refresh();
            }
        }

        // ---- Undo and redo ----

        // Called before every change: the level as it is now becomes the next thing Undo brings back.
        private void Record()
        {
            _undo.Add(Now());
            if (_undo.Count > MaxUndoSteps)
            {
                _undo.RemoveAt(0);
            }
            _redo.Clear();
        }

        private Snapshot Now()
        {
            return new Snapshot((char[,])_cells.Clone(), _buses, _timeLimit);
        }

        private void Undo() => StepBack(_undo, _redo);
        private void Redo() => StepBack(_redo, _undo);

        // Puts back the last snapshot of one list and keeps the present in the other, so the step can be reversed.
        private void StepBack(List<Snapshot> from, List<Snapshot> to)
        {
            if (from.Count == 0 || _test != null || _strokeOnBoard)
            {
                return;
            }
            to.Add(Now());
            Snapshot snapshot = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            _cells = snapshot.Cells;
            _buses = snapshot.Buses;
            _timeLimit = snapshot.TimeLimit;
            _busSelected = -1;
            _status = "";
            Refresh();
        }

        // Checks the rules and shows the level as it is now, rules broken or not.
        private void Refresh()
        {
            _test = null;
            if (_busSelected >= _buses.Length)
            {
                _busSelected = -1;
            }
            List<string> rows = RowStrings();
            _boardText = string.Join("\n", rows) + "|" + _buses;
            Level.TryCreate(_id, rows, _buses, _timeLimit, out _level, out _problems);
            if (Level.TryRead(_id, rows, _buses, _timeLimit, out Level shown, out _))
            {
                levelView.Show(new LevelSession(shown));
            }
        }

        private List<string> RowStrings()
        {
            var rows = new List<string>(Rows);
            for (int r = 0; r < Rows; r++)
            {
                var row = new StringBuilder(Cols);
                for (int c = 0; c < Cols; c++)
                {
                    row.Append(_cells[r, c]);
                }
                rows.Add(row.ToString());
            }
            return rows;
        }

        private bool TryCellUnderMouse(out GridPos cell)
        {
            cell = default;
            float scale = Screen.width / Width;
            float fromTop = Screen.height - Input.mousePosition.y;
            if (fromTop < TopHeight * scale || Input.mousePosition.y < BottomHeight * scale)
            {
                return false;                           // on the panel
            }
            Ray ray = boardCamera.ScreenPointToRay(Input.mousePosition);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
            {
                return false;
            }
            return BoardLayout.TryWorldToCell(ray.GetPoint(distance), Cols, Rows, out cell);
        }

        // ---- The finish check ----

        // Can the level be finished at all? The solver plays it through on another thread, so the editor never
        // stalls on a big board, and each board is solved once: the answer is kept under its text. A new check
        // starts only when the level passes every rule and the mouse is up, never in the middle of a paint drag.
        // Its budget is small, so a hard board answers "too big" soon and never keeps the next board waiting long.
        private void RunFinishCheck()
        {
            if (_solving != null && _solving.IsCompleted)
            {
                if (_solving.IsFaulted)
                {
                    Debug.LogException(_solving.Exception);
                }
                _solved[_solvingText] = _solving.Status == TaskStatus.RanToCompletion ? _solving.Result : null;
                _solving = null;
            }
            if (_solving == null && _level != null && !Input.GetMouseButton(0) && !_solved.ContainsKey(_boardText))
            {
                Level level = _level;
                _solvingText = _boardText;
                _solving = Task.Run(() => LevelSolver.Solve(level, FinishCheckStates));
            }
        }

        // The winning taps for the level as it is now, or null: not checked yet, or no way to finish was found.
        private IReadOnlyList<GridPos> Solution()
        {
            if (_level != null && _solved.TryGetValue(_boardText, out SolveResult finish)
                && finish != null && finish.Outcome == SolveOutcome.Solvable)
            {
                return finish.Moves;
            }
            return null;
        }

        // What the problems area says about a level that passes every rule.
        private string FinishText(out bool good)
        {
            good = true;
            if (!_solved.TryGetValue(_boardText, out SolveResult finish))
            {
                return "OK: every rule passes. Checking that it can be finished...";
            }
            if (finish == null)
            {
                return "OK: every rule passes. The finish check failed (see the Console), so play it through.";
            }
            switch (finish.Outcome)
            {
                case SolveOutcome.Solvable:
                    return "OK: every rule passes and it can be finished. Ready to save.";
                case SolveOutcome.Unsolvable:
                    good = false;
                    return "Cannot be finished: no order of taps gets everyone on a bus without winning a race for the last seat.";
                default:
                    return "OK: every rule passes. Too big to check that it can be finished, so play it through.";
            }
        }

        private static int TapCount(IReadOnlyList<GridPos> moves)
        {
            int taps = 0;
            foreach (GridPos move in moves)
            {
                if (move != LevelSession.BusArrivedMark)
                {
                    taps++;
                }
            }
            return taps;
        }

        // ---- Testing ----

        private bool StartTest()
        {
            if (!Level.TryCreate(_id, RowStrings(), _buses, _timeLimit, out Level level, out _problems))
            {
                _status = "Fix the problems first.";
                return false;
            }
            _test = new LevelSession(level);
            levelView.Show(_test);
            _test.Start();
            _status = "Testing: tap the passengers. Stop to edit again.";
            return true;
        }

        // Starts the test again and plays the solver's taps on it.
        private void StartSolution()
        {
            IReadOnlyList<GridPos> moves = Solution();
            if (moves != null && StartTest())
            {
                _status = "Playing the solution. Stop to edit again.";
                StartCoroutine(PlaySolution(_test, moves));
            }
        }

        // One tap a frame, through the session the level view shows, so the view plays it like a player's taps.
        // Where the solution lets the next bus in, it waits until the view has brought that bus to the stop.
        // It ends by itself when the test is stopped or restarted, or the level is over.
        private IEnumerator PlaySolution(LevelSession session, IReadOnlyList<GridPos> moves)
        {
            yield return null;              // the view's new passengers have their first frame before they are tapped
            int arrivals = 0;
            foreach (GridPos move in moves)
            {
                if (move == LevelSession.BusArrivedMark)
                {
                    arrivals++;
                    while (IsBeingPlayed(session) && Arrivals(session) < arrivals)
                    {
                        yield return null;
                    }
                    continue;
                }
                while (IsBeingPlayed(session))
                {
                    TapResult result = session.Board.PassengerAt(move) == null ? TapResult.Blocked : session.Tap(move);
                    if (result == TapResult.Moved)
                    {
                        break;
                    }
                    if (result == TapResult.Blocked)
                    {
                        _status = "The solution stopped: the board is not as it expects (was it tapped by hand?).";
                        yield break;
                    }
                    yield return null;      // held back while riders walk or buses change: try again next frame
                }
                yield return null;
            }
        }

        private bool IsBeingPlayed(LevelSession session)
        {
            return _test == session && session.State == SessionState.Playing;
        }

        private static int Arrivals(LevelSession session)
        {
            return session.Moves.Count - TapCount(session.Moves);
        }

        // ---- Saving ----

        private void Save(bool asNew)
        {
#if UNITY_EDITOR
            string id = asNew ? NextFreeId() : _id;
            if (!Level.TryCreate(id, RowStrings(), _buses, _timeLimit, out _, out _problems))
            {
                _status = "Not saved: the level breaks a rule (listed above).";
                return;
            }
            var file = new LevelFile { timeLimitSeconds = _timeLimit, buses = _buses, cells = RowStrings().ToArray() };
            string path = $"{LevelFolder}/{id}.json";
            File.WriteAllText(path, JsonUtility.ToJson(file, true) + "\n");
            UnityEditor.AssetDatabase.ImportAsset(path);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            bool listed = false;
            foreach (TextAsset level in catalog.Levels)
            {
                listed |= level == asset;
            }
            if (!listed)
            {
                var so = new UnityEditor.SerializedObject(catalog);
                UnityEditor.SerializedProperty levels = so.FindProperty("levels");
                levels.arraySize++;
                levels.GetArrayElementAtIndex(levels.arraySize - 1).objectReferenceValue = asset;
                so.ApplyModifiedPropertiesWithoutUndo();
                UnityEditor.EditorUtility.SetDirty(catalog);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            _id = id;
            _savedText = SaveText;
            _status = listed ? $"Saved {id}." : $"Saved {id} and added it to the catalog as level {catalog.Levels.Count}.";
            // The finish check never refuses a save: its "cannot" means "not without winning a race", and a
            // level still being worked on must be savable. It warns instead.
            _solved.TryGetValue(_boardText, out SolveResult finish);
            if (finish != null && finish.Outcome == SolveOutcome.Unsolvable)
            {
                _status += " Warning: the finish check found no way to finish it.";
            }
            else if (finish == null || finish.Outcome != SolveOutcome.Solvable)
            {
                _status += " Warning: it is not checked that the level can be finished, so play it through.";
            }
#else
            _status = "Saving writes into the project: open this scene in the Unity Editor.";
#endif
        }

        private static string NextFreeId()
        {
            for (int n = 1; ; n++)
            {
                string id = $"Level_{n:00}";
                if (!File.Exists($"{LevelFolder}/{id}.json"))
                {
                    return id;
                }
            }
        }

        // ---- The panel ----

        private void OnGUI()
        {
            float scale = Screen.width / Width;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            MakeStyles();
            Shortcuts();
            float height = Screen.height / scale;

            GUI.Box(new Rect(0, 0, Width, TopHeight), GUIContent.none);
            GUILayout.BeginArea(new Rect(20, 16, Width - 40, TopHeight - 24));
            TopPanel();
            GUILayout.EndArea();

            GUI.Box(new Rect(0, height - BottomHeight, Width, BottomHeight), GUIContent.none);
            GUILayout.BeginArea(new Rect(20, height - BottomHeight + 16, Width - 40, BottomHeight - 24));
            BottomPanel();
            GUILayout.EndArea();
        }

        private void TopPanel()
        {
            bool editing = _test == null;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Level editor: {_id}{(HasUnsavedChanges ? " *" : "")}", _label);
            GUILayout.FlexibleSpace();
            if (editing)
            {
                GUI.enabled = _undo.Count > 0;
                if (GUILayout.Button("Undo", _button))
                {
                    _clicked = Undo;
                }
                GUI.enabled = _redo.Count > 0;
                if (GUILayout.Button("Redo", _button))
                {
                    _clicked = Redo;
                }
                GUI.enabled = true;
                if (GUILayout.Button("Test", _button))
                {
                    _clicked = () => StartTest();
                }
            }
            else
            {
                GUI.enabled = Solution() != null;
                if (GUILayout.Button("Play solution", _button))
                {
                    _clicked = StartSolution;
                }
                GUI.enabled = true;
                if (GUILayout.Button("Stop", _button))
                {
                    _clicked = () =>
                    {
                        _status = "";
                        Refresh();
                    };
                }
            }
            GUILayout.EndHorizontal();

            GUI.enabled = editing;                      // a level being tested is not edited: Stop first
            GUILayout.BeginHorizontal();
            float width = Mathf.Clamp(560f / Mathf.Max(catalog.Levels.Count, 1) - 4f, 30f, 80f);   // Save stays on the panel
            for (int i = 0; i < catalog.Levels.Count; i++)
            {
                int index = i;
                if (catalog.Levels[i] != null && GUILayout.Button((i + 1).ToString(), _button, GUILayout.Width(width)))
                {
                    _clicked = () => LeaveLevel(() => Load(index));
                }
            }
            if (GUILayout.Button("New", _button))
            {
                _clicked = () => LeaveLevel(NewLevel);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save", _button))
            {
                _clicked = () => Save(false);
            }
            if (GUILayout.Button("Save as new", _button))
            {
                _clicked = () => Save(true);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Stepper("Columns", Cols, d => Resize(Rows, Cols + d), 1);
            Stepper("Rows", Rows, d => Resize(Rows + d, Cols), 1);
            Stepper("Time", Mathf.RoundToInt(_timeLimit), d => SetTime(_timeLimit + d), 5);
            GUILayout.EndHorizontal();

            BusOrder(editing);
            GUI.enabled = true;

            bool good = _problems.Count == 0;
            string problems = good ? FinishText(out good) : ProblemsText();
            GUI.contentColor = good ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.75f, 0.6f);
            GUILayout.Label(problems, _small);
            GUI.contentColor = Color.white;
            if (_status.Length > 0)
            {
                GUILayout.Label(_status, _small);
            }
        }

        // The buses in the order they come, each a button that selects it (again: selects none), and under them
        // what can be done to the selected one and the colours that add a bus.
        private void BusOrder(bool editing)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(_buses.Length > 0 ? "Buses" : "Buses: none", _label);
            float width = Mathf.Clamp(900f / Mathf.Max(_buses.Length, 1) - 4f, 20f, 64f);   // a long order still fits
            for (int i = 0; i < _buses.Length; i++)
            {
                int index = i;
                Color colour = ColourOf(_buses[i]);
                // with a bus selected, the others go dark
                GUI.backgroundColor = _busSelected < 0 || _busSelected == i ? colour : Color.Lerp(colour, Color.black, 0.5f);
                if (GUILayout.Button(_buses[i].ToString(), _button, GUILayout.Width(width)))
                {
                    _clicked = () => _busSelected = _busSelected == index ? -1 : index;
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool selected = _busSelected >= 0;
            GUI.enabled = editing && _busSelected > 0;
            if (GUILayout.Button("<", _button, GUILayout.Width(64)))
            {
                _clicked = () => MoveBus(-1);
            }
            GUI.enabled = editing && selected && _busSelected < _buses.Length - 1;
            if (GUILayout.Button(">", _button, GUILayout.Width(64)))
            {
                _clicked = () => MoveBus(1);
            }
            GUI.enabled = editing && selected;
            if (GUILayout.Button("Remove", _button, GUILayout.Width(140)))
            {
                _clicked = RemoveBus;
            }
            GUI.enabled = editing;
            GUILayout.FlexibleSpace();
            GUILayout.Label(selected ? $"Insert after bus {_busSelected + 1}:" : "Add at the end:", _small);
            foreach (char code in Colours)
            {
                GUI.backgroundColor = ColourOf(code);
                if (GUILayout.Button(code.ToString(), _button, GUILayout.Width(56)))
                {
                    _clicked = () => AddBus(code);
                }
            }
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Auto", _button, GUILayout.Width(100)))
            {
                _clicked = AutoBuses;
            }
            GUILayout.EndHorizontal();
        }

        // The first few broken rules: a long list would push the status line off the panel.
        private string ProblemsText()
        {
            var text = new StringBuilder();
            for (int i = 0; i < _problems.Count && i < MaxProblemsShown; i++)
            {
                text.Append(i > 0 ? "\n• " : "• ").Append(_problems[i]);
            }
            if (_problems.Count > MaxProblemsShown)
            {
                text.Append($" (and {_problems.Count - MaxProblemsShown} more)");
            }
            return text.ToString();
        }

        // Ctrl or Cmd + Z undoes; with Shift, or Ctrl or Cmd + Y, redoes.
        private void Shortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || !(e.control || e.command) || (e.keyCode != KeyCode.Z && e.keyCode != KeyCode.Y))
            {
                return;
            }
            if (e.keyCode == KeyCode.Y || e.shift)
            {
                _clicked = Redo;
            }
            else
            {
                _clicked = Undo;
            }
            e.Use();
        }

        private void BottomPanel()
        {
            GUILayout.Label("Brush: click or drag on the board", _small);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Brushes.Length; i++)
            {
                char code = Brushes[i];
                GUI.backgroundColor = ColourOf(code);
                string name = code == _brush ? $"[{BrushNames[i]}]" : BrushNames[i];
                if (GUILayout.Button(name, _button, GUILayout.Height(90))) _brush = code;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Label(SeatCounts(), _small);
        }

        // "Red 6/6" for each colour: passengers on the board / seats on its buses.
        private string SeatCounts()
        {
            var text = new StringBuilder();
            string rows = string.Concat(RowStrings());
            foreach (char code in Colours)
            {
                int people = rows.Split(code).Length - 1;
                int seats = (_buses.Split(code).Length - 1) * Level.BusCapacity;
                if (people + seats > 0)
                {
                    text.Append($"{BrushNames[System.Array.IndexOf(Brushes, code)]} {people}/{seats}   ");
                }
            }
            return text.Length > 0 ? "Passengers / bus seats:   " + text : "No passengers yet.";
        }

        private void Stepper(string name, int value, System.Action<int> change, int step)
        {
            GUILayout.Label($"{name} {value}", _label, GUILayout.Width(176));    // three of these fit the panel's width
            if (GUILayout.Button("-", _button, GUILayout.Width(64)))
            {
                _clicked = () => change(-step);
            }
            if (GUILayout.Button("+", _button, GUILayout.Width(64)))
            {
                _clicked = () => change(step);
            }
            GUILayout.Space(12);
        }

        private static Color ColourOf(char code)
        {
            switch (code)
            {
                case 'R': return new Color(0.95f, 0.4f, 0.42f);
                case 'B': return new Color(0.4f, 0.62f, 1f);
                case 'G': return new Color(0.45f, 0.85f, 0.5f);
                case 'Y': return new Color(1f, 0.88f, 0.35f);
                case 'P': return new Color(0.72f, 0.52f, 0.95f);
                case 'O': return new Color(1f, 0.65f, 0.3f);
                case ' ': return new Color(0.35f, 0.35f, 0.4f);
                case '#': return new Color(0.55f, 0.75f, 0.5f);
                default: return Color.white;
            }
        }

        private void MakeStyles()
        {
            if (_button != null)
            {
                return;
            }
            _button = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold, fixedHeight = 64 };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 24, wordWrap = true };
        }
    }
}
