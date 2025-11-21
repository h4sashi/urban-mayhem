using UnityEngine;

namespace DIndicator
{
    public class TargetExample : MonoBehaviour
    {
        private DirectionIndicator targetDirectionIndicator;

        private void Start()
        {
            targetDirectionIndicator = DirectionRegister.Instance.CreateDirectionIndicator(this.transform, DirectionIndicatorType.Target);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.tag == "Player")
            {
                if (targetDirectionIndicator != null) targetDirectionIndicator.DestroyIndicator();
            }
        }
    }
}
