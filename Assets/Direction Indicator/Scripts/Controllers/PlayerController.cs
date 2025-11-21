using UnityEngine;

namespace DIndicator
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private Transform _playerCamera;
        [SerializeField] private float _gravity;

        private float MinPitch = -60;
        private float MaxPitch = 60;
        private float LookSensitivity = 1;

        private float MoveSpeed = 10;
        private float SprintSpeed = 30;
        private float currMoveSpeed = 0;

        protected CharacterController movementController;

        protected bool isControlling;
        protected float yaw;
        protected float pitch;

        protected Vector3 velocity;

        protected virtual void Start()
        {
            movementController = GetComponent<CharacterController>();   //  Character Controller

            isControlling = true;

            Cursor.lockState = (isControlling) ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !isControlling;
        }

        protected virtual void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) transform.position = new Vector3(3, 0, 0);

            Vector3 direction = Vector3.zero;
            direction += transform.forward * Input.GetAxisRaw("Vertical");
            direction += transform.right * Input.GetAxisRaw("Horizontal");

            direction.Normalize();

            if (movementController.isGrounded)
            {
                velocity = Vector3.zero;
            }
            else
            {
                velocity += -transform.up * _gravity * Time.deltaTime; // Gravity
            }

            if (Input.GetKey(KeyCode.LeftShift))
            {
                currMoveSpeed = SprintSpeed;
            }
            else
            {
                currMoveSpeed = MoveSpeed;
            }

            direction += velocity * Time.deltaTime;
            movementController.Move(direction * Time.deltaTime * currMoveSpeed);

            // Camera Look
            yaw += Input.GetAxisRaw("Mouse X") * LookSensitivity;
            pitch -= Input.GetAxisRaw("Mouse Y") * LookSensitivity;

            _playerCamera.eulerAngles = new Vector3(Mathf.Clamp(pitch, MinPitch, MaxPitch), _playerCamera.eulerAngles.y, _playerCamera.eulerAngles.z);
            transform.eulerAngles = new Vector3(0.0f, yaw, 0.0f);
        }
    }
}
