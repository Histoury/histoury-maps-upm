// Copyright 2019 Niantic, Inc. All Rights Reserved.

using System;
using Niantic.Lightship.Maps.SampleAssets.Cameras.OrbitCamera.Internal;
using Niantic.Lightship.Maps.SampleAssets.Cameras.OrbitCamera.Internal.Input;
using Niantic.Lightship.Maps.SampleAssets.Cameras.OrbitCamera.Internal.Input.Gestures;
using Niantic.Lightship.Maps.SampleAssets.Cameras.OrbitCamera.Internal.ZoomCurves;
using UnityEngine;
using System.Collections;


namespace Niantic.Lightship.Maps.SampleAssets.Cameras.OrbitCamera
{
    /// <summary>
    /// Camera controller for Niantic-standard map camera
    /// interactions, similar to the Pokemon GO camera.
    /// </summary>
    public class OrbitCameraController : MonoBehaviour
    {

        public bool IsNavigating;
        [SerializeField] private float _zoomFraction;
        [SerializeField]
        private float _minimumZoomDistance = 23f;

        [SerializeField]
        private float _maximumZoomDistance = 99f;

        [SerializeField]
        private float _minimumPitchDegrees = 20.0f;

        [SerializeField]
        private float _maximumPitchDegrees = 60.0f;

        [SerializeField]
        private float _verticalFocusOffset = 10.0f;

        [SerializeField]
        private GestureSettings _gestureSettings;

        [HideInInspector]
        [SerializeField]
        private Camera _camera;

        [SerializeField]
        private GameObject _focusObject;

        private InputService _inputService;
        private CameraGestureTracker _gestureTracker;
        private IZoomCurveEvaluator _zoomCurveEvaluator;

        [SerializeField] float _heading;
        public float TrueHeading;
        public bool _isFacingOn;
        [SerializeField] Coroutine _rotateCoroutine;
        [SerializeField] Coroutine _rotateToTrueNorthFacing;
        private float _currentAnimatingValueForTrueNorthFacing;

        [SerializeField] Coroutine _returnToOriginalPosCoroutine;
        [SerializeField] bool _isTransitioning;

        [SerializeField] float _currentTopdownZoomDistance;
        public void Awake()
        {
            _gestureTracker = new CameraGestureTracker(_camera, _focusObject, _gestureSettings);
            _inputService = new InputService(_gestureTracker);

            _gestureTracker.IsNavigating = false;

            _zoomCurveEvaluator = new ZoomCurveEvaluator(
                _minimumZoomDistance,
                _maximumZoomDistance,
                _minimumPitchDegrees,
                _maximumPitchDegrees,
                _verticalFocusOffset);

            StartCoroutine(MoveBackToOrigin(true));
        }

        public void SetIsNavigating(bool value)
        {
            if (IsNavigating != value)
            {
                IsNavigating = value;
                _gestureTracker.IsNavigating = value;

                if (IsNavigating)
                {
                    var zoomCurveEvaluator = new ZoomCurveEvaluator(
                        _minimumZoomDistance,
                        _maximumZoomDistance,
                        _minimumPitchDegrees,
                        _maximumPitchDegrees,
                        _verticalFocusOffset);
                    _zoomCurveEvaluator = zoomCurveEvaluator;

                    if (_returnToOriginalPosCoroutine != null)
                    {
                        StopCoroutine(_returnToOriginalPosCoroutine);
                        _returnToOriginalPosCoroutine = null;
                    }
                }
                else
                {
                    if (_rotateCoroutine != null)
                    {
                        StopCoroutine(_rotateCoroutine);
                        _rotateCoroutine = null;
                    }
                    if (_rotateToTrueNorthFacing != null)
                    {
                        StopCoroutine(_rotateToTrueNorthFacing);
                        _rotateToTrueNorthFacing = null;
                    }    

                    var zoomCurveEvaluator = new ZoomCurveEvaluator(
                        550f,
                        _maximumZoomDistance,
                        60f,
                        _maximumPitchDegrees,
                        _verticalFocusOffset);
                    _zoomCurveEvaluator = zoomCurveEvaluator;

                    _currentTopdownZoomDistance = 1000f;
                }

                StartCoroutine(StartTransition());
            }


        }

