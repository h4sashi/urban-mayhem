using UnityEngine;

namespace DIndicator
{
    public class Waypoint : MonoBehaviour
    {
        private void Start()
        {
            DirectionRegister.Instance.CreateDirectionIndicator(this.transform, DirectionIndicatorType.HideWaypoint);
        }
    }
}
