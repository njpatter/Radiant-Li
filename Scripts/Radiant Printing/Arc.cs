using UnityEngine;
using System.Collections;

/// <summary>
/// An arc of material on a ring.
/// </summary>
public class Arc : System.Object {
	public const int kNotInitialized = -1134;
	
	public PrinterMotor  motor;
	public StepDirection direction;
	
	/// <summary>
	/// The inclusive start step.
	/// </summary>
	public int startStep;
	
	/// <summary>
	/// The exclusive end step.
	/// </summary>
	public int endStep;

	public int material;

	public bool hasVariableStepRate = false;
	public int[] variableStepLocations;
	int variableStepIndex = 0;

	public void SetNumberOfVariableSteps(int aStepCount) {
		variableStepLocations = new int[aStepCount];

	}

	public void AddStepLocation(int aStepLocation) {
		variableStepLocations[variableStepIndex] = aStepLocation;
		variableStepIndex++;
	}

	public int pressureStart(int stepsForPressure) {
		return startStep - stepsForPressure;
	}
	
	public int depressureEnd(int stepsForDePressure) {
		return endStep + stepsForDePressure;
	}

	/// <summary>
	/// The total length of the arc.
	/// </summary>
	/// <value>
	/// The length.
	/// </value>
	public int length {
		get {
			Contract.Assert(startStep != kNotInitialized, @"Start step not initialized.");
			Contract.Assert(endStep   != kNotInitialized, @"End step not initialized.");
			Contract.Assert(startStep <= endStep, @"Start step ({0}) after end step ({1}) for motor {2}.",
				startStep, endStep, ((motor != null) ? motor.id.ToString() : "Motor not set"));
			
			int result = endStep - startStep;
			Contract.Assert(result >= 0, @"Negative length of {0}.", result);
			return result;
		}
	}
	
	/// <summary>
	/// Initializes a new instance of the <see cref="Arc"/> class.
	/// </summary>
	/// <param name='anId'>
	/// An extruder identifier.
	/// </param>
	/// <param name='aStartStep'>
	/// A start step.
	/// </param>
	public Arc(PrinterMotor aMotor, int aStartStep) {
		motor = aMotor;
		startStep = aStartStep;
		endStep = kNotInitialized;
		material = kNotInitialized;
		direction = aMotor.stepDirection;
		hasVariableStepRate = false;
	}
	
	/// <summary>
	/// Initializes a new instance of the <see cref="Arc"/> class.
	/// </summary>
	/// <param name='aMaterial'>
	/// A material id.
	/// </param>
	/// <param name='aStartStep'>
	/// A start step.
	/// </param>
	public Arc(int aMaterial, int aStartStep) {
		motor = null;
		material = aMaterial;
		startStep = aStartStep;
		endStep = kNotInitialized;
		direction = StepDirection.Unknown;
		hasVariableStepRate = false;
	}
	
	/// <summary>
	/// Çlones an instance of the <see cref="Arc"/> class.
	/// </summary>
	/// <param name='aSource'>
	/// A source.
	/// </param>
	public Arc(Arc aSource) {
		motor     = aSource.motor;
		startStep = aSource.startStep;
		endStep   = aSource.endStep;
		material  = aSource.material;
		direction = aSource.direction;
		hasVariableStepRate = false;
	}
	
	/// <summary>
	/// Returns a <see cref="System.String"/> that represents the current <see cref="Arc"/>.
	/// </summary>
	/// <returns>
	/// A <see cref="System.String"/> that represents the current <see cref="Arc"/>.
	/// </returns>
	public override string ToString() {
		string id = motor != null ? motor.id.ToString() : "null";
		return string.Format("[Arc motor {0} {1} material {2} range: [{3}, {4})={5} step {6}]",
			id, direction, material, startStep, endStep, length, Text.S(length));
	}
	
	/// <summary>
	/// Ensures the arc has a non-empty material and correctly-set steps.
	/// </summary>
	public bool Validate() {
		// NOTE: Don't check motor != null
		//	&& (motor.id > 3 ? material != 0 : material == 0) 
		//  && direction != StepDirection.Unknown.
		// These will be assigned later.
		
		return startStep >= 0
			&& endStep > startStep
			&& endStep != kNotInitialized
			&& startStep != kNotInitialized;
	}
	
	public static int CompareArcs(Arc lhs, Arc rhs) {
		Contract.Assert(lhs != null, @"Cannot sort null arc.");
		Contract.Assert(rhs != null, @"Cannot sort null arc.");
		
		if (lhs.startStep < rhs.startStep) {
			Contract.Assert(lhs.endStep <= rhs.startStep || lhs.material != rhs.material, 
			                @"Can't sort overlapping arcs of same material {0} and {1}.",
							lhs, rhs);
			return -1;
		}
		if (lhs.startStep > rhs.startStep) {
			Contract.Assert(rhs.endStep <= lhs.startStep || lhs.material != rhs.material, 
			                @"Can't sort overlapping arcs of same material {0} and {1}.",
							lhs, rhs);
			return 1;
		}
		
		Contract.Assert((lhs.startStep == rhs.startStep
						&& lhs.endStep == rhs.endStep)
		                || lhs.material != rhs.material, 
		                @"Overlapping arcs of same material: {0} and {1}.",
						lhs, rhs);

		return 0;
	}
}