        IEnumerator StartTransition()
        {
            _isTransitioning = true;
            float transitionDuration = 1f;
            float elapsedTime = 0f;

            if (IsNavigating)
            {
                float rotationAngleDegrees = _gestureTracker.RotationAngleDegrees;

                /*
                if (_isFacingOn)
                {
                    rotationAngleDegrees += Input.compass.trueHeading;
                }
                */

                rotationAngleDegrees += _heading;
                float rotationAngleRadians = Mathf.Deg2Rad * rotationAngleDegrees;

                _gestureTracker.ZoomFraction = 0.3f;
                float zoomFraction = _gestureTracker.ZoomFraction;

                float distance = _zoomCurveEvaluator.GetDistanceFromZoomFraction(zoomFraction);
                float elevMeters = _zoomCurveEvaluator.GetElevationFromDistance(distance);
                float pitchDegrees = _zoomCurveEvaluator.GetAngleFromDistance(distance);

                float x = -distance * Mathf.Sin(rotationAngleRadians);
                float z = -distance * Mathf.Cos(rotationAngleRadians);
                var offsetPos = new Vector3(x, elevMeters, z);

                Vector3 targetPos = _focusObject.transform.position + offsetPos;
                Quaternion targetRot = Quaternion.Euler(pitchDegrees, rotationAngleDegrees, 0.0f);

                Vector3 currentCamPos = _camera.transform.position;
                Quaternion currentCamRot = _camera.transform.rotation;


                Debug.Log($"Current pos: {currentCamPos}, target pos: {targetPos}");
                while (elapsedTime < transitionDuration)
                {
                    elapsedTime += Time.deltaTime;
                    float lerpFactor = Mathf.Clamp01(elapsedTime / transitionDuration);
                    _gestureTracker.ZoomFraction = Mathf.Lerp(1f, 0.3f, lerpFactor);
                    float scaleMultiplier = IsNavigating == true ? 10f : 30f;
                    _focusObject.transform.localScale = Vector3.one * Mathf.Clamp(_gestureTracker.ZoomFraction, 0.3f, 1f) * scaleMultiplier;
                    _camera.transform.position = Vector3.Lerp(currentCamPos, targetPos, lerpFactor);
                    _camera.transform.rotation = Quaternion.Slerp(currentCamRot, targetRot, lerpFactor);

                    yield return null;
                }

                _camera.transform.position = targetPos;
                _camera.transform.rotation = targetRot;
            }
            else
            {
                float rotationAngleDegrees = _gestureTracker.RotationAngleDegrees;
                rotationAngleDegrees += _heading;
                float rotationAngleRadians = Mathf.Deg2Rad * rotationAngleDegrees;


                //Calculate MinGesture
                float zoomFraction = _gestureTracker.ZoomFraction = 0.3f;

                float distance = _currentTopdownZoomDistance;// _zoomCurveEvaluator.GetDistanceFromZoomFraction(zoomFraction);
                float elevMeters = _zoomCurveEvaluator.GetElevationFromDistance(distance);
                float pitchDegrees = _zoomCurveEvaluator.GetAngleFromDistance(distance); //60;

                float x = -distance * Mathf.Sin(rotationAngleRadians);
                float z = -distance * Mathf.Cos(rotationAngleRadians);
                var offsetPos = new Vector3(x, elevMeters, z);
                _gestureTracker.MinCameraMovement = new Vector3(x, 0, z) + _focusObject.transform.position;

                Debug.Log($"Min {_gestureTracker.MinCameraMovement}");
                zoomFraction = _gestureTracker.ZoomFraction = 1f;

                distance = _currentTopdownZoomDistance;// _zoomCurveEvaluator.GetDistanceFromZoomFraction(zoomFraction);
                elevMeters = _zoomCurveEvaluator.GetElevationFromDistance(distance);
                pitchDegrees = _zoomCurveEvaluator.GetAngleFromDistance(distance); //60;

                x = -distance * Mathf.Sin(rotationAngleRadians);
                z = -distance * Mathf.Cos(rotationAngleRadians);
                offsetPos = new Vector3(x, elevMeters, z);

                Vector3 targetPos = _focusObject.transform.position + offsetPos;// + _gestureTracker.CameraMovement;
                Quaternion targetRot = Quaternion.Euler(pitchDegrees, rotationAngleDegrees, 0.0f);

                //For update function to correctly update position dynamically
                _gestureTracker.MaxCameraMovement = _gestureTracker.CameraMovement = new Vector3(x, 0, z) + _focusObject.transform.position;
                Debug.Log($"Min {_gestureTracker.MaxCameraMovement}");


                Vector3 currentCamPos = _camera.transform.position;
                Quaternion currentCamRot = _camera.transform.rotation;

                elapsedTime = 0f;
                while (elapsedTime < transitionDuration)
                {
                    elapsedTime += Time.deltaTime;
                    float lerpFactor = Mathf.Clamp01(elapsedTime / transitionDuration);
                    _gestureTracker.ZoomFraction = Mathf.Lerp(0.3f, 1f, lerpFactor);
                    float scaleMultiplier = IsNavigating == true ? 10f : 30f;
                    _focusObject.transform.localScale = Vector3.one * Mathf.Clamp(_gestureTracker.ZoomFraction, 0.3f, 1f)* scaleMultiplier;

                    _camera.transform.position = Vector3.Lerp(currentCamPos, targetPos, lerpFactor);
                    _camera.transform.rotation = Quaternion.Slerp(currentCamRot, targetRot, lerpFactor);

                    yield return null;
                }

                _camera.transform.position = targetPos;
                _camera.transform.rotation = targetRot;
            }

            _isTransitioning = false;
        }
        public void SetFacing(bool value)
        {
            _isFacingOn = value;
            _gestureTracker.IsTrueNorthFacing = value;
    
            if (value)
            {
                if (_rotateCoroutine != null)
                {
                    StopCoroutine(_rotateCoroutine);
                    _rotateCoroutine = null;
                }

                _rotateToTrueNorthFacing = StartCoroutine(RotateToTrueHeading());
            }
        }
        public void Update()
        {
            _inputService.Update();
        }

