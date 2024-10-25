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
        public bool _isFacingOn;
        [SerializeField] Coroutine _rotateCoroutine;
        [SerializeField] Coroutine _returnToOriginalPosCoroutine;
        [SerializeField] bool _isTransitioning;

        [SerializeField] float _currentTopdownZoomDistance;
        public void Awake()
        {
            _gestureTracker = new CameraGestureTracker(_camera, _focusObject, _gestureSettings);
            _inputService = new InputService(_gestureTracker);

            _zoomCurveEvaluator = new ZoomCurveEvaluator(
                _minimumZoomDistance,
                _maximumZoomDistance,
                _minimumPitchDegrees,
                _maximumPitchDegrees,
                _verticalFocusOffset);

            //_gestureTracker.ZoomFraction = 1f;
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
                        30f,
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
                if (_isFacingOn)
                {
                    rotationAngleDegrees += Input.compass.trueHeading;
                }

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
                    _focusObject.transform.localScale = Vector3.one * 30 * Mathf.Clamp(_gestureTracker.ZoomFraction, 0.3f, 1f);

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
                float zoomFraction = _gestureTracker.ZoomFraction = 1f;

                float distance = _currentTopdownZoomDistance;// _zoomCurveEvaluator.GetDistanceFromZoomFraction(zoomFraction);
                float elevMeters = _zoomCurveEvaluator.GetElevationFromDistance(distance);
                float pitchDegrees = _zoomCurveEvaluator.GetAngleFromDistance(distance); //60;

                float x = -distance * Mathf.Sin(rotationAngleRadians);
                float z = -distance * Mathf.Cos(rotationAngleRadians);
                var offsetPos = new Vector3(x, elevMeters, z);

                Vector3 targetPos = _focusObject.transform.position + offsetPos;// + _gestureTracker.CameraMovement;
                Quaternion targetRot = Quaternion.Euler(pitchDegrees, rotationAngleDegrees, 0.0f);

                //For update function to correctly update position dynamically
                _gestureTracker.MaxCameraMovement = _gestureTracker.CameraMovement = new Vector3(x, 0, z) + _focusObject.transform.position;

                Vector3 currentCamPos = _camera.transform.position;
                Quaternion currentCamRot = _camera.transform.rotation;

                elapsedTime = 0f;
                while (elapsedTime < transitionDuration)
                {
                    elapsedTime += Time.deltaTime;
                    float lerpFactor = Mathf.Clamp01(elapsedTime / transitionDuration);
                    _gestureTracker.ZoomFraction = Mathf.Lerp(0.3f, 1f, lerpFactor);
                    _focusObject.transform.localScale = Vector3.one * 30 * Mathf.Clamp(_gestureTracker.ZoomFraction, 0.3f, 1f);

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
                    rotationAngleDegrees += Input.compass.trueHeading;
                }
                else
                {
                    if (_rotateCoroutine == null && _gestureTracker.RotationAngleDegrees != -_heading)
                    {
                        _rotateCoroutine = StartCoroutine(RotateBackToOrigin());
                    }
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
                     
                _camera.transform.position = new Vector3(0, elevMeters, 0) + _gestureTracker.CameraMovement;
                _camera.transform.rotation = Quaternion.Euler(pitchDegrees, rotationAngleDegrees, 0.0f);
            }
            _focusObject.transform.localScale = Vector3.one * 30 * Mathf.Clamp(_gestureTracker.ZoomFraction, 0.3f, 1f);
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
        private IEnumerator MoveBackToOrigin()
        {
            var currentMovement = _gestureTracker.CameraMovement;


            bool IsIdle = false;
            while (!IsIdle)
            {
                yield return new WaitForSeconds(5f);  // Optional delay before moving back to origin
                if (currentMovement == _gestureTracker.CameraMovement)
                    IsIdle = true;
                else
                    currentMovement = _gestureTracker.CameraMovement;
            }


            Debug.Log("Start return");

            float moveDuration = 0.5f;
            float elapsedTime = 0f;
            float rotationAngleDegrees = _gestureTracker.RotationAngleDegrees;
            if (_isFacingOn)
            {
                rotationAngleDegrees += Input.compass.trueHeading;
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
