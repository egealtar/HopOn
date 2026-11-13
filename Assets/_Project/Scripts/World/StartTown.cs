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
    /// It stands still until <see cref="StartMoving"/>: the boot scene shows the same street (the same
    /// prefab and seed, so the same houses in the same places) and it only starts to move once the
    /// start screen's intro is over.
    /// In the boot scene the street builds itself first (Build In): lane by lane, near to far, each
    /// piece rising out of the ground or growing with a bounce, before the logo comes.
    /// The pieces are placed here because a street of about a hundred of them is not worth placing
    /// by hand; what they are and how they stand (models, lanes, gaps, turns, sizes, speed, fog) is
    /// all set in the Inspector.
    /// </summary>
    public sealed class StartTown : MonoBehaviour
    {
        private enum BuildStyle
        {
            Pop,    // grows from nothing with a bounce (houses, trees)
            Rise,   // comes up out of the ground (the road)
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
        }

        private sealed class Piece
        {
            public Transform Transform;
            public Quaternion BaseRotation;      // the model's own root rotation and size, kept under ours
            public Vector3 BaseScale;
            public int Lane;
            public float Side;
            public float Distance;
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

        [Header("Build In: the boot scene builds the street before the logo comes")]
        [SerializeField] private bool buildIn;
        [Tooltip("Extra wait per metre of distance, so a lane builds from near to far.")]
        [SerializeField] private float buildPerMetre = 0.007f;
        [Tooltip("How long one piece takes to appear.")]
        [SerializeField] private float buildSeconds = 0.3f;
        [Tooltip("How deep under the ground a rising piece starts.")]
        [SerializeField] private float buildDepth = 0.6f;
        [Tooltip("The bounce at the end: 0 none, about 1.7 a clear one.")]
        [SerializeField] private float buildOvershoot = 1.6f;

        [Header("Haze: Unity fog, only while the town shows")]
        [SerializeField] private Color fogColor = new Color(0.969f, 0.847f, 0.761f);
        [SerializeField] private float fogStart = 24f;
        [SerializeField] private float fogEnd = 100f;

        private readonly List<Piece> _pieces = new List<Piece>();
        private System.Random _random;
        private bool _moving;
        private float _currentSpeed;

        /// <summary>The street starts to drive by: it speeds up from standing still.</summary>
        public void StartMoving()
        {
            _moving = true;
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
            if (buildIn)
            {
                BuildIn();
            }
        }

        // Every piece starts hidden (shrunk to nothing, or under the ground) and appears on its lane's
        // turn, nearer pieces first. The street stands still meanwhile, so nothing moves them back.
        private void BuildIn()
        {
            foreach (Piece piece in _pieces)
            {
                Lane settings = lanes[piece.Lane];
                float delay = settings.buildDelay + Mathf.Max(piece.Distance, 0f) * buildPerMetre;
                Transform target = piece.Transform;
                Tween tween;
                if (settings.buildStyle == BuildStyle.Rise)
                {
                    Vector3 end = target.localPosition;
                    target.localPosition = end + Vector3.down * buildDepth;
                    tween = target.DOLocalMove(end, buildSeconds);
                }
                else
                {
                    Vector3 end = target.localScale;
                    target.localScale = Vector3.zero;
                    tween = target.DOScale(end, buildSeconds);
                }
                tween.SetDelay(delay).SetEase(Ease.OutBack, buildOvershoot).SetLink(target.gameObject);
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