        // Late update to ensure we use the latest avatar position
        private void LateUpdate()
        {

            if (_isTransitioning)
                return;

            if (IsNavigating)
            {
                float rotationAngleDegrees = _gestureTracker.RotationAngleDegrees;

                if (_isFacingOn)
                {
                    if (_rotateToTrueNorthFacing == null)
                    {
                        rotationAngleDegrees += TrueHeading;
                    }
                    else
                        rotationAngleDegrees += _currentAnimatingValueForTrueNorthFacing;
                }
                else
                {
                    if (_rotateCoroutine == null && _gestureTracker.RotationAngleDegrees != -_heading)
                        _rotateCoroutine = StartCoroutine(RotateBackToOrigin());
                }

                rotationAngleDegrees += _heading;
                float rotationAngleRadians = Mathf.Deg2Rad * rotationAngleDegrees;
                float zoomFraction = _gestureTracker.ZoomFraction;

                float distance = _zoomCurveEvaluator.GetDistanceFromZoomFraction(zoomFraction);
                float elevMeters = _zoomCurveEvaluator.GetElevationFromDistance(distance);
                float pitchDegrees = _zoomCurveEvaluator.GetAngleFromDistance(distance);

                // Position the camera above the x-z plane,
                // according to our pitch and distance constraints.
                float x = -distance * Mathf.Sin(rotationAngleRadians);
                float z = -distance * Mathf.Cos(rotationAngleRadians);
                var offsetPos = new Vector3(x, elevMeters, z);

                _camera.transform.position = _focusObject.transform.position + offsetPos;
                _camera.transform.rotation = Quaternion.Euler(pitchDegrees, rotationAngleDegrees, 0.0f);
            }
            else
            {

                float rotationAngleDegrees = _gestureTracker.RotationAngleDegrees;

                rotationAngleDegrees += _heading;
                float rotationAngleRadians = Mathf.Deg2Rad * rotationAngleDegrees;
                float zoomFraction = _gestureTracker.ZoomFraction;

#if UNITY_EDITOR
                zoomFraction = _zoomFraction;

                var zoomCurveEvaluator = new ZoomCurveEvaluator(
                    _minimumZoomDistance,
                    _maximumZoomDistance,
                    60,
                    _maximumPitchDegrees,
                    _verticalFocusOffset);
                    _zoomCurveEvaluator = zoomCurveEvaluator;
#endif
                float distance = _currentTopdownZoomDistance;// _zoomCurveEvaluator.GetDistanceFromZoomFraction(zoomFraction);
                float elevMeters = _zoomCurveEvaluator.GetElevationFromDistance(distance);
                float pitchDegrees = _zoomCurveEvaluator.GetAngleFromDistance(distance);

                // Position the camera above the x-z plane,
                // according to our pitch and distance constraints.
                float x = -distance * Mathf.Sin(rotationAngleRadians);
                float z = -distance * Mathf.Cos(rotationAngleRadians);
                var offsetPos = new Vector3(x, 0, z);

                var origin = _focusObject.transform.position;

                if (_gestureTracker.CameraMovement != (offsetPos + _focusObject.transform.position) && _returnToOriginalPosCoroutine == null)
                    _returnToOriginalPosCoroutine = StartCoroutine(MoveBackToOrigin());
                
                //Normalize y to avoid zooming in/out with extreme gesture gesture
                var cameraNewPos = new Vector3(0, elevMeters, 0) + _gestureTracker.CameraMovement;
                cameraNewPos = new Vector3(cameraNewPos.x, Mathf.Clamp(cameraNewPos.y, 300f, 1700f), cameraNewPos.z);
                _gestureTracker.CameraMovement = cameraNewPos - new Vector3(0, elevMeters, 0);


                _camera.transform.position = cameraNewPos;
                _camera.transform.rotation = Quaternion.Euler(pitchDegrees, rotationAngleDegrees, 0.0f);
            }
            float scaleMultiplier = IsNavigating == true ? 10f : 30f;
            _focusObject.transform.localScale = Vector3.one * Mathf.Clamp(_gestureTracker.ZoomFraction, 0.3f, 1f) * scaleMultiplier;
        }


