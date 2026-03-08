// GazeInputManager.cs — Eye/gaze tracking input for D-Space interaction
// In the Daemon, operatives interact with D-Space elements by looking at them.
// Gaze selection is primary input for AR glasses — look at an object to select it.

using System;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Input
{
    public class GazeInputManager : SubsystemBase
    {
        public override string Name => "GazeInput";

        [Header("Gaze Settings")]
        [SerializeField] private float gazeDistance = 50f;
        [SerializeField] private float dwellTimeSeconds = 1.5f;     // Look at something for 1.5s to select
        [SerializeField] private float gazeRadius = 0.05f;          // Cone radius for gaze raycast
        [SerializeField] private LayerMask interactableLayers = -1;
        [SerializeField] private bool showReticle = true;
        [SerializeField] private bool useDwellSelection = true;

        private Camera arCamera;
        private GameObject currentTarget;
        private float dwellTimer;
        private Vector3 gazeHitPoint;

        public GameObject CurrentTarget => currentTarget;
        public Vector3 GazeHitPoint => gazeHitPoint;
        public float DwellProgress => useDwellSelection ? Mathf.Clamp01(dwellTimer / dwellTimeSeconds) : 0f;
        public bool IsGazing => currentTarget != null;

        public event Action<GameObject> OnGazeEnter;
        public event Action<GameObject> OnGazeExit;
        public event Action<GameObject> OnGazeSelect;   // Dwell complete or tap

        protected override Task OnInitialize()
        {
            arCamera = Manager.ARCamera;
            return Task.CompletedTask;
        }

        public override void Tick(float deltaTime)
        {
            if (arCamera == null) return;

            PerformGazeRaycast();
            UpdateDwell(deltaTime);
        }

        private void PerformGazeRaycast()
        {
            Ray gazeRay = new Ray(arCamera.transform.position, arCamera.transform.forward);

            if (Physics.SphereCast(gazeRay, gazeRadius, out RaycastHit hit,
                gazeDistance, interactableLayers))
            {
                gazeHitPoint = hit.point;
                var hitObject = hit.collider.gameObject;

                if (hitObject != currentTarget)
                {
                    if (currentTarget != null)
                    {
                        OnGazeExit?.Invoke(currentTarget);
                    }

                    currentTarget = hitObject;
                    dwellTimer = 0f;
                    OnGazeEnter?.Invoke(currentTarget);
                }
            }
            else
            {
                if (currentTarget != null)
                {
                    OnGazeExit?.Invoke(currentTarget);
                    currentTarget = null;
                    dwellTimer = 0f;
                }
                gazeHitPoint = arCamera.transform.position + arCamera.transform.forward * gazeDistance;
            }
        }

        private void UpdateDwell(float deltaTime)
        {
            if (!useDwellSelection || currentTarget == null) return;

            dwellTimer += deltaTime;
            if (dwellTimer >= dwellTimeSeconds)
            {
                OnGazeSelect?.Invoke(currentTarget);
                dwellTimer = 0f; // Reset to prevent repeated triggers
            }
        }

        /// <summary>
        /// Manual select (tap, button press, or voice command "select").
        /// </summary>
        public void ManualSelect()
        {
            if (currentTarget != null)
            {
                OnGazeSelect?.Invoke(currentTarget);
            }
        }
    }
}
