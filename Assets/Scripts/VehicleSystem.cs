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

    void Start()
    {
        cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
        foreach (CarController car in cars) car.IsDriven = false;
        ShowDrivingUI(false);
    }

    void Update()
    {
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
            float d = Vector3.Distance(player.transform.position, car.transform.position);
            if (d < closest) { closest = d; nearbyCar = car; }
        }
        enterButton.SetActive(nearbyCar != null);
    }

    // Hooked up to the 🚗 button's On Click
    public void EnterCar()
    {
        if (nearbyCar == null) return;

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

    void ShowDrivingUI(bool driving)
    {
        onFootControls.SetActive(!driving);
        drivingControls.SetActive(driving);
        enterButton.SetActive(false);
    }
}