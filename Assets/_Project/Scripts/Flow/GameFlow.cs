using HopOn.Logic;
using HopOn.UI;
using UnityEngine;

namespace HopOn.Flow
{
    public enum GameState
    {
        Start,
        Playing,
        Won,
        Lost,
    }

    /// <summary>
    /// The game's flow: which state it is in and what each screen shows.
    /// Start -> Playing -> Won -> Start (next level), or Playing -> Lost -> Start (same level).
    /// It talks to whole screens (views), never to their buttons or texts: it tells a view what to
    /// show, and listens to the view's events for what the player pressed.
    /// Every transition checks the current state first, so a double tap, or a win and a time-up in
    /// the same frame, can never run a transition twice.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        [Header("Screens")]
        [SerializeField] private StartView startView;
        [SerializeField] private HudView hudView;
        [SerializeField] private WinView winView;
        [SerializeField] private LoseView loseView;

        [Header("Prototype (replaced by level data later)")]
        [SerializeField] private float levelSeconds = 90f;

        private GameState _state;
        private int _levelIndex;            // 0-based; the player sees _levelIndex + 1
        private LevelTimer _timer;

        public GameState State => _state;

        private int LevelNumber => _levelIndex + 1;

        private void Awake()
        {
            startView.PlayPressed += OnPlayPressed;
            winView.NextPressed += OnNextPressed;
            loseView.RetryPressed += OnRetryPressed;
        }

        private void OnDestroy()
        {
            startView.PlayPressed -= OnPlayPressed;
            winView.NextPressed -= OnNextPressed;
            loseView.RetryPressed -= OnRetryPressed;
            ReleaseTimer();
        }

        private void Start()
        {
            // Start, not Awake: by now every view has run its own Awake.
            EnterStart(instant: true);
        }

        private void Update()
        {
            if (_state != GameState.Playing)
            {
                return;
            }
            _timer.Tick(Time.deltaTime);
            hudView.SetTime(_timer.DisplaySeconds);

#if UNITY_EDITOR
            // TEMP until the gameplay exists: W wins the level. Editor only, never in a build.
            if (Input.GetKeyDown(KeyCode.W))
            {
                Win();
            }
#endif
        }

        // ---- States ----

        private void EnterStart(bool instant)
        {
            _state = GameState.Start;
            NewTimer(levelSeconds);

            startView.Show(LevelNumber, _timer.DisplaySeconds, instant);
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
            _state = GameState.Playing;
            hudView.SetLevel(LevelNumber);
            hudView.SetTime(_timer.DisplaySeconds);

            startView.Hide();
            hudView.Show();
            _timer.Start();
        }

        public void Win()
        {
            if (_state != GameState.Playing)
            {
                return;
            }
            _timer.Stop();
            _state = GameState.Won;
            hudView.Hide();
            winView.Show(LevelNumber + 1);
        }

        public void Lose(LoseReason reason)
        {
            if (_state != GameState.Playing)
            {
                return;
            }
            _timer.Stop();
            _state = GameState.Lost;
            hudView.Hide();
            loseView.Show(reason);
        }

        private void OnNextPressed()
        {
            if (_state != GameState.Won)
            {
                return;
            }
            _levelIndex++;
            EnterStart(instant: false);
        }

        private void OnRetryPressed()
        {
            if (_state != GameState.Lost)
            {
                return;
            }
            EnterStart(instant: false);
        }

        // ---- Timer ----

        private void NewTimer(float seconds)
        {
            ReleaseTimer();                 // the old level's timer must not keep a line to us
            _timer = new LevelTimer(seconds);
            _timer.Expired += OnTimeUp;
        }

        private void ReleaseTimer()
        {
            if (_timer == null)
            {
                return;
            }
            _timer.Expired -= OnTimeUp;
            _timer = null;
        }

        private void OnTimeUp()
        {
            Lose(LoseReason.TimeUp);
        }
    }
}
