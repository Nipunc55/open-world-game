using UnityEngine;
using Fusion;
using Fusion.Addons.SimpleKCC;

namespace Starter.ThirdPersonCharacter
{
	/// <summary>
	/// Main player scrip - controls player movement and animations.
	/// </summary>
	public sealed class Player : NetworkBehaviour
	{
		[Header("References")]
		public SimpleKCC KCC;
		public PlayerInput PlayerInput;
		public Animator Animator;
		public Transform CameraPivot;
		public Transform CameraHandle;

		[Header("Movement Setup")]
		public float WalkSpeed = 2f;
		public float SprintSpeed = 5f;
		public float JumpImpulse = 10f;
		public float UpGravity = 25f;
		public float DownGravity = 40f;
		public float RotationSpeed = 8f;

		[Header("Movement Accelerations")]
		public float GroundAcceleration = 55f;
		public float GroundDeceleration = 25f;
		public float AirAcceleration = 25f;
		public float AirDeceleration = 1.3f;

		[Header("Sounds")]
		public AudioClip[] FootstepAudioClips;
		public AudioClip LandingAudioClip;
		[Range(0f, 1f)]
		public float FootstepAudioVolume = 0.5f;
		public AudioSource FireSound;

		[Header("Fire Setup")]
		public LayerMask HitMask;
		public GameObject ImpactPrefab;
		public ParticleSystem MuzzleParticle;

		[Header("Aim Setup")]
		public Transform ChestTargetPosition;
		public Transform ChestBone;
		public float AimRotationSpeed = 15f;

		[Networked]
		private NetworkBool _isJumping { get; set; }
		[Networked]
		private int _fireCount { get; set; }
		[Networked]
		private Vector3 _hitPosition { get; set; }
		[Networked]
		private Vector3 _hitNormal { get; set; }
		[Networked]
		public NetworkBool IsAiming { get; set; }
		[Networked]
		public Vector2 NetworkedLookRotation { get; set; }

		private Vector3 _moveVelocity;
		private int _visibleFireCount;

		// Animation IDs
		private int _animIDSpeed;
		private int _animIDGrounded;
		private int _animIDJump;
		private int _animIDFreeFall;
		private int _animIDMotionSpeed;
		private int _animIDShoot;
		private int _animIDAim;

		public override void Spawned()
		{
			// Reset visible fire count to match networked value on spawn
			_visibleFireCount = _fireCount;

			// Disable input for other players
			if (HasStateAuthority == false)
			{
				PlayerInput.enabled = false;
			}
		}

		public override void FixedUpdateNetwork()
		{
			ProcessInput(PlayerInput.CurrentInput);

			if (KCC.IsGrounded)
			{
				// Stop jumping
				_isJumping = false;
			}

			PlayerInput.ResetInput();
		}

		public override void Render()
		{
			
			Animator.SetFloat(_animIDSpeed, KCC.RealSpeed, 0.15f, Time.deltaTime);
			Animator.SetFloat(_animIDMotionSpeed, 1f);
			Animator.SetBool(_animIDJump, _isJumping);
			Animator.SetBool(_animIDGrounded, KCC.IsGrounded);
			Animator.SetBool(_animIDFreeFall, KCC.RealVelocity.y < -10f);

			Animator.SetBool(_animIDAim, IsAiming);

			//ShowFireEffects();
		}

		private void Awake()
		{
			AssignAnimationIDs();
		}

		private void LateUpdate()
		{
			// Update camera pivot. For the local player we use direct input for maximum smoothness.
			// For others we use the networked rotation.
			Vector2 lookRotation = HasStateAuthority ? PlayerInput.CurrentInput.LookRotation : NetworkedLookRotation;
			CameraPivot.rotation = Quaternion.Euler(lookRotation);

			// Only local player needs to update the actual camera position
			if (HasStateAuthority == false)
				return;

			Camera.main.transform.SetPositionAndRotation(CameraHandle.position, CameraHandle.rotation);

			// IK logic for aiming
		// 	if (ChestBone != null && ChestTargetPosition != null && PlayerInput.CurrentInput.Aim)
		// 	{
		// 		float blendAmount = 0.1f;
		// 		ChestBone.position = Vector3.Lerp(ChestTargetPosition.position, ChestBone.position, blendAmount);
		// 		ChestBone.rotation = Quaternion.Lerp(ChestTargetPosition.rotation, ChestBone.rotation, blendAmount);
		// 	}
		 }

