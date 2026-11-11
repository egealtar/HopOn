using UnityEngine;

namespace HopOn.UI
{
    /// <summary>
    /// Fits this RectTransform to the screen's safe area (notch, camera hole, home indicator,
    /// rounded corners), so nothing the player must see or tap sits under them.
    /// Put it on a full-screen child of the Canvas and place the screens inside it.
    /// Backgrounds that should reach the screen edges stay outside it.
    /// </summary>
    // ExecuteAlways: also runs outside Play mode, so the editor and the Device Simulator show the
    // layout exactly as a phone will. Side effect: the scene saves the anchors of whichever device
    // was last previewed; harmless, they are recalculated on every launch.
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _appliedArea;
        private Vector2Int _appliedScreen;

        private void OnEnable()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        // The safe area can change at runtime: in the editor's Device Simulator, or on a device
        // that rotates. The check is two cheap comparisons, so polling it every frame is fine.
        private void Update()
        {
            if (Screen.safeArea != _appliedArea || Screen.width != _appliedScreen.x || Screen.height != _appliedScreen.y)
            {
                Apply();
            }
        }

        private void Apply()
        {
            _appliedArea = Screen.safeArea;
            _appliedScreen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            // Anchors are fractions (0..1) of the parent, so the safe rectangle in pixels is
            // turned into fractions of the screen.
            Vector2 min = _appliedArea.position;
            Vector2 max = _appliedArea.position + _appliedArea.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
