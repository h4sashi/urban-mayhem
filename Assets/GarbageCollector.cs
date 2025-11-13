using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GarbageCollector : MonoBehaviour
{
  

    void OnTriggerEnter(Collider other)
    {

        switch (other.gameObject.tag)
        {
            case "Fragile":
                Destroy(other.gameObject);
                break;
            case "Heavy Object":
                Destroy(other.gameObject);
                break;
            case "Light Object":
                Destroy(other.gameObject);
                break;
            case "Crate":
                Destroy(other.gameObject);
                break;
            default:
                break;
        }
    }




}