		private void ProcessInput(GameplayInput input)
		{
			float jumpImpulse = 0f;

			// Comparing current input buttons to previous input buttons - this prevents glitches when input is lost
			if (KCC.IsGrounded && input.Jump)
			{
				// Set world space jump vector
				jumpImpulse = JumpImpulse;
				_isJumping = true;
			}

			// It feels better when the player falls quicker
			KCC.SetGravity(KCC.RealVelocity.y >= 0f ? UpGravity : DownGravity);

			// Synchronize networked properties
			IsAiming = input.Aim;
			NetworkedLookRotation = input.LookRotation;

			float speed = input.Sprint ? SprintSpeed : WalkSpeed;

			var lookRotation = Quaternion.Euler(0f, input.LookRotation.y, 0f);
			// Calculate correct move direction from input (rotated based on camera look)
			var moveDirection = lookRotation * new Vector3(input.MoveDirection.x, 0f, input.MoveDirection.y);
			var desiredMoveVelocity = moveDirection * speed;

			float acceleration;
			if (input.Aim)
			{
				// Rotate the character towards camera direction when aiming
				var nextRotation = Quaternion.Lerp(KCC.TransformRotation, lookRotation, AimRotationSpeed * Runner.DeltaTime);
				KCC.SetLookRotation(nextRotation.eulerAngles);

				acceleration = KCC.IsGrounded ? GroundAcceleration : AirAcceleration;
			}
			else if (desiredMoveVelocity == Vector3.zero)
			{
				// No desired move velocity - we are stopping
				acceleration = KCC.IsGrounded ? GroundDeceleration : AirDeceleration;
			}
			else
			{
				// Rotate the character towards move direction over time
				var currentRotation = KCC.TransformRotation;
				var targetRotation = Quaternion.LookRotation(moveDirection);
				var nextRotation = Quaternion.Lerp(currentRotation, targetRotation, RotationSpeed * Runner.DeltaTime);

				KCC.SetLookRotation(nextRotation.eulerAngles);

				acceleration = KCC.IsGrounded ? GroundAcceleration : AirAcceleration;
			}

			_moveVelocity = Vector3.Lerp(_moveVelocity, desiredMoveVelocity, acceleration * Runner.DeltaTime);

			// Ensure consistent movement speed even on steep slope
			if (KCC.ProjectOnGround(_moveVelocity, out var projectedVector))
			{
				_moveVelocity = projectedVector;
			}

			KCC.Move(_moveVelocity, jumpImpulse);

			if (input.Fire)
			{
				Fire();
			}
		}

		private void Fire()
		{
			_hitPosition = Vector3.zero;

			// Raycast from camera center
			if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out var hitInfo, 200f, HitMask))
			{
				_hitPosition = hitInfo.point;
				_hitNormal = hitInfo.normal;

				// Deal damage if hit something with Health component (like in Shooter example)
				// var health = hitInfo.collider.GetComponentInParent<Health>();
				// if (health != null) health.TakeHit(1);
			}

			_fireCount++;
		}

		private void ShowFireEffects()
		{
			if (_visibleFireCount < _fireCount)
			{
				if (FireSound != null) FireSound.PlayOneShot(FireSound.clip);
				if (MuzzleParticle != null) MuzzleParticle.Play();
				Animator.SetTrigger(_animIDShoot);

				if (_hitPosition != Vector3.zero && ImpactPrefab != null)
				{
					Instantiate(ImpactPrefab, _hitPosition, Quaternion.LookRotation(_hitNormal));
				}
			}

			_visibleFireCount = _fireCount;
		}

		private void AssignAnimationIDs()
		{
			_animIDSpeed = Animator.StringToHash("Speed");
			_animIDGrounded = Animator.StringToHash("Grounded");
			_animIDJump = Animator.StringToHash("Jump");
			_animIDFreeFall = Animator.StringToHash("FreeFall");
			_animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
			_animIDShoot = Animator.StringToHash("Shoot");
			_animIDAim = Animator.StringToHash("Aim");
		}

		// Animation event
		private void OnFootstep(AnimationEvent animationEvent)
		{
			if (animationEvent.animatorClipInfo.weight < 0.5f)
				return;

			if (FootstepAudioClips != null && FootstepAudioClips.Length > 0)
			{
				var index = Random.Range(0, FootstepAudioClips.Length);
				AudioSource.PlayClipAtPoint(FootstepAudioClips[index], KCC.Position, FootstepAudioVolume);
			}
		}

		// Animation event
		private void OnLand(AnimationEvent animationEvent)
		{
			if (LandingAudioClip != null)
				AudioSource.PlayClipAtPoint(LandingAudioClip, KCC.Position, FootstepAudioVolume);
		}
	}
}
