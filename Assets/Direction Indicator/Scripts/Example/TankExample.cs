using UnityEngine;

namespace DIndicator
{
    public class TankExample : MonoBehaviour
    {
        [SerializeField] Transform _tankTower;
        [SerializeField] Transform _player;

        [SerializeField, Range(10, 50)] int _range;

        private float timeToShoot;

        private void Start()
        {
            DirectionRegister.Instance.CreateDirectionIndicator(_tankTower, DirectionIndicatorType.Point);
        }

        private void Update()
        {
            _tankTower.LookAt(_player, Vector3.up);

            timeToShoot += Time.deltaTime;

            if (timeToShoot >= Random.Range(_range - 10, _range))
            {
                DirectionRegister.Instance.CreateDirectionIndicator(_tankTower, DirectionIndicatorType.Damage);

                timeToShoot = 0f;
            }
        }
    }
}
