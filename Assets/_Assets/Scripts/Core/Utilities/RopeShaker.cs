using UnityEngine;

public class RopeShaker : MonoBehaviour
{
    public Rigidbody target;         // The top rope segment
    public float forceStrength = 2f; // Adjust for how much it shakes
    public float torqueStrength = 1f;
    public float shakeFrequency = 1.5f; // How often it shakes per second
    public bool randomizeDirection = true;

    private float timer;

    void FixedUpdate()
    {
        timer += Time.fixedDeltaTime;
        if (timer >= 1f / shakeFrequency)
        {
            timer = 0f;

            Vector3 randomDir = randomizeDirection 
                ? Random.insideUnitSphere 
                : new Vector3(0, 0, Random.Range(-1f, 1f));

            target.AddForce(randomDir * forceStrength, ForceMode.Impulse);
            target.AddTorque(Random.insideUnitSphere * torqueStrength, ForceMode.Impulse);
        }
    }
}
