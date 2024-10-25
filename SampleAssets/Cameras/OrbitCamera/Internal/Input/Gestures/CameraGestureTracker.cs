// Copyright 2019 Niantic, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;
using Niantic.Lightship.Maps.Linq;
using UnityEngine;
using UnityEngine.Assertions;

namespace Niantic.Lightship.Maps.SampleAssets.Cameras.OrbitCamera.Internal.Input.Gestures
{
    /// <summary>
    /// Camera Controller Gesture Tracker for
    /// Niantic-standard map camera interactions.
    /// </summary>
    internal class CameraGestureTracker : IScreenInputGesture
    {
        public bool IsNavigating;
        public bool IsTrueNorthFacing;

        private const float GroundClampCosThreshold = -0.1f;
        private static Plane _groundPlane = new(Vector3.up, 0.0f);

        private readonly Camera _raycastCamera;
        private readonly GameObject _focusObject;
        private readonly GestureSettings _settings;

        private readonly List<InputEvent> _transformationEvents = new();

        private Vector3 _lastSwipePosition;
        private int _lastSwipeFrame;

        // Potential first input for the double tap
        private InputEvent _firstZoomTap;
        private bool _firstTapEnded;

        // 2nd input, only not null during a double-tap-to-zoom
        private InputEvent _secondZoomTap;

        private Vector3 _initialWorldPinchPoint;
        private bool _isPinching = false;

        private bool _isCurrentlyZooming;
        private bool _wasRotatingLastFrame;
        private Vector3 _lastTouch0Position;
        private Vector3 _lastTouch1Position;
        private Vector3 _prevZoomPosition;
        private InputEvent _lastTouch0;
        private InputEvent _lastTouch1;
        private Vector2 _scrollDelta = Vector2.zero;

        public CameraGestureTracker(
            Camera raycastCamera,
            GameObject focusObject,
            GestureSettings settings)
        {
            _raycastCamera = raycastCamera;
            _focusObject = focusObject;
            _settings = settings;
            ZoomFraction = settings.DefaultZoom;
        }

        /// <summary>
        /// Normalized fraction (0.0 - 1.0) of where we are
        /// between fully zoomed in and fully zoomed out.
        /// </summary>
        public float ZoomFraction { get; set; }

        /// <summary>
        /// Current rotation, in degrees.
        /// </summary>
        public float RotationAngleDegrees { get; private set; }


        public Vector3 MaxCameraMovement;
        public Vector3 CameraMovement { get; set; }

        private bool IsCurrentlyRotating =>
            !_isCurrentlyZooming &&
            // Expects only one finger down
            _transformationEvents.Count == 1 &&
            _transformationEvents[0].Phase is InputPhase.Began or InputPhase.Held;

        /// <inheritdoc />
        public void ProcessEvent(InputEvent inputEvent)
        {
            _transformationEvents.Add(inputEvent);

            if (inputEvent.ScrollDelta.HasValue)
            {
                _scrollDelta += inputEvent.ScrollDelta.Value;
            }
        }

        /// <inheritdoc />
        public void PostProcessInput()
        {
            Assert.IsNotNull(_focusObject,
                $"{nameof(CameraGestureTracker)}.{nameof(_focusObject)} cannot be null");

            ProcessZoom();
            ProcessSwipe();

            _transformationEvents.Clear();
            _scrollDelta = Vector2.zero;
        }

        private static Vector3 ClampDirToGround(Vector3 dir)
        {
            if (dir.y > -Mathf.Epsilon)
            {
                dir.y = GroundClampCosThreshold;
            }

            return dir;
        }

        private void MouseZoom()
        {
            // Don't zoom the camera if the scroll is happening over the UI
            if (_transformationEvents.Any(e => IsTransformOverUI(e.Transform)))
            {
                return;
            }

            float scrollChange = Time.unscaledDeltaTime * _scrollDelta.y;
            ZoomFraction = Mathf.Clamp01(ZoomFraction + scrollChange * _settings.MouseScrollZoomSpeed);
        }

