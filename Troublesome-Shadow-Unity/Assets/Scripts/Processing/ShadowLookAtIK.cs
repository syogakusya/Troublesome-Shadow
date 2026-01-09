using UnityEngine;

namespace PoseRuntime
{
    /// <summary>
    /// Drives Animator IK to keep the head looking at a target while clips play.
    /// Animator layers that should use IK must have "IK Pass" enabled.
    /// </summary>
    [DefaultExecutionOrder(360)]
    public class ShadowLookAtIK : MonoBehaviour
    {
        public Animator _animator;
        public float _maxWeight = 1f;
        public float _bodyWeight = 0.1f;
        public float _headWeight = 0.6f;
        public float _eyesWeight = 1f;
        public float _clampWeight = 0.7f;
        public float _smoothTime = 0.12f;

        private Vector3 _targetPosition;
        private bool _hasTarget;
        private float _currentWeight;
        private float _targetWeight;
        private float _weightVelocity;

        private void Reset()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
        }

        public void SetTarget(Vector3 position, float weight = 1f)
        {
            _targetPosition = position;
            _hasTarget = true;
            _targetWeight = Mathf.Clamp01(weight) * Mathf.Clamp01(_maxWeight);
        }

        public void ClearTarget()
        {
            _hasTarget = false;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null)
            {
                return;
            }

            var desiredWeight = _hasTarget ? _targetWeight : 0f;
            _currentWeight = Mathf.SmoothDamp(_currentWeight, desiredWeight, ref _weightVelocity, _smoothTime);
            _animator.SetLookAtWeight(_currentWeight, _bodyWeight, _headWeight, _eyesWeight, _clampWeight);

            if (_currentWeight > 0.001f && _hasTarget)
            {
                _animator.SetLookAtPosition(_targetPosition);
            }
        }
    }
}
