using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace HopOn.World
{
    /// <summary>
    /// The 3D street behind the start screen. Pieces of the start kit (road chunks, houses, trees,
    /// fences, lamps) stand in lanes along the road and drift toward the camera; a piece that passes
    /// the camera goes back to the far end, so the street never ends while the bus seems to drive.
    /// The far end melts into the start art's haze through Unity's own fog, which is on only while
    /// the town shows, so the board keeps its own look.
    /// While the town is on, the game camera is off, so the two never draw at the same time.
    /// It stands still until <see cref="StartMoving"/>: it only starts to move once the start screen's
    /// intro is over.
    /// On the first launch the street builds itself behind the logo (Build In): hidden from the start,
    /// it builds when the intro calls <see cref="Build"/>, once per launch (the scene opening again for
    /// Next or Retry finds it built), lane
    /// by lane, near to far: the ground fades in and the road's pieces come up out of it one after another,
    /// each fading in and settling with a small bounce, then the far city fades in, row by row, and last each
    /// house grows with a bounce, with a spray of drops and mist rising round it as it appears.
    /// The pieces are placed here because a street of about a hundred of them is not worth placing
    /// by hand; what they are and how they stand (models, lanes, gaps, turns, sizes, speed, fog) is
    /// all set in the Inspector.
    /// </summary>
    public sealed class StartTown : MonoBehaviour
    {
        private enum BuildStyle
        {
            Pop,    // grows from nothing with a bounce (houses, trees)
            Fade,   // fades in where it stands
            Rise,   // comes up out of the ground, a little past its place, and settles, fading in (the road)
        }

        [Serializable]
        private struct Lane
        {
            [Tooltip("From the middle of the road to the middle of the pieces.")]
            public float offset;
            public float minGap;
            public float maxGap;
            [Tooltip("Where the first piece starts, past the nearest distance: puts trees between houses.")]
            public float startOffset;
            [Tooltip("Extra start for the left side, so the two sides do not mirror each other.")]
            public float otherSideShift;
            [Tooltip("On both sides of the road. Off: one row on the road's middle line (the road chunks).")]
            public bool bothSides;
            [Tooltip("Turn the pieces so their front (the model's +Z) faces the road.")]
            public bool faceRoad;
            [Tooltip("A random turn around Y, in degrees, for trees and bushes.")]
            public float randomTurn;
            public float minScale;
            public float maxScale;
            [Tooltip("The models this lane picks from at random.")]
            public GameObject[] prefabs;
            [Tooltip("Build In only: when this lane starts to build, in seconds.")]
            public float buildDelay;
            [Tooltip("Build In only: how its pieces appear.")]
            public BuildStyle buildStyle;
            [Tooltip("Build In only: how much of the burst each piece gets (1: all of it, 0: none).")]
            public float burstAmount;
        }

        [Serializable]
        private struct BurstLayer
        {
            [Tooltip("An emit-only particle system that simulates in world space.")]
            public ParticleSystem system;
            [Tooltip("How many particles one piece gets at Burst Amount 1.")]
            public int count;
        }

        private sealed class Piece
        {
            public Transform Transform;
            public Quaternion BaseRotation;      // the model's own root rotation and size, kept under ours
            public Vector3 BaseScale;
            public int Lane;
            public float Side;
            public float Distance;
            public Vector3 Foot;                 // Build In only: where it stands, in world space
            public StartTown Town;

            /// <summary>Build In only: the moment this piece starts to appear.</summary>
            public void Burst()
            {
                Town.Burst(this);
            }
        }

        // Fades a renderer in: it shows its own copy of a see-through material, from clear to solid,
        // then gets its shared material back, which keeps the street batched.
        private sealed class Fader
        {
            private readonly Renderer _look;
            private readonly Material _own;
            private readonly Material _faded;

            public Fader(Renderer look, Material seeThrough)
            {
                _look = look;
                _own = look.sharedMaterial;
                _faded = new Material(seeThrough);
                Color clear = _faded.GetColor(BaseColor);
                clear.a = 0f;
                _faded.SetColor(BaseColor, clear);
                look.sharedMaterial = _faded;
            }

            public Tween FadeIn(float seconds)
            {
                return _faded.DOFade(1f, BaseColor, seconds).SetEase(Ease.InOutSine).OnComplete(Restore);
            }

            private void Restore()
            {
                _look.sharedMaterial = _own;
                UnityEngine.Object.Destroy(_faded);
            }
        }

        [Tooltip("The board's camera. Off while the town shows. Empty: Camera.main.")]
        [SerializeField] private Camera gameCamera;
        [SerializeField] private Lane[] lanes;
        [Tooltip("World units per second, once moving.")]
        [SerializeField] private float speed = 6f;
        [Tooltip("How long the street takes to reach full speed when it starts.")]
        [SerializeField] private float startSeconds = 0.8f;
        [Tooltip("The same number gives the same street: the boot scene and the start screen must match.")]
        [SerializeField] private int seed = 7;
        [Tooltip("A piece closer than this (behind the camera) goes back to the far end. The road lane " +
                 "needs farthest - nearest to be a whole number of road chunks.")]
        [SerializeField] private float nearest = -10f;
        [SerializeField] private float farthest = 110f;

        [Header("Build In: the boot scene builds the street behind the logo")]
        [Tooltip("Hidden from the start; builds when Build is called.")]
        [SerializeField] private bool buildIn;
        [Tooltip("Extra wait per metre of distance, so a lane builds from near to far.")]
        [SerializeField] private float buildPerMetre = 0.007f;
        [Tooltip("How long one piece takes to appear.")]
        [SerializeField] private float buildSeconds = 0.3f;
        [Tooltip("Rise pieces: how deep under the ground they start.")]
        [SerializeField] private float riseDepth = 0.5f;
        [Tooltip("Rise pieces: how long they take to come up and settle.")]
        [SerializeField] private float riseSeconds = 0.45f;
        [Tooltip("Rise pieces: how far past their place they come before they settle (0 none, about 1.7 a clear one).")]
        [SerializeField] private float riseOvershoot = 2f;
        [Tooltip("Rise pieces: the share of the rise they take to fade in, so the gaps between pieces at " +
                 "different heights are never solid (0: solid at once). Needs the Fade Material.")]
        [SerializeField, Range(0f, 1f)] private float riseFade = 0.7f;
        [Tooltip("A see-through copy of the pieces' material, for the Fade and Rise pieces.")]
        [SerializeField] private Material fadeMaterial;
        [Tooltip("The ground: hidden at the start as well, it fades in with the road.")]
        [SerializeField] private Renderer ground;
        [Tooltip("A see-through copy of the ground's material.")]
        [SerializeField] private Material groundFadeMaterial;
        [Tooltip("How long the ground takes to fade in.")]
        [SerializeField] private float groundSeconds = 0.5f;
        [Tooltip("The bounce at the end of a Pop: 0 none, about 1.7 a clear one.")]
        [SerializeField] private float buildOvershoot = 1.6f;
        [Tooltip("What bursts from a piece's foot as it appears: one entry per particle layer.")]
        [SerializeField] private BurstLayer[] burstLayers;
        [Tooltip("The far city's rows in front of the far view, nearest first: each fades in after the one before.")]
        [SerializeField] private SpriteRenderer[] farRows;
        [Tooltip("When the nearest far row starts to fade in.")]
        [SerializeField] private float farDelay = 1.2f;
        [Tooltip("The wait between one far row and the next.")]
        [SerializeField] private float farStep = 0.2f;
        [Tooltip("How long one far row takes to fade in.")]
        [SerializeField] private float farSeconds = 0.6f;

        [Header("Haze: Unity fog, only while the town shows")]
        [SerializeField] private Color fogColor = new Color(0.969f, 0.847f, 0.761f);
        [SerializeField] private float fogStart = 24f;
        [SerializeField] private float fogEnd = 100f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        // Build In happens once per launch: when the game scene opens again (Next, Retry) the street stands built.
        private static bool _built;

        private readonly List<Piece> _pieces = new List<Piece>();
        private System.Random _random;
        private bool _moving;
        private float _currentSpeed;

        /// <summary>Build In only: seconds from <see cref="Build"/> until the last piece stands.</summary>
        public float BuildLength { get; private set; }

        /// <summary>The street starts to drive by: it speeds up from standing still.</summary>
        public void StartMoving()
        {
            _moving = true;
        }

        /// <summary>Build In only: the hidden street builds itself now.</summary>
        public void Build()
        {
            _built = true;
            DOTween.Play(this);
        }

        private void Awake()
        {
            _random = new System.Random(seed);
            if (gameCamera == null)
            {
                gameCamera = Camera.main;
            }
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                Spawn(lane, 1f);
                if (lanes[lane].bothSides)
                {
                    Spawn(lane, -1f);
                }
            }
            if (buildIn && !_built)
            {
                BuildIn();
            }
        }

        // Every piece starts hidden (shrunk to nothing, or under the ground) and its tween waits, paused,
        // for Build; then each appears on its lane's turn, nearer pieces first. The street stands still
        // meanwhile, so nothing moves them back. As each piece starts to appear, its tween calls Burst.
        private void BuildIn()
        {
            if (ground != null && groundFadeMaterial != null)
            {
                new Fader(ground, groundFadeMaterial).FadeIn(groundSeconds).SetLink(ground.gameObject).SetId(this).Pause();
                BuildLength = groundSeconds;
            }
            foreach (Piece piece in _pieces)
            {
                Lane settings = lanes[piece.Lane];
                float delay = settings.buildDelay + Mathf.Max(piece.Distance, 0f) * buildPerMetre;
                Transform target = piece.Transform;
                piece.Foot = target.position;
                piece.Town = this;
                Tween tween;
                if (settings.buildStyle == BuildStyle.Rise)
                {
                    // Up from under the ground, a little past its place and back. It fades in on the way:
                    // the pieces are thin and open at their ends, so while one stands above or below its
                    // neighbour the gap between them shows, and it should never show solid.
                    BuildLength = Mathf.Max(BuildLength, delay + riseSeconds);
                    Vector3 end = target.localPosition;
                    target.localPosition = end + Vector3.down * riseDepth;
                    tween = target.DOLocalMove(end, riseSeconds).SetEase(Ease.OutBack, riseOvershoot);
                    if (riseFade > 0f && fadeMaterial != null)
                    {
                        new Fader(target.GetComponentInChildren<Renderer>(), fadeMaterial).FadeIn(riseSeconds * riseFade)
                            .SetDelay(delay).SetLink(target.gameObject).SetId(this).Pause();
                    }
                }
                else if (settings.buildStyle == BuildStyle.Fade)
                {
                    BuildLength = Mathf.Max(BuildLength, delay + buildSeconds);
                    tween = new Fader(target.GetComponentInChildren<Renderer>(), fadeMaterial).FadeIn(buildSeconds);
                }
                else
                {
                    BuildLength = Mathf.Max(BuildLength, delay + buildSeconds);
                    Vector3 end = target.localScale;
                    target.localScale = Vector3.zero;
                    tween = target.DOScale(end, buildSeconds).SetEase(Ease.OutBack, buildOvershoot);
                }
                tween.SetDelay(delay).SetLink(target.gameObject).OnStart(piece.Burst).SetId(this).Pause();
            }
            for (int row = 0; row < farRows.Length; row++)
            {
                SpriteRenderer sprite = farRows[row];
                float delay = farDelay + row * farStep;
                BuildLength = Mathf.Max(BuildLength, delay + farSeconds);
                Color clear = sprite.color;
                clear.a = 0f;
                sprite.color = clear;
                sprite.DOFade(1f, farSeconds).SetDelay(delay).SetEase(Ease.InOutSine).SetLink(sprite.gameObject)
                    .SetId(this).Pause();
            }
        }

        // Every layer bursts from the piece's foot, as much as its lane asks for. The systems simulate
        // in world space and are only emitted from here, so they need no Play and leave nothing behind.
        private void Burst(Piece piece)
        {
            float amount = lanes[piece.Lane].burstAmount;
            if (amount <= 0f || burstLayers == null)
            {
                return;
            }
            var at = new ParticleSystem.EmitParams { position = piece.Foot, applyShapeToPosition = true };
            foreach (BurstLayer layer in burstLayers)
            {
                if (layer.system != null)
                {
                    layer.system.Emit(at, Mathf.CeilToInt(layer.count * amount));
                }
            }
        }

        // The scene is saved with linear fog on, so a build keeps the fog shader variants; it is
        // switched off here for everything but the start screen.
        private void OnEnable()
        {
            if (gameCamera != null)
            {
                gameCamera.enabled = false;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }

        private void OnDisable()
        {
            RenderSettings.fog = false;
            if (gameCamera != null)
            {
                gameCamera.enabled = true;
            }
        }

        private void Update()
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _moving ? speed : 0f,
                speed / Mathf.Max(startSeconds, 0.01f) * Time.deltaTime);
            float step = _currentSpeed * Time.deltaTime;
            if (step <= 0f)
            {
                return;
            }
            float length = farthest - nearest;
            foreach (Piece piece in _pieces)
            {
                piece.Distance -= step;
                if (piece.Distance < nearest)
                {
                    piece.Distance += length;
                    Vary(piece);
                }
                Place(piece);
            }
        }

        // One side of one lane, from the nearest distance to the farthest.
        private void Spawn(int lane, float side)
        {
            Lane settings = lanes[lane];
            if (settings.prefabs == null || settings.prefabs.Length == 0)
            {
                Debug.LogWarning($"Start town lane {lane} has no models; it stays empty.", this);
                return;
            }
            bool regular = Mathf.Approximately(settings.minGap, settings.maxGap);
            float distance = nearest + settings.startOffset + (side < 0f ? settings.otherSideShift : 0f)
                + (regular ? 0f : Range(0f, settings.maxGap));
            while (distance < farthest)
            {
                GameObject prefab = settings.prefabs[_random.Next(settings.prefabs.Length)];
                var piece = new Piece
                {
                    Transform = Instantiate(prefab, transform).transform,
                    BaseRotation = prefab.transform.localRotation,
                    BaseScale = prefab.transform.localScale,
                    Lane = lane,
                    Side = side,
                    Distance = distance,
                };
                Vary(piece);
                _pieces.Add(piece);
                Place(piece);
                distance += Range(settings.minGap, settings.maxGap);
            }
        }

        // Turn and size: facing the road, plus a random turn and size where the lane asks for it.
        private void Vary(Piece piece)
        {
            Lane settings = lanes[piece.Lane];
            float turn = settings.faceRoad ? -piece.Side * 90f : 0f;
            turn += Range(-settings.randomTurn, settings.randomTurn);
            piece.Transform.localRotation = Quaternion.Euler(0f, turn, 0f) * piece.BaseRotation;
            float size = settings.maxScale > 0f ? Range(settings.minScale, settings.maxScale) : 1f;
            piece.Transform.localScale = piece.BaseScale * size;
        }

        // Seeded, so the same street comes out every time.
        private float Range(float min, float max)
        {
            return min + (float)_random.NextDouble() * (max - min);
        }

        private void Place(Piece piece)
        {
            Lane settings = lanes[piece.Lane];
            float x = settings.bothSides ? piece.Side * settings.offset : settings.offset;
            piece.Transform.localPosition = new Vector3(x, 0f, piece.Distance);
        }
    }
}