        // Returns true if currently zooming
        private bool TouchZoomAndRotate()
        {
            InputEvent touch0 = null;
            InputEvent touch1 = null;

            int touch0Id = _lastTouch0?.Transform.Id ?? int.MaxValue;
            int touch1Id = _lastTouch1?.Transform.Id ?? int.MaxValue;

            foreach (var touch in _transformationEvents)
            {
                int possibleTouchId = touch.Transform.Id;

                switch (touch.Phase)
                {
                    case InputPhase.Began when _lastTouch0 == null:
                        {
                            _lastTouch0 = touch;
                            _lastTouch0Position = _lastTouch0.Transform.Position;
                            touch0Id = possibleTouchId;
                            touch0 = _lastTouch0;
                            break;
                        }
                    case InputPhase.Began:
                        {
                            if (_lastTouch1 == null)
                            {
                                _lastTouch1 = touch;
                                _lastTouch1Position = _lastTouch1.Transform.Position;
                                touch1Id = possibleTouchId;
                                touch1 = _lastTouch1;
                            }

                            break;
                        }
                    case InputPhase.Ended:
                    case InputPhase.Canceled:
                        {
                            if (_lastTouch0 != null && possibleTouchId == touch0Id)
                            {
                                // Done zooming for now
                                _lastTouch0 = null;
                            }

                            if (_lastTouch1 != null && possibleTouchId == touch1Id)
                            {
                                _lastTouch1 = null;
                            }
                            _isPinching = false;
                            break;
                        }
                    case InputPhase.Held when _lastTouch0 != null && possibleTouchId == touch0Id:
                        {
                            touch0 = touch;
                            break;
                        }
                    case InputPhase.Held:
                        {
                            if (_lastTouch1 != null && possibleTouchId == touch1Id)
                            {
                                touch1 = touch;
                            }

                            break;
                        }
                    case InputPhase.Hovered:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // If we didn't find both touches, keep
            // tracking any stray touch's position.
            if (touch0 == null || touch1 == null)
            {
                return false;
            }

            var touch0Transform = touch0.Transform;
            var touch1Transform = touch1.Transform;

            // If both touches are on UI, filter it out, but if only 1 is over UI allow it.
            if (IsTransformOverUI(touch0Transform) && IsTransformOverUI(touch1Transform))
            {
                return false;
            }

            // Calculate the final pinch zoom difference
            var touch0Pos = touch0Transform.Position;
            var touch1Pos = touch1Transform.Position;

            var lastPos0 = _lastTouch0Position;
            var lastPos1 = _lastTouch1Position;

            float screenWidthInches = Screen.width / Screen.dpi;
            float curDist = (touch1Pos - touch0Pos).magnitude;
            float prevDist = (lastPos1 - lastPos0).magnitude;
            float pinchChange = (prevDist - curDist) * screenWidthInches;


            ZoomFraction = Mathf.Clamp01(ZoomFraction + pinchChange * _settings.TouchPinchZoomSpeed);

            if (!IsNavigating && ZoomFraction > 0.3f && ZoomFraction <1f)
            {
                Vector2 pinchMidPoint = (touch0Pos + touch1Pos) / 2;
                pinchMidPoint.x *= Screen.width;  // Convert normalized X to pixel X
                pinchMidPoint.y *= Screen.height;

                if (!_isPinching)
                {
                    _isPinching = true;

                    Debug.Log($"Pinch mid point: {pinchMidPoint}");
                    Ray ray = _raycastCamera.ScreenPointToRay(new Vector3(pinchMidPoint.x, pinchMidPoint.y, 0));  // Screen midpoint

                    Plane groundPlane = new Plane(Vector3.up, new Vector3(0, _focusObject.transform.position.y, 0));  // Assuming ground plane

                    float enter;
                    if (groundPlane.Raycast(ray, out enter))
                    {
                        // Store the initial world pinch point
                        _initialWorldPinchPoint = ray.GetPoint(enter);
                        Debug.Log($"Captured initial pinch point: {_initialWorldPinchPoint}");
                    }
                }

                Vector3 directionToPinch = (_initialWorldPinchPoint - _raycastCamera.transform.position).normalized;

                // Adjust the movement magnitude
                float movementMagnitude = Mathf.Abs(pinchChange) * _settings.TouchPinchZoomSpeed * 2000f;

                // Apply movement in the correct direction, based on pinchChange (invert if zooming in)
                if (pinchChange > 0)  // Zooming in
                {
                    CameraMovement += -directionToPinch * movementMagnitude;
                    
                }
                else  // Zooming out
                {
                    CameraMovement += directionToPinch * movementMagnitude;
                    CameraMovement = new Vector3(
                        CameraMovement.x,
                        Mathf.Min(CameraMovement.y, MaxCameraMovement.y),
                        CameraMovement.z
                        );
                }
            }


            // Now check if there is any rotation in this zoom
            var lastDirection = (_lastTouch1Position - _lastTouch0Position).normalized;
            var thisDirection = (touch1Pos - touch0Pos).normalized;
            float determinant = lastDirection.x * thisDirection.y - lastDirection.y * thisDirection.x;

            RotateByDirections(lastDirection, thisDirection, determinant);

            _lastTouch0Position = touch0Pos;
            _lastTouch1Position = touch1Pos;

            return true;
        }

        private void ProcessZoom()
        {
            bool wasZooming = _isCurrentlyZooming;
            _isCurrentlyZooming = false;

            if (!Mathf.Approximately(0.0f, _scrollDelta.y))
            {
                MouseZoom();
                _isCurrentlyZooming = true;
            }

            if (TouchZoomAndRotate())
            {
                _isCurrentlyZooming = true;
            }
            else if (wasZooming)
            {
                _lastTouch0 = null;
                _lastTouch1 = null;
            }

            if (_settings.DoubleTapZoomEnabled && DoubleTapZoom())
            {
                _isCurrentlyZooming = true;
            }
            else if (_isCurrentlyZooming)
            {
                // 2 finger zoom cancels double tap zoom
                ResetDoubleTapZoom();
            }
        }

        private void ResetDoubleTapZoom()
        {
            _firstZoomTap = null;
            _secondZoomTap = null;
            _firstTapEnded = false;
        }

        // Returns true while zooming
        private bool DoubleTapZoom()
        {
            // If the current possible double tap is timed out
            // without the second tap occuring, clear out the first tap.
            if (_secondZoomTap == null && _firstZoomTap?.Time < Time.unscaledTime - _settings.DoubleTapMaxTime)
            {
                ResetDoubleTapZoom();
            }

            // Find potential start if present
            if (_firstZoomTap == null)
            {
                _firstZoomTap = _transformationEvents.FirstOrDefault(
                    e => e.Phase == InputPhase.Began && !IsTransformOverUI(e.Transform));

                _firstTapEnded = false;

                return false;
            }

            // Has start tap timed out?
            if (_secondZoomTap == null && Time.unscaledTime - _firstZoomTap?.Time > _settings.DoubleTapMaxTime)
            {
                ResetDoubleTapZoom();
                return false;
            }

            // Has the first tap ended?
            if (_firstZoomTap != null && _firstTapEnded == false)
            {
                _firstTapEnded = _transformationEvents.Any(e =>
                    e.Source == _firstZoomTap.Source &&
                    e.Phase is InputPhase.Ended or InputPhase.Canceled);
            }

            switch (_firstTapEnded)
            {
                // At this point we know we have a valid first tap, see if we have a 2nd.
                case true when _secondZoomTap == null:
                    {
                        _secondZoomTap = _transformationEvents.FirstOrDefault(
                            e => e.Phase == InputPhase.Began && !IsTransformOverUI(e.Transform));

                        if (_secondZoomTap != null)
                        {
                            _secondZoomTap.Consume(this);
                            _prevZoomPosition = _secondZoomTap.Transform.Position;
                        }

                        return true;
                    }
                case true when _firstZoomTap != null && _secondZoomTap != null:
                    {
                        // Zooming detected
                        var duplicateBeganZoomEvent = _transformationEvents.FirstOrDefault(e =>
                            e.Transform.Id == _secondZoomTap.Transform.Id &&
                            e.Phase == InputPhase.Began);

                        if (duplicateBeganZoomEvent != null)
                        {
                            duplicateBeganZoomEvent.Consume(this);
                            return true;
                        }

                        var currentZoomEvent = _transformationEvents.FirstOrDefault(e =>
                            e.Transform.Id == _secondZoomTap.Transform.Id &&
                            e.Phase == InputPhase.Held);

                        if (currentZoomEvent == null)
                        {
                            ResetDoubleTapZoom();
                            return false;
                        }

                        currentZoomEvent.Consume(this);
                        var newZoomPosition = currentZoomEvent.Transform.Position;

                        if (currentZoomEvent.Phase == InputPhase.Held)
                        {
                            float y = (newZoomPosition - _prevZoomPosition).y;
                            ZoomFraction = Mathf.Clamp01(ZoomFraction - y * _settings.DoubleTapZoomSpeed);
                        }

                        _prevZoomPosition = newZoomPosition;

                        return true;
                    }
                default:
                    return false;
            }
        }

        private static bool IsTransformOverUI(in TransformData inputTransform)
        {
            var inputPos = new Vector2(
                inputTransform.Position[0] * Screen.width,
                inputTransform.Position[1] * Screen.height);

            return PlatformAgnosticInput.IsOverUIObject(inputPos);
        }

        /*
        private void ProcessSwipe()
        {
            bool currentlyRotating = IsCurrentlyRotating;

            if (currentlyRotating)
            {
                var touch = _transformationEvents[0];
                var inputTransform = touch.Transform;

                if (!_wasRotatingLastFrame)
                {
                    // We can only start if begin and not over UI
                    if (touch.Phase != InputPhase.Began || IsTransformOverUI(inputTransform))
                    {
                        return;
                    }
                }

                var swipePosition = inputTransform.Position;

                if (_lastSwipeFrame != Time.frameCount - 1)
                {
                    // No delta on first frame, fix it by
                    // getting the first position from Began.
                    _lastSwipePosition = swipePosition;
                }

                var touchRay = _raycastCamera.ViewportPointToRay(swipePosition);
                var lastTouchRay = _raycastCamera.ViewportPointToRay(_lastSwipePosition);

                touchRay.direction = ClampDirToGround(touchRay.direction);
                lastTouchRay.direction = ClampDirToGround(lastTouchRay.direction);

                _groundPlane.Raycast(touchRay, out float rayDist);
                _groundPlane.Raycast(lastTouchRay, out float lastRayDist);

                var groundTouch = touchRay.GetPoint(rayDist);
                var lastGroundTouch = lastTouchRay.GetPoint(lastRayDist);

                var position = _focusObject.transform.position;
                var centerToLast = (lastGroundTouch - position).normalized;
                var centerToThis = (groundTouch - position).normalized;
                float determinant = centerToLast.x * centerToThis.z - centerToLast.z * centerToThis.x;

                RotateByDirections(centerToLast, centerToThis, determinant);

                _lastSwipePosition = swipePosition;
                _lastSwipeFrame = Time.frameCount;
            }

            _wasRotatingLastFrame = currentlyRotating;
        }
        */
        // Rotates from directionA toward directionB
        private void RotateByDirections(Vector3 directionA, Vector3 directionB, float direction)
        {
            if (Mathf.Approximately(directionA.magnitude, 0.0f) ||
                Mathf.Approximately(directionB.magnitude, 0.0f))
            {
                return;
            }

            float dot = Vector3.Dot(directionA, directionB);

            if (!Mathf.Approximately(1.0f, dot))
            {
                float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;

                if (direction < 0.0f)
                {
                    angle *= -1.0f;
                }

                RotationAngleDegrees += angle;
            }
        }


        private void ProcessSwipe()
        {
            bool currentlyMoving = IsCurrentlyRotating;  // Keeping the "IsCurrentlyRotating" logic for detecting swipe

            if (currentlyMoving)
            {
                var touch = _transformationEvents[0];
                var inputTransform = touch.Transform;

                if (!_wasRotatingLastFrame)
                {
                    // Start only if phase is 'Began' and not over UI
                    if (touch.Phase != InputPhase.Began || IsTransformOverUI(inputTransform))
                    {
                        return;
                    }
                }

                var swipePosition = inputTransform.Position;

                if (_lastSwipeFrame != Time.frameCount - 1)
                {
                    // No delta on the first frame, fix by using initial swipe position
                    _lastSwipePosition = swipePosition;
                }


                if (IsNavigating && !IsTrueNorthFacing)
                {
                    //For rotation
                    var touchRay = _raycastCamera.ViewportPointToRay(swipePosition);
                    var lastTouchRay = _raycastCamera.ViewportPointToRay(_lastSwipePosition);

                    touchRay.direction = ClampDirToGround(touchRay.direction);
                    lastTouchRay.direction = ClampDirToGround(lastTouchRay.direction);

                    _groundPlane.Raycast(touchRay, out float rayDist);
                    _groundPlane.Raycast(lastTouchRay, out float lastRayDist);

                    var groundTouch = touchRay.GetPoint(rayDist);
                    var lastGroundTouch = lastTouchRay.GetPoint(lastRayDist);

                    var position = _focusObject.transform.position;
                    var centerToLast = (lastGroundTouch - position).normalized;
                    var centerToThis = (groundTouch - position).normalized;
                    float determinant = centerToLast.x * centerToThis.z - centerToLast.z * centerToThis.x;

                    RotateByDirections(centerToLast, centerToThis, determinant);
                }
                else
                {
                    // Calculate swipe delta (movement on screen)
                    Vector2 swipeDelta = swipePosition - _lastSwipePosition;

                    // Adjust camera movement speed (tweak to your preference)
                    float movementSpeed = Mathf.Clamp(ZoomFraction * 1000f, 200f, 1000f);

                    // Translate the camera based on swipe delta
                    MoveCamera(swipeDelta, movementSpeed);
                }

                _lastSwipePosition = swipePosition;
                _lastSwipeFrame = Time.frameCount;
            }

            _wasRotatingLastFrame = currentlyMoving;
        }

        private void MoveCamera(Vector2 swipeDelta, float movementSpeed)
        {
            // Get the camera's forward and right vectors for world-space movement
            Vector3 forwardMovement = _raycastCamera.transform.forward; // Forward direction in world space
            Vector3 rightMovement = _raycastCamera.transform.right;     // Right direction in world space

            // We don't want to move the camera vertically based on the forward vector (only on x and z plane),
            // so set the y-component of forward and right to zero
            forwardMovement.y = 0f;
            rightMovement.y = 0f;

            // Normalize the vectors to avoid scaling issues
            forwardMovement.Normalize();
            rightMovement.Normalize();

            // Calculate camera movement based on swipe direction and the camera's current orientation
            Vector3 cameraMovement = (rightMovement * -swipeDelta.x + forwardMovement * -swipeDelta.y) * movementSpeed;

            CameraMovement += cameraMovement;
        }

    }
}
