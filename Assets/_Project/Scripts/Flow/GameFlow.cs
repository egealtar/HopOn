using System.Collections;
using HopOn.Data;
using HopOn.Logic;
using HopOn.UI;
using HopOn.World;
using UnityEngine;

namespace HopOn.Flow
{
    public enum GameState
    {
        Start,
        Playing,
        Ending,     // the session has decided; waiting for the animations to reach that moment
        Won,
        Lost,
    }

    /// <summary>
    /// The game's flow and the one place that wires everything together: it loads the current
    /// level, creates its <see cref="LevelSession"/>, feeds it time and shows the right screen.
    /// Start -> Playing -> Ending -> Won -> Start (next level), or ... -> Lost -> Start (same level).
    /// It never decides a win or a loss: the session does. The flow reads the session's State
    /// every frame, waits until the picture has caught up, then shows the matching screen.
    /// It talks to whole screens (views), never to their buttons or texts, and every handler
    /// checks the current state first, so a double tap can never run a transition twice.
    /// It also saves where the player is (<see cref="SaveStore"/>): the level in play after every move
    /// and when the app goes to the background, and the level to play next the moment a level is
    /// decided. On launch the game opens there, a level left in the middle exactly as it was left,
    /// behind the start screen with the time that was left.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        // A view bug must never trap the player: the result shows after this long at the latest.
        // A real ending can take over 3 s (a last long walk, then several buses leaving in a row),
        // so this is far above that; when it fires, the warning in the console means a view bug.
        private const float MaxEndingSeconds = 8f;

        [SerializeField] private LevelCatalog levelCatalog;
        [SerializeField] private LevelView levelView;

        [Header("Screens")]
        [SerializeField] private StartView startView;
        [SerializeField] private HudView hudView;
        [SerializeField] private WinView winView;
        [SerializeField] private LoseView loseView;
        [Tooltip("Covers the change from the start screen to the level.")]
        [SerializeField] private UIScreen curtain;
        [Tooltip("How long the Level button's press animation plays before the curtain closes.")]
        [SerializeField] private float pressSeconds = 0.35f;

        [Header("Win")]
        [Tooltip("Played when a level is won, once the last bus has driven off. All of it is particle modules: tune it in its prefab.")]
        [SerializeField] private ParticleSystem winFireworks;
        [Tooltip("Seconds of fireworks before the win card shows; the last sparks still fall behind it.")]
        [SerializeField] private float winCardDelay = 2f;
        [Tooltip("The game's logo over the fireworks, off in the scene: its Animator pops it in each time it is switched on, and the win card takes its place.")]
        [SerializeField] private GameObject winLogo;

        [Header("Lose")]
        [Tooltip("Seconds the stopped board stays on screen before the lose card, so the player sees why: the full waiting seats, the late rider, the clock at zero.")]
        [SerializeField] private float loseCardDelay = 1.5f;

        private ILevelProvider _levels;
        private SaveStore _saves;
        private LevelSession _session;
        private GameState _state;
        private int _levelIndex;            // which catalog level is played
        private float _endingSeconds;
        private int _movesSaved;            // the session's move count at its last save; -1: not saved yet

        private void Awake()
        {
            // Phones default to 30 frames per second; walks and bus moves look much smoother at 60.
            Application.targetFrameRate = 60;
            _levels = new CatalogLevelProvider(levelCatalog);
            _saves = SaveStore.InDataFolder();
            startView.PlayPressed += OnPlayPressed;
            winView.NextPressed += OnNextPressed;
            loseView.RetryPressed += OnRetryPressed;
        }

        private void OnDestroy()
        {
            startView.PlayPressed -= OnPlayPressed;
            winView.NextPressed -= OnNextPressed;
            loseView.RetryPressed -= OnRetryPressed;
        }

        private void Start()
        {
            // Start, not Awake: by now every screen has run its own Awake.
            SavedGame saved = _saves.Load();
            if (saved != null)
            {
                // By id, so progress follows its level when the catalog changes. If the level is gone,
                // the one at the saved number takes its place.
                int index = _levels.IndexOf(saved.levelId);
                _levelIndex = index >= 0 ? index : (saved.levelNumber - 1) % _levels.Count;
            }
            LoadLevel(instant: true, saved);
        }

        // Leaving the app saves the time left too, not only the last move: iOS and Android always send
        // an app to the background before they close it, and a closed app gets no other warning.
        private void OnApplicationPause(bool paused)
        {
            if (paused && _state == GameState.Playing)
            {
                SaveProgress();
            }
        }

        private void OnApplicationQuit()
        {
            if (_state == GameState.Playing)
            {
                SaveProgress();
            }
        }

        private void Update()
        {
            if (_state == GameState.Playing)
            {
                _session.Tick(Time.deltaTime);
                hudView.SetTime(_session.RemainingSeconds);
                if (_session.State != SessionState.Playing)
                {
                    _state = GameState.Ending;
                    _endingSeconds = 0f;
                    SaveProgress();                 // decided: the next level, or this one again
                }
                else if (_session.Moves.Count != _movesSaved)
                {
                    SaveProgress();                 // every move, and the first frame of play (the clock runs)
                }
            }
            if (_state == GameState.Ending)
            {
                _endingSeconds += Time.deltaTime;
                if (PictureHasCaughtUp())
                {
                    ShowResult();
                }
                else if (_endingSeconds >= MaxEndingSeconds)
                {
                    Debug.LogWarning($"The level view did not finish within {MaxEndingSeconds} s; showing the result anyway.");
                    ShowResult();
                }
            }
        }

