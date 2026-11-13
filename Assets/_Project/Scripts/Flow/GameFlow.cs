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

        private ILevelProvider _levels;
        private LevelSession _session;
        private GameState _state;
        private int _levelIndex;            // which catalog level is played
        private int _levelNumber = 1;       // what the player sees; keeps counting after the last level
        private float _endingSeconds;

        private void Awake()
        {
            // Phones default to 30 frames per second; walks and bus moves look much smoother at 60.
            Application.targetFrameRate = 60;
            _levels = new CatalogLevelProvider(levelCatalog);
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
            LoadLevel(instant: true);
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
        // time up: at once.
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
            return true;
        }

        // A fresh session for the current level, waiting behind the start screen.
        private void LoadLevel(bool instant)
        {
            _session = new LevelSession(_levels.Get(_levelIndex));
            _state = GameState.Start;
            levelView.Show(_session);

            startView.Show(_levelNumber, _session.RemainingSeconds, instant);
            hudView.Hide(instant: true);
            winView.Hide(instant);
            loseView.Hide(instant);
        }

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
            hudView.SetLevel(_levelNumber);
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
            hudView.Hide();
            if (_session.State == SessionState.Won)
            {
                _state = GameState.Won;
                // After the last level the list starts again from the first, while the number keeps counting.
                _levelIndex = (_levelIndex + 1) % _levels.Count;
                _levelNumber++;
                winView.Show(_levelNumber);
            }
            else
            {
                _state = GameState.Lost;
                loseView.Show(_session.LoseReason);
            }
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
