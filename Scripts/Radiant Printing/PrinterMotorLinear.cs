using UnityEngine;
using System.Collections;

/// <summary>
/// Adds a linear position to the printer motor class.
/// </summary>
[System.Serializable]
public class PrinterMotorLinear : PrinterMotor { 
	private float m_initialPosition = 0f;
	public float position {
		get {
			return (m_initialPosition - ((float)integralStepPosition / ((float)kMinStepSizeCountPerWholeStep * 
			                                                            kStepsPerRotationStandard) / threadsPerMm));
		}
		set {
			m_initialPosition = value;
		}
	}
	public float threadsPerInch;
	
	public float threadsPerMm {
		get { return threadsPerInch / 25.4f; }	
	}
	/// <summary>
	/// How far the motor travels per step.
	/// </summary>
	/// <value>
	/// The distance per step.
	/// </value>
	public float distancePerStep {
		get {
			return (float)stepDirection / (stepsPerRotation * threadsPerMm) ;
		}
	}
	
	/// <summary>
	/// Clones this instance.
	/// </summary>
	public override PrinterMotor Clone() {
		PrinterMotorLinear result = new PrinterMotorLinear();
		Populate(result);
		return result;
	}
	
	/// <summary>
	/// Clones this instance.
	/// </summary>
	public void Populate(PrinterMotorLinear result) {
		base.Populate(result);
		
		result.position = m_initialPosition;
		result.threadsPerInch = threadsPerInch;
	}
	
	/// <summary>
	/// Takes a step.
	/// </summary>
	public override void Step() {
		Step(1);
	}
	
	/// <summary>
	/// Takes multiple steps at once.
	/// </summary>
	/// <param name='numberOfSteps'>
	/// Number of steps.
	/// </param>
	public override void Step(int numberOfSteps) {
		base.Step(numberOfSteps);
		//position -= distancePerStep * numberOfSteps;
		/*Debug.Log("Old style position = " + position + " and new version = " + (m_initialPosition - 
		                                                                        ((float)integralStepPosition / (16f * kStepsPerRotationStandard) /
		 threadsPerMm)));*/
	}
	
	private float m_error = 0.0f;
	
	/// <summary>
	/// Returns the number of steps for the given distance in mm... Does NOT factor
	/// in stepRate.
	/// </summary>
	/// <returns>
	/// The for.
	/// </returns>
	/// <param name='distanceInMm'>
	/// Distance in mm.
	/// </param>
	public int StepsForMm(float distanceInMm) {
		float stepsRequired = (distanceInMm - m_error)  / Mathf.Abs(distancePerStep);
		
		int integralStepsRequired = Mathf.RoundToInt(stepsRequired);
		m_error = (integralStepsRequired - stepsRequired) * Mathf.Abs(distancePerStep); //(stepsRequired - integralStepsRequired) * Mathf.Sign(stepsRequired);
		//Text.Error("For a target distance {0}, we needed {1} steps and will take {2} which gives us an actual distance of {3} and an error = {4}",
		//           distanceInMm, stepsRequired, integralStepsRequired, integralStepsRequired * Mathf.Abs(distancePerStep), m_error);
		Contract.Assert(Mathf.Abs(m_error) < Mathf.Abs(distancePerStep),
		                @"Error of {0} is larger than a step ({1}).",
		                m_error, 
		                Mathf.Abs(distancePerStep));
		return integralStepsRequired;
	}

	public float locationError {
		get {
			return m_error;
		}
		set {
			m_error = value;
			if (Mathf.Abs(m_error) > Mathf.Abs(distancePerStep)) Text.Error("Error ({0}) is larger than a single step ({1})", m_error, distancePerStep);
		}
	}
}
