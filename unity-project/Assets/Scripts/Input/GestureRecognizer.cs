// GestureRecognizer.cs — Hand gesture recognition for D-Space interaction
// In the Daemon, operatives use hand gestures and haptic gloves.
// On modern AR glasses, hand tracking provides pinch, grab, and swipe gestures.

using System;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Input
{
    public class GestureRecognizer : SubsystemBase
    {
        public override string Name => "Gestures";

        [Header("Gesture Settings")]
        [SerializeField] private float pinchThreshold = 0.02f;    // meters between thumb and index
        [SerializeField] private float swipeMinDistance = 0.1f;
        [SerializeField] private float swipeMaxDuration = 0.5f;
        [SerializeField] private bool enableHandTracking = true;

        public event Action OnPinch;
        public event Action OnPinchRelease;
        public event Action<Vector3> OnSwipe;    // Direction
        public event Action OnGrab;
        public event Action OnRelease;
        public event Action OnPointUp;
        public event Action OnPointDown;

        private bool isPinching;
        private Vector3 swipeStartPos;
        private float swipeStartTime;

        protected override Task OnInitialize()
        {
            // Platform-specific hand tracking initialization
#if UNITY_ANDROID && !UNITY_EDITOR
            InitializeHandTracking();
#endif
            return Task.CompletedTask;
        }

        public override void Tick(float deltaTime)
        {
            if (!enableHandTracking) return;

            // Process touch/tap as fallback when hand tracking isn't available
            ProcessTouchInput();
        }

        /// <summary>
        /// Called by platform-specific hand tracking when a pinch is detected.
        /// </summary>
        public void OnPinchDetected()
        {
            if (!isPinching)
            {
                isPinching = true;
                OnPinch?.Invoke();
            }
        }

        public void OnPinchReleased()
        {
            if (isPinching)
            {
                isPinching = false;
                OnPinchRelease?.Invoke();
            }
        }

        public void OnSwipeDetected(Vector3 direction)
        {
            OnSwipe?.Invoke(direction.normalized);
        }

        private void ProcessTouchInput()
        {
            if (UnityEngine.Input.touchCount == 0) return;

            var touch = UnityEngine.Input.GetTouch(0);

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    swipeStartPos = touch.position;
                    swipeStartTime = Time.time;
                    break;

                case TouchPhase.Ended:
                    float duration = Time.time - swipeStartTime;
                    Vector3 swipeDelta = (Vector3)touch.position - swipeStartPos;
                    float distance = swipeDelta.magnitude;

                    if (distance > swipeMinDistance * Screen.dpi && duration < swipeMaxDuration)
                    {
                        // Swipe detected
                        OnSwipe?.Invoke(swipeDelta.normalized);
                    }
                    else if (duration < 0.3f)
                    {
                        // Tap = pinch equivalent
                        OnPinchDetected();
                        OnPinchReleased();
                    }
                    break;
            }
        }

        private void InitializeHandTracking()
        {
            // Platform-specific:
            // - Meta Quest: OVRHand tracking
            // - Android XR: ARCore hand tracking
            // - OpenXR: XR_EXT_hand_tracking extension
            Log("Hand tracking initialized (platform-specific implementation required).");
        }
    }
}
