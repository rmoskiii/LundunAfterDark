using UnityEngine;

public class VehicleSystem : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private GameObject player;
    [SerializeField] private CameraFollow cameraFollow;
    [SerializeField] private float enterRange = 3.5f;

    [Header("UI groups")]
    [SerializeField] private GameObject onFootControls;
    [SerializeField] private GameObject drivingControls;
    [SerializeField] private GameObject enterButton;

    [Header("Driving buttons")]
    [SerializeField] private HoldButton gasButton;
    [SerializeField] private HoldButton brakeButton;
    [SerializeField] private HoldButton leftButton;
    [SerializeField] private HoldButton rightButton;

    private CarController[] cars;
    private CarController nearbyCar;
    private CarController currentCar;
    private bool controlsLocked;

    // Whatever the police should chase: James on foot, or the car he's driving
    public Transform PlayerTarget => currentCar != null ? currentCar.transform : player.transform;

    public bool IsDriving => currentCar != null;

    // How fast the player is going, on foot or in a car
    public float PlayerSpeed
    {
        get
        {
            if (currentCar != null) return Mathf.Abs(currentCar.Speed);
            Vector3 v = player.GetComponent<CharacterController>().velocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    void Start()
    {
        cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
        foreach (CarController car in cars) car.IsDriven = false;
        ShowDrivingUI(false);
    }

    void Update()
    {
        if (controlsLocked) return;

        if (currentCar != null)
        {
            // Turn held buttons into steering and throttle
            float throttle = (gasButton.IsHeld ? 1f : 0f) - (brakeButton.IsHeld ? 1f : 0f);
            float steer = (rightButton.IsHeld ? 1f : 0f) - (leftButton.IsHeld ? 1f : 0f);
            currentCar.SetTouchInput(throttle, steer);
            return;
        }

        // On foot: is there a car close enough to get into?
        nearbyCar = null;
        float closest = enterRange;
        foreach (CarController car in cars)
        {
            if (car == null) continue;
            float d = Vector3.Distance(player.transform.position, car.transform.position);
            if (d < closest) { closest = d; nearbyCar = car; }
        }
        enterButton.SetActive(nearbyCar != null);
    }

    // Hooked up to the 🚗 button's On Click
    public void EnterCar()
    {
        if (nearbyCar == null || controlsLocked) return;

        currentCar = nearbyCar;
        player.SetActive(false);                    // James "gets in"
        currentCar.IsDriven = true;
        cameraFollow.SetTarget(currentCar.transform, true);
        ShowDrivingUI(true);
    }

    // Hooked up to the exit button's On Click
    public void ExitCar()
    {
        if (currentCar == null) return;

        // UK cars: driver gets out on the right
        Transform car = currentCar.transform;
        player.transform.position = car.position + car.right * 1.8f + Vector3.up * 0.5f;
        player.transform.rotation = Quaternion.Euler(0f, car.eulerAngles.y, 0f);

        currentCar.SetTouchInput(0f, 0f);
        currentCar.IsDriven = false;
        player.SetActive(true);
        cameraFollow.SetTarget(player.transform, false);

        currentCar = null;
        ShowDrivingUI(false);
    }

    // Freeze the player during cutscenes like BUSTED
    public void LockControls(bool locked)
    {
        controlsLocked = locked;
        onFootControls.SetActive(!locked && currentCar == null);
        drivingControls.SetActive(!locked && currentCar != null);
        enterButton.SetActive(false);

        player.GetComponent<PlayerController>().enabled = !locked;
        player.GetComponent<PlayerCombat>().enabled = !locked;

        if (currentCar != null)
        {
            currentCar.SetTouchInput(0f, 0f);
            currentCar.IsDriven = !locked;
        }
    }

    // Get out of any car and appear somewhere else
    public void RespawnPlayer(Vector3 position, Quaternion rotation)
    {
        if (currentCar != null) ExitCar();

        // A CharacterController ignores teleports unless you switch it off first
        CharacterController cc = player.GetComponent<CharacterController>();
        cc.enabled = false;
        player.transform.SetPositionAndRotation(position, rotation);
        cc.enabled = true;
    }

    void ShowDrivingUI(bool driving)
    {
        onFootControls.SetActive(!driving);
        drivingControls.SetActive(driving);
        enterButton.SetActive(false);
    }
}