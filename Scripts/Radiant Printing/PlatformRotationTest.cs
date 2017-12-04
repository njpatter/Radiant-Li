using UnityEngine;
using System.Collections;

public class PlatformRotationTest : MonoBehaviour {
	#if !UNITY_IOS && !UNITY_ANDROID
	private bool startAngleTest = false;
	private bool startRotationTest = false;

	public PrinterController pc;
	private Printer workingPrinter;
	public SerialController serial;
	public bool shouldUseAccel = false;
	//private int[] rates = new int[13]{73,45,35,30, 27,25,23,22, 21,20,19,18, 17};

	private int numStepsPerRot = 6400;
	private int smallAngle = 10;
	private int largeAngle = 15;
	private int stepRate = 17;
	private int smallAngleCount = 0;
	private int largeAngleCount = 0;
	// Use this for initialization
	void Start () {
		Scheduler.StartCoroutine(TestRoutine());
	}

	IEnumerator TestRoutine() {
		while(!startAngleTest && !startRotationTest) yield return null;
		Init();
		yield return null;

		if (startAngleTest) {
			int stepDirection = -1;
			int targetAngle = smallAngle;
			for(int testCount = 0; testCount < 10000000; testCount++) {

				stepDirection = -stepDirection;
				targetAngle = targetAngle == smallAngle ? largeAngle : smallAngle;
				if (true){ //!shouldUseAccel) {
					pc.MoveMotorAtSteprateForTicks(workingPrinter, workingPrinter.platform, StepSize.Quarter, 
					                               (StepDirection)stepDirection,
					                               stepRate, stepRate * numStepsPerRot * targetAngle / 360);
				} 
				/*
				else {
					TickProfile aProfile;
					for(int i = 0; i < rates.Length - 1; i++) {
						aProfile = new TickProfile(workingPrinter.platform,
						                                       StepSize.Quarter,
						                                       (StepDirection)stepDirection,
						                                       rates[i], 
						                                       0,
						                                       rates[i]);
						pc.AddTickProfileForMotorAndSend(workingPrinter, workingPrinter.platform, aProfile);
					}
					aProfile = new TickProfile(workingPrinter.platform,
					                           StepSize.Quarter,
					                           (StepDirection)stepDirection,
					                           rates[rates.Length - 1], 
					                           0,
					                           rates[rates.Length - 1] * numStepsPerRot * targetAngle / 360);
					pc.AddTickProfileForMotorAndSend(workingPrinter, workingPrinter.platform, aProfile);

					for(int i = rates.Length - 2; i > -1; i--) {
						aProfile = new TickProfile(workingPrinter.platform,
						                           StepSize.Quarter,
						                           (StepDirection)stepDirection,
						                           rates[i], 
						                           0,
						                           rates[i]);
						pc.AddTickProfileForMotorAndSend(workingPrinter, workingPrinter.platform, aProfile);
					}
				}
				*/
				yield return null;

				if (targetAngle == smallAngle) smallAngleCount++;
				else largeAngleCount++;
				yield return Scheduler.StartCoroutine(pc.WaitUntilDoneMoving());
			}
		}
		else if (startRotationTest) {
			for(int testCount = 0; testCount < 1000000000; testCount++) {
				TickProfile aProfile = new TickProfile(workingPrinter.platform,
				                                       StepSize.Quarter,
				                                       StepDirection.Ccw,
				                                       stepRate, 
				                                       0,
				                                       stepRate * numStepsPerRot);
				pc.AddTickProfileForMotorAndSend(workingPrinter, workingPrinter.platform, aProfile);

				yield return new WaitSeconds(2.2f);
				//yield return Scheduler.StartCoroutine(pc.WaitUntilDoneMoving());
				largeAngleCount++;
			}
		}

	}

	void Init() {
		workingPrinter = pc.originalPrinter.Clone();
		pc.InitializeMotors(workingPrinter);

	}
	
	// Update is called once per frame
	void OnGUI () {
		if(GUI.Button(new Rect(0,0,200,50), "Start angle test")) startAngleTest = true;
		if (startAngleTest) {
			GUI.Box(new Rect(0, 100, 300, 50), "Moved " + smallAngle + " degrees " + smallAngleCount + " times " +
			        "\nMoved " + largeAngle + " degrees " + largeAngleCount + " times ");
		}

		if (GUI.Button(new Rect(0,50,200,50), "Start rotation test")) {
			startRotationTest = true;
			stepRate = 13;
		}
		if (startRotationTest) {
			GUI.Box(new Rect(0,100, 300, 50), "At Step Rate : " + stepRate + "\nCompleted rotations : " + largeAngleCount);
		}
	}
#endif
}
