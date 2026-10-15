using UnityEngine;
using UnityEngine.Rendering;
using Mirror;
using Unity.Cinemachine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/*
 * First-person version of ThirdPersonController.
 * - Mouse X turns the whole body, mouse Y tilts only the camera (PlayerCameraRoot).
 * - WASD moves relative to where you're facing (A/D strafe).
 * - The local player's own body is hidden from their camera but still casts shadows;
 *   other players still see the full body.
 * Jump, gravity, grounding, animation, footsteps and Mirror networking are unchanged
 * from Aaron's ThirdPersonController.
 */

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class FirstPersonController : NetworkBehaviour
    {
        [Header("Player")]
        [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 2.0f;

        [Tooltip("Sprint speed of the character in m/s")]
        public float SprintSpeed = 5.335f;

        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.50f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;

        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;

        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Header("First Person Camera")]
        [Tooltip("The camera pivot at head height (PlayerCameraRoot). Tilts up/down; the body turns left/right.")]
        public GameObject CinemachineCameraTarget;

        [Tooltip("How far in degrees you can look up")]
        public float TopClamp = 85.0f;

        [Tooltip("How far in degrees you can look down")]
        public float BottomClamp = -85.0f;

        [Tooltip("Mouse sensitivity (x = left/right, y = up/down)")]
        public Vector2 LookSensitivity = new Vector2(7.5f, 5.0f);

        [Tooltip("Hide your own body from your own camera (it still casts a shadow). Other players always see it.")]
        public bool HideOwnBody = true;

        [Tooltip("Root of the visible body meshes (the 'Geometry' object). Leave empty to hide every renderer on the player except the camera.")]
        public Transform BodyRoot;

        // camera
        private float _cameraPitch;

        public bool IsRespawning { get; set; } = false;

        // player
        private float _speed;
        private float _animationBlend;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        // animation IDs
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private StarterAssetsInputs _input;

        private const float _threshold = 0.01f;

        private bool _hasAnimator;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
                return false;
#endif
            }
        }

        public override void OnStartClient()
        {
            if (isLocalPlayer) return;

            // remote players: turn off their cameras, audio listener and input so only our own player drives the view
            NetworkIdentity root = GetComponentInParent<NetworkIdentity>();
            foreach (CinemachineCamera cinemachineCamera in root.GetComponentsInChildren<CinemachineCamera>(true))
                cinemachineCamera.gameObject.SetActive(false);
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                camera.gameObject.SetActive(false);
            foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
#if ENABLE_INPUT_SYSTEM
            GetComponent<PlayerInput>().enabled = false;
#endif
        }

        public override void OnStartLocalPlayer()
        {
            if (HideOwnBody) HideBodyFromOwnCamera();
        }

        private void Start()
        {
            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#else
            Debug.LogError("Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            AssignAnimationIDs();

            _cameraPitch = 0f;

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }

        private void Update()
        {
            if (!isLocalPlayer) return;

            _hasAnimator = TryGetComponent(out _animator);

            JumpAndGravity();
            GroundedCheck();
            Move();
        }

        private void LateUpdate()
        {
            if (!isLocalPlayer) return;

            CameraRotation();
        }

        private void HideBodyFromOwnCamera()
        {
            NetworkIdentity root = GetComponentInParent<NetworkIdentity>();
            Transform searchRoot = BodyRoot != null ? BodyRoot : root.transform;

            foreach (Renderer r in searchRoot.GetComponentsInChildren<Renderer>(true))
            {
                // keep the shadow so you can still see your silhouette on the ground
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        }

        private void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
                transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
                QueryTriggerInteraction.Ignore);

            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void CameraRotation()
        {
            if (IsRespawning)
            {
                _cameraPitch = 0f;
                CinemachineCameraTarget.transform.localRotation = Quaternion.identity;
                IsRespawning = false;
                return;
            }

            if (_input.look.sqrMagnitude >= _threshold)
            {
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                // up/down: tilt only the camera pivot
                _cameraPitch += _input.look.y * deltaTimeMultiplier * LookSensitivity.y;
                _cameraPitch = ClampAngle(_cameraPitch, BottomClamp, TopClamp);

                // left/right: turn the whole body (Mirror syncs this to other players)
                float yawDelta = _input.look.x * deltaTimeMultiplier * LookSensitivity.x;
                transform.Rotate(Vector3.up * yawDelta);
            }

            CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cameraPitch, 0.0f, 0.0f);
        }

        private void Move()
        {
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            // move relative to where the body is facing: W/S forward/back, A/D strafe
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;
            Vector3 moveDirection = Vector3.zero;
            if (_input.move != Vector2.zero)
            {
                moveDirection = transform.right * inputDirection.x + transform.forward * inputDirection.z;
            }

            _controller.Move(moveDirection.normalized * (_speed * Time.deltaTime) +
                             new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }
                }

                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;

                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }

                _input.jump = false;
            }

            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            Gizmos.color = Grounded ? transparentGreen : transparentRed;

            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
                GroundedRadius);
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (FootstepAudioClips.Length > 0)
                {
                    var index = Random.Range(0, FootstepAudioClips.Length);
                    AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
                }
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }

        // Used by RespawnPlayer
        public void ResetCameraRotation(float targetYaw)
        {
            transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
            _cameraPitch = 0f;
            CinemachineCameraTarget.transform.localRotation = Quaternion.identity;
        }

        // Used by RespawnPlayer
        public void ResetVerticalVelocity()
        {
            _verticalVelocity = 0f;
        }
    }
}