        IEnumerator RotateToTrueHeading()
        {
            float moveDuration = 0.5f;
            float elapsedTime = 0f;
            _heading = -_gestureTracker.RotationAngleDegrees;

            var targetAngle = TrueHeading;
            while (elapsedTime < moveDuration)
            {
                elapsedTime += Time.deltaTime;
                var lerpFactor = elapsedTime / moveDuration;
                _currentAnimatingValueForTrueNorthFacing = Mathf.Lerp(0, targetAngle, lerpFactor);
                yield return null;
            }
            _rotateToTrueNorthFacing = null;
        }
        IEnumerator RotateBackToOrigin()
        {
            float maxSkip = 15;
            float currentSkip = 0;
            yield return new WaitForSeconds(3f);
            while (currentSkip < maxSkip)
            {
                _heading = -currentSkip / (float)maxSkip * _gestureTracker.RotationAngleDegrees;
                yield return null;
                currentSkip++;
            }
            _heading = -_gestureTracker.RotationAngleDegrees;
            _rotateCoroutine = null;
        }
        private IEnumerator MoveBackToOrigin(bool isIdle = false)
        {
            var currentMovement = _gestureTracker.CameraMovement;

            while (!isIdle)
            {
                yield return new WaitForSeconds(5f);
                if (currentMovement == _gestureTracker.CameraMovement)
                    isIdle = true;
                else
                    currentMovement = _gestureTracker.CameraMovement;
            }


            Debug.Log("Start return");

            float moveDuration = 0.5f;
            float elapsedTime = 0f;
            float rotationAngleDegrees = _gestureTracker.RotationAngleDegrees;
            if (_isFacingOn)
            {
                rotationAngleDegrees += TrueHeading;
            }

            rotationAngleDegrees += _heading;
            float rotationAngleRadians = Mathf.Deg2Rad * rotationAngleDegrees;
            float zoomFraction = _gestureTracker.ZoomFraction = 0.5f;

            float distance = _currentTopdownZoomDistance = 500f;// _zoomCurveEvaluator.GetDistanceFromZoomFraction(zoomFraction);
            float elevMeters = _zoomCurveEvaluator.GetElevationFromDistance(distance);
            float pitchDegrees = _zoomCurveEvaluator.GetAngleFromDistance(distance);

            // Position the camera above the x-z plane,
            // according to our pitch and distance constraints.
            float x = -distance * Mathf.Sin(rotationAngleRadians);
            float z = -distance * Mathf.Cos(rotationAngleRadians);
            var offsetPos = new Vector3(x, 0, z);


            var targetMovement = _focusObject.transform.position + offsetPos;

            while (elapsedTime < moveDuration)
            {
                // Calculate how far we have moved as a percentage of the total duration
                elapsedTime += Time.deltaTime;

                var lerpFactor = elapsedTime / moveDuration;
                _gestureTracker.CameraMovement = Vector3.Lerp(currentMovement, targetMovement, lerpFactor);
                yield return null;
            }

            _gestureTracker.CameraMovement = targetMovement;
            _returnToOriginalPosCoroutine = null;

        }


    }
}
