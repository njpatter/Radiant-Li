using UnityEngine;
using System.Collections;

/// <summary>
/// A virtual stepper motor.
/// </summary>
[System.Serializable]
public class PrinterMotor : System.Object {
	public const int kStepsPerRotationStandard = 200;
	public const int kMinStepSizeCountPerWholeStep = 16;
	public const int kMaxAccelInStepsPerSec_MovementMotors = 187500;
	public const int kMaxAccelInStepsPerSec_Extruders = 75000;//750000; // Maybe scale by 10?

	public int id;

	public int gearingNumerator      = 1;
	public int gearingDenominator    = 6;
	public virtual int maxAccelInStepsPerSec {
		get {
			return kMaxAccelInStepsPerSec_MovementMotors * (int)stepSize;
		}
	}
	
	public StepSize      stepSize      = StepSize.Whole;
	public StepDirection stepDirection = StepDirection.Ccw;
	public int integralStepPosition = 0;
	
	public float rotationInDegrees {
		get {
			return Mathf.Repeat(degreesPerStep * rotationInSteps, 360.0f);
		}
	}

	public float rotationInRadians {
		get {
			return Mathf.Repeat(Mathf.PI * 2f * rotationInSteps / stepsPerRotation, Mathf.PI * 2f);
		}
	}
	
	/// <summary>
	/// The current rotation, in steps.
	/// NOTE that this assumes that we're not changing the 
	/// step size of the platform, which we currently
	/// (2013 Aug 7) do not.
	/// </summary>
	public int rotationInSteps {
		get {
			return (int)Mathf.Repeat(integralStepPosition / (kMinStepSizeCountPerWholeStep / (int)stepSize), stepsPerRotation);
		}
	}//= 0;
	
	public int stepRate    = 0;
	public int stepCounter = 0;
	
	/// <summary>
	/// Returns the degrees stepped based on the current gearing & step size.
	/// </summary>
	/// <value>
	/// The step size deg.
	/// </value>
	public float degreesPerStep {
		get {
			return 360.0f / (float)stepsPerRotation;
		}
	}

	public float radiansPerStep {
		get {
			return Mathf.PI * 2f / (float)stepsPerRotation;
		}
	}
	
	/// <summary>
	/// Returns the number of raw steps per 360° rotation.
	/// </summary>
	/// <value>
	/// The steps per rotation.... IF this calculates a non-integer value and is
	/// automatically rounded, then we will be in trouble.
	/// </value>
	public int stepsPerRotation {
		get {
			/// If you changed the gearing look at the note above in the function summary
			return kStepsPerRotationStandard * (int)stepSize
				* gearingDenominator / gearingNumerator;
		}
	}
	
	/// <summary>
	/// Clones this instance.
	/// </summary>
	public virtual PrinterMotor Clone() {
		PrinterMotor result = new PrinterMotor();
		Populate(result);
		return result;
	}
	
	/// <summary>
	/// Clones this instance.
	/// </summary>
	public void Populate(PrinterMotor result) {
		result.id = id;
		result.gearingNumerator = gearingNumerator;
		result.gearingDenominator = gearingDenominator;
		result.stepSize = stepSize;
		result.stepDirection = stepDirection;
		
		result.stepRate = stepRate;
		result.stepCounter = stepCounter;
		//result.maxAccelInStepsPerSec = maxAccelInStepsPerSec;
	}
	
	/// <summary>
	/// Takes a step, updating the current rotation in degrees.
	/// </summary>
	public virtual void Step() {
		Step(1);
	}
	
	/// <summary>
	/// Takes multiple steps.
	/// </summary>
	/// <param name='numberOfSteps'>
	/// Number of steps.
	/// </param>
	public virtual void Step(int numberOfSteps) {
		//Text.Log("Moving " + (((stepDirection == StepDirection.Ccw)
		//	? stepSizeDeg : -stepSizeDeg) * numberOfSteps) );
		Contract.Assert(numberOfSteps >= 0,
			@"Negative steps requested: {0}.", numberOfSteps);

		int numBaseStepsTaken = numberOfSteps * (kMinStepSizeCountPerWholeStep / (int)stepSize);
		if (stepDirection == StepDirection.Ccw) {
			//rotationInSteps -= numberOfSteps;
			integralStepPosition -= numBaseStepsTaken;
		}
		else {
			//rotationInSteps += numberOfSteps;
			integralStepPosition += numBaseStepsTaken;
		}
		
		//rotationInSteps   = (int)Mathf.Repeat(rotationInSteps, stepsPerRotation);
	}
	
	public int StepsForDegrees(float degrees) {
		return Mathf.RoundToInt(Mathf.Abs(degrees) / degreesPerStep);
	}
	
	public void ResetRotation() {
		//rotationInSteps = 0;
		integralStepPosition = 0;
	}
}