        // When to show the result, as in Bus Jam:
        // won: once the last bus has driven off;
        // no seats: the moment the last waiting seat is sat in on screen (anyone still walking
        // freezes where it is, but a rider in mid-jump lands first);
        // time up: at once, once a rider in mid-jump has landed.
        private bool PictureHasCaughtUp()
        {
            if (_session.State == SessionState.Won)
            {
                return !levelView.IsBusy;
            }
            if (_session.LoseReason == LoseReason.NoSeats)
            {
                return (levelView.WaitingSeatsLookFull && !levelView.SomeoneInMidAir) || !levelView.IsBusy;
            }
            return !levelView.SomeoneInMidAir;
        }

        // A session for the current level, waiting behind the start screen: as it was left when the save
        // says so, otherwise fresh.
        private void LoadLevel(bool instant, SavedGame saved = null)
        {
            Level level = _levels.Get(_levelIndex);
            _session = Resume(level, saved) ?? new LevelSession(level);
            _movesSaved = -1;
            _state = GameState.Start;
            levelView.Show(_session);

            startView.Show(LevelNumber, _session.RemainingSeconds, instant);
            hudView.Hide(instant: true);
            winView.Hide(instant);
            loseView.Hide(instant);
        }

        // The level as the save left it, or null to start it fresh: it was not left in the middle, or the
        // save no longer fits it (its file has changed since).
        private static LevelSession Resume(Level level, SavedGame saved)
        {
            if (saved == null || !saved.inProgress || saved.levelId != level.Id)
            {
                return null;
            }
            return LevelSession.TryResume(level, saved.Moves(), saved.remainingSeconds, out var session) ? session : null;
        }

        // Where the player is now. While playing: this level, its moves and the time left. Once decided
        // (even before the result shows, so quitting during the last animations still counts): after a
        // win the next level, after a loss this one again, both from the start.
        private void SaveProgress()
        {
            switch (_session.State)
            {
                case SessionState.Playing:
                    _saves.Write(SavedGame.LevelInPlay(_session.Level.Id, LevelNumber, _session.Moves, _session.RemainingSeconds));
                    break;
                case SessionState.Won:
                    _saves.Write(SavedGame.LevelToPlay(_levels.IdAt(NextLevelIndex), NextLevelIndex + 1));
                    break;
                default:
                    _saves.Write(SavedGame.LevelToPlay(_session.Level.Id, LevelNumber));
                    break;
            }
            _movesSaved = _session.Moves.Count;
        }

        // After the last level the list starts again from the first, and so does the number.
        private int NextLevelIndex => (_levelIndex + 1) % _levels.Count;

        // What the player sees: the level's own place in the catalog, so a level edited in the level
        // editor is the same number in the game.
        private int LevelNumber => _levelIndex + 1;

        private void OnPlayPressed()
        {
            if (_state != GameState.Start)
            {
                return;
            }
            StartCoroutine(EnterLevel());
        }

        // The button's press animation, then the curtain closes, the level is set up behind it and the
        // curtain opens; only then does the clock start. The state stays Start until that moment (the
        // start screen has locked its button), so no time passes before the player sees the board.
        private IEnumerator EnterLevel()
        {
            yield return new WaitForSeconds(pressSeconds);
            curtain.Show();
            yield return new WaitForSeconds(curtain.FadeSeconds);
            winFireworks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // a quick Next: no last sparks on the new board
            hudView.SetLevel(LevelNumber);
            hudView.SetTime(_session.RemainingSeconds);
            startView.Hide(instant: true);
            hudView.Show(instant: true);
            curtain.Hide();
            yield return new WaitForSeconds(curtain.FadeSeconds);
            _state = GameState.Playing;
            _session.Start();
        }

        private void ShowResult()
        {
            levelView.Freeze();
            if (_session.State == SessionState.Won)
            {
                hudView.Hide();
                _state = GameState.Won;
                _levelIndex = NextLevelIndex;
                winFireworks.Play();
                winLogo.SetActive(true);
                StartCoroutine(ShowWinCardAfterFireworks());
            }
            else
            {
                _state = GameState.Lost;
                StartCoroutine(ShowLoseCardAfterPause());
            }
        }

        // The board stays as it stopped, the clock still on screen, then the lose card.
        private IEnumerator ShowLoseCardAfterPause()
        {
            yield return new WaitForSeconds(loseCardDelay);
            hudView.Hide();
            loseView.Show(_session.LoseReason);
        }

        private IEnumerator ShowWinCardAfterFireworks()
        {
            yield return new WaitForSeconds(winCardDelay);
            winLogo.SetActive(false);
            winView.Show(LevelNumber);
        }

        private void OnNextPressed()
        {
            if (_state == GameState.Won)
            {
                LoadLevel(instant: false);
            }
        }

        private void OnRetryPressed()
        {
            if (_state == GameState.Lost)
            {
                LoadLevel(instant: false);
            }
        }
    }
}
