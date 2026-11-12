using UnityEngine;

namespace HopOn.World
{
    /// <summary>
    /// Places the camera so that the level being played (its board, the waiting seats and the bus at
    /// the stop, as points from <see cref="BoardLayout.FramedPoints"/>) fills the screen below the HUD,
    /// on any phone. A small board is seen from nearer, a
    /// big one from farther.
    /// The angle and the field of view stay as they are set on the camera in the scene: this only
    /// slides the camera nearer or farther along one line. The top of the level goes just below the
    /// lowest part of the HUD, read from the HUD itself, so the timer never covers the buses whatever
    /// the shape of the phone or the size of its notch.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BoardCamera : MonoBehaviour
    {
        // Each step halves the range the right distance can be in; 30 steps are far below a millimetre.
        private const int SearchSteps = 30;
        private const float FarthestDistance = 500f;

        [Tooltip("The lowest part of the HUD (the timer). The level is framed below it. Its canvas must be Screen Space - Overlay.")]
        [SerializeField] private RectTransform hudBottom;

        [Header("Space kept free (part of the screen)")]
        [Tooltip("Between the HUD and the top of the level.")]
        [SerializeField, Range(0f, 0.2f)] private float belowHud = 0.02f;
        [SerializeField, Range(0f, 0.2f)] private float bottom = 0.03f;
        [SerializeField, Range(0f, 0.2f)] private float sides = 0.02f;

        private readonly Vector3[] _hudCorners = new Vector3[4];
        private Camera _camera;
        private Vector3[] _points;
        private Vector3 _middle;                            // the middle of the points

        // What the last framing was worked out for; when any of it changes, the camera frames again.
        private float _framedTop;
        private float _framedAspect;
        private float _framedFieldOfView;
        private Quaternion _framedRotation;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        /// <summary>Frames these points: every one of them on screen, as near as possible.</summary>
        public void Frame(Vector3[] points)
        {
            _points = points;
            var around = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points)
            {
                around.Encapsulate(point);
            }
            _middle = around.center;
            FrameNow(TopKeptFree());
        }

        // A margin changed in the Inspector: frame again.
        private void OnValidate()
        {
            _framedAspect = 0f;
        }

        // Frames again only when something it depends on has changed: the shape of the screen, the
        // HUD's height (its safe area is set after the first frame), or the angle or field of view
        // while you try values in Play mode.
        private void LateUpdate()
        {
            if (_points == null)
            {
                return;
            }
            float top = TopKeptFree();
            if (top != _framedTop
                || _camera.aspect != _framedAspect
                || _camera.fieldOfView != _framedFieldOfView
                || transform.rotation != _framedRotation)
            {
                FrameNow(top);
            }
        }

        // The part of the screen above the level: everything down to the HUD's lowest edge, plus a gap.
        // On a Screen Space - Overlay canvas the corners are already in screen pixels.
        private float TopKeptFree()
        {
            if (hudBottom == null)
            {
                return belowHud;
            }
            hudBottom.GetWorldCorners(_hudCorners);         // bottom left, top left, top right, bottom right
            return 1f - _hudCorners[0].y / Screen.height + belowHud;
        }

        private void FrameNow(float top)
        {
            _framedTop = top;
            _framedAspect = _camera.aspect;
            _framedFieldOfView = _camera.fieldOfView;
            _framedRotation = transform.rotation;

            // The middle of the points goes to the middle of the free part of the screen. Moving along
            // this line keeps it there at any distance, so the distance is the only thing left to find.
            float freeMiddle = (bottom + 1f - top) * 0.5f;
            float tanHalfFieldOfView = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 line = -transform.forward + transform.up * ((0.5f - freeMiddle) * 2f * tanHalfFieldOfView);

            // Binary search for the nearest distance at which every point fits: try the middle of
            // the range; if it fits, the answer is that or nearer, if not, it is farther.
            float tooNear = 0f;
            float farEnough = FarthestDistance;
            for (int step = 0; step < SearchSteps; step++)
            {
                float distance = (tooNear + farEnough) * 0.5f;
                transform.position = _middle + line * distance;
                if (Fits(top))
                {
                    farEnough = distance;
                }
                else
                {
                    tooNear = distance;
                }
            }
            transform.position = _middle + line * farEnough;
        }

        // Every point is in front of the camera and inside the free part of the screen. Unity's own
        // projection does the maths, so this holds for any angle, field of view or screen.
        private bool Fits(float top)
        {
            foreach (Vector3 point in _points)
            {
                Vector3 onScreen = _camera.WorldToViewportPoint(point);
                if (onScreen.z <= 0f
                    || onScreen.x < sides || onScreen.x > 1f - sides
                    || onScreen.y < bottom || onScreen.y > 1f - top)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
