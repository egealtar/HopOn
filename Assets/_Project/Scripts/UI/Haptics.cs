#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace HopOn.UI
{
    /// <summary>
    /// A light tap felt in the hand, like the phone's own controls give: iOS's light impact
    /// (Plugins/iOS/HopOnHaptics.mm) and Android's tick (its VIBRATE permission is in
    /// Plugins/Android/AndroidManifest.xml). Unity's Handheld.Vibrate is a long buzz, too strong for
    /// every tap. In the editor, and on a phone without a vibrator, it does nothing.
    /// </summary>
    public static class Haptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void HopOnHapticsLight();

        public static void Light()
        {
            HopOnHapticsLight();
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        private const int TickEffect = 2;               // VibrationEffect.EFFECT_TICK, from Android 10 (API 29)
        private const long OlderPhoneMilliseconds = 15;

        private static AndroidJavaObject _vibrator;
        private static AndroidJavaObject _tick;         // null before Android 10: a short pulse instead
        private static bool _unavailable;

        public static void Light()
        {
            if (_unavailable)
            {
                return;
            }
            try
            {
                if (_vibrator == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                        if (_vibrator == null || !_vibrator.Call<bool>("hasVibrator"))
                        {
                            _unavailable = true;
                            return;
                        }
                        if (version.GetStatic<int>("SDK_INT") >= 29)
                        {
                            using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                            {
                                _tick = effects.CallStatic<AndroidJavaObject>("createPredefined", TickEffect);
                            }
                        }
                    }
                }
                if (_tick != null)
                {
                    _vibrator.Call("vibrate", _tick);
                }
                else
                {
                    _vibrator.Call("vibrate", OlderPhoneMilliseconds);
                }
            }
            catch (AndroidJavaException e)
            {
                _unavailable = true;                    // never again: a tap must not fail over a vibration
                Debug.LogWarning("No vibration on this phone: " + e.Message);
            }
        }
#else
        public static void Light()
        {
        }
#endif
    }
}
