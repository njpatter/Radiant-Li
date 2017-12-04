using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MotorRateMover {
	public const int kMovementMotorCount = 4;
	
	public PrinterMotor motor;
	public StepDirection direction;
	public StepSize size;
	public int stepRate;
	
	public int globalStartStep;
	public int stepCount;
	
	public int globalEndStep {
		get {
			return globalStartStep + stepCount;
		}
	}
	
	public MotorRateMover(PrinterExtruder anExtruder, StepDirection aDirection, 
		StepSize aSize, int aRate, int aGlobalStepStart, int totalStepCount) 
	{
		motor = (PrinterMotor)anExtruder;
		direction = aDirection;
		size = aSize;
		stepRate = aRate;
		globalStartStep = aGlobalStepStart;
		stepCount = totalStepCount;
	}
	
	public MotorRateMover(PrinterMotor aMotor, StepDirection aDirection, 
		StepSize aSize, int aRate, int aGlobalStepStart, int totalStepCount) 
	{
		motor = aMotor;
		direction = aDirection;
		size = aSize;
		stepRate = aRate;
		globalStartStep = aGlobalStepStart;
		stepCount = totalStepCount;
	}
	
	public MotorRateMover(MotorRateMover aMover, int aGlobalStart) {
		motor = aMover.motor;
		direction = aMover.direction;
		size = aMover.size;
		stepRate = aMover.stepRate;
		globalStartStep = aGlobalStart;
		stepCount = aMover.stepCount;
	}
	
	public bool PushSizeAndDirectionToMotor() {
		bool test = (motor.stepSize != size || motor.stepDirection != direction);
		motor.stepSize = size;
		motor.stepDirection = direction;
		return test;
	}
	
	public override string ToString() {
		return string.Format("<<MotorRateMover for motor id {0}: Starting at step {1}, " +
			"ending at step {2} with a total of {3} {5} step(s) @1 step per {4} global steps>>", 
			motor.id, globalStartStep, globalEndStep, stepCount, stepRate, direction);
	}
	
}