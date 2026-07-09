using UnityEngine;

namespace Tarodev {
    public class Target : MonoBehaviour, IExplode {
        [SerializeField] private Rigidbody _rb;
        [SerializeField] private float _size = 10;
        [SerializeField] private float _speed = 10;
        public Rigidbody Rb {
            get {
                CacheRigidbody();
                return _rb;
            }
        }
        public Vector3 Position => Rb != null ? Rb.position : transform.position;
        public Vector3 Velocity => Rb != null ? Rb.velocity : Vector3.zero;

        private void Awake() => CacheRigidbody();

        private void OnValidate() => CacheRigidbody();

        private void CacheRigidbody() {
            if (_rb != null)
                return;

            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = GetComponentInParent<Rigidbody>();
            if (_rb == null)
                _rb = GetComponentInChildren<Rigidbody>();
        }

        // void Update() {
        //     var dir = new Vector3(Mathf.Cos(Time.time * _speed) * _size, Mathf.Sin(Time.time * _speed) * _size);

        //     _rb.velocity = dir;
        // }

        public void Explode() => Destroy(gameObject);
    }
}
