using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class TickProfile : System.Object {
	#region Constants
	public const int kCyclesPerSecond      = 80000000;
	public const int kMotorDelayInCycles   = 500;
	const float kSecPerSqrCycles    = (float)kCyclesPerSecond / (float)kMotorDelayInCycles;
	public const int kPressureSteps = 150; // 1125 steps are required to get out of the printhead.
	const int kMinStepScaleForAcceleration = 12;

	const float kConstantStepRateScale = 1.1f;
	const int kSmallArcLimit = 60;
	const float kSmallArcRateScale = 2f;
	const int kEarlyLeakageBackupCap = 60;
	#endregion

	public PrinterMotor motor;

	public StepSize size;
	public StepDirection direction;
	public int stepRate;

	public int startTick;
	public int tickLength;
	public int endTick {
		get { return startTick + tickLength; }
	}

	#region Initialization & cleanup.
	/// <summary>
	/// Converts the queue of arcs into a list of tick profiles
	/// using the provided platform step-rate for conversion and
	/// optionally with acceleration.
	/// </summary>
	/// <param name='arcs'>
	/// The arc source.
	/// </param>
	/// <param name='toList'>
	/// The target list.
	/// </param>
	/// <param name='usingPlatformStepRate'>
	/// The platform step rate.
	/// </param>
	/// <param name='withAcceleration'>
	/// True for acceleration, false for a constant speed.
	/// </param>
	public static void Convert(List<Arc> fromArcs, List<TickProfile> toList, 
	                           int usingPlatformStepRate)
	{
		toList.Clear();

		// NOTE(kevin): Could try merging arcs, but this is low
		// priority.
		for (int aI = 0; aI < fromArcs.Count; aI++) {
			Arc a = fromArcs[aI];
			if (a.length == 0) {
				continue;
			}

			Contract.Assert(a.motor != null, @"No motor assigned for arc {0}.", a);
			TickProfile result = new TickProfile(a, usingPlatformStepRate, usingPlatformStepRate);
			toList.Add(result);
		}

		fromArcs.Clear();
	}

	public static void ConvertExtruder(List<Arc> arcs, List<TickProfile> toList,
		int usingPlatformStepRate, int stepsPerPlatformRotation,
		int withInitialPressureSteps, int withLastPlatformStep,
		out int shiftInTicksRequired, out int depressureRequested)
	{
		Contract.Assert(arcs.Count > 0, @"Called with empty arcs; should be handled prior.");

		int extrusionRate = ((PrinterExtruder)
			(arcs[0].motor)).SetExtrusionRate(stepsPerPlatformRotation
				* usingPlatformStepRate,
			arcs.Count);

		// How far before the initial tick do we start?
		int initialPressureTick = 0;
		if (withInitialPressureSteps > 0) {
			initialPressureTick = Pressurize(arcs[0], withInitialPressureSteps, usingPlatformStepRate, toList);
		}
		shiftInTicksRequired = Mathf.Max(-initialPressureTick, 0);
		
		Extrude(arcs[0], extrusionRate, usingPlatformStepRate, toList);
		for (int i = 1; i < arcs.Count; ++i) {
			Arc lastArc = arcs[i - 1];
			Arc thisArc = arcs[i];

			int halfSpaceAvailable = (thisArc.startStep - lastArc.endStep) / 2;
			int pressureSteps      = Mathf.Min(halfSpaceAvailable, kPressureSteps);
			Contract.Assert(pressureSteps >= 0, @"Expected positive pressure steps, not {0}, between {1} and {2}.",
				pressureSteps, lastArc, thisArc);
			Contract.Assert(pressureSteps <= kPressureSteps,
				@"Pressure steps too large: {0}.", pressureSteps);

			if (lastArc.hasVariableStepRate && thisArc.hasVariableStepRate) {
				pressureSteps = 0;
			}

			if (pressureSteps > 0) {
				Depressurize(lastArc, pressureSteps, usingPlatformStepRate, toList);
				Pressurize(thisArc, pressureSteps, usingPlatformStepRate, toList);
			}

			Extrude(thisArc, extrusionRate, usingPlatformStepRate, toList);
		}
		Arc finalArc = arcs[arcs.Count - 1];
		int depressureAvailable = Mathf.Min(kPressureSteps,
			Mathf.Max(withLastPlatformStep - finalArc.endStep, 0));

		if (depressureAvailable > 0) {
			Depressurize(arcs[arcs.Count - 1], depressureAvailable,
				usingPlatformStepRate, toList);
		}

		// Return the untaken depressure steps.
		depressureRequested = kPressureSteps - depressureAvailable;

#if UNITY_EDITOR
		for (int i = 1; i < toList.Count; ++i) {
			TickProfile p0 = toList[i - 1];
			TickProfile p1 = toList[i];

			Contract.Assert(p0.endTick <= p1.startTick,
				@"Created overlapping profiles {0} and {1}", p0, p1);
		}
#endif
	}

	public static int CompleteDepressurization(PrinterMotor forMotor, int overSteps,
		int usingPlatformStepRate, List<TickProfile> toList)
	{
		// No depressure remaining? Don't care, then.
		if (overSteps < 1) return 0;

		int ticksTaken = ApplyPressure(forMotor, StepDirection.Cw, overSteps,
			usingPlatformStepRate, toList);

		Contract.Assert(ticksTaken > 0,
			@"No ticks required for depressurizing {0} by {1} step{2}.",
			forMotor.id, overSteps, Text.S(overSteps));
		Contract.Assert(toList[0].startTick == 0,
			@"Depressure {0} doesn't start at 0.", toList[0]);

		// How long we actually took to depresurize, worst case.
		return Mathf.CeilToInt((float)ticksTaken / (float)usingPlatformStepRate);
	}

	#region Constructors
	TickProfile(Arc sourceArc, int stepToTickRate, int tickRate) {
		Contract.Assert(tickRate > 0, @"Zero tick rate for {0}.", sourceArc);

		motor     = sourceArc.motor;
		size      = sourceArc.motor.stepSize;
		direction = sourceArc.direction;

		stepRate   = tickRate;
		// Sync the start with the platform conversion rate.
		startTick  = sourceArc.startStep * stepToTickRate;

		// Figure out how many steps we can actually take at our rate.
		int stepsAvailable = Mathf.FloorToInt((float)(sourceArc.length * stepToTickRate) / (float)tickRate);
		tickLength = stepsAvailable * tickRate;

		// If we were supposed to take a step in the arc,
		// try to take at least one step.
		if (sourceArc.length > 0) tickLength = Mathf.Max(1, tickLength);

		Contract.Assert(tickLength % tickRate == 0 || tickLength < tickRate,
			@"Non-integral tick rate for {0} from {1}.",
			this, sourceArc);
		Contract.Assert((sourceArc.length == 0 && tickLength == 0)
			|| (tickLength > 0), @"Should take at least one tick step for {0} => {1}",
			sourceArc, this);
	}

	public TickProfile(TickProfile source, int aStartTick) {
		motor     = source.motor;
		size      = source.size;
		direction = source.direction;
		stepRate  = source.stepRate;

		startTick  = aStartTick;
		tickLength = source.tickLength;

		// NOTE: Negative tick profiles are OK
		// when pressurizing, so we don't
		// check for negative start ticks.
		Contract.Assert(tickLength >= 0, @"Negative tick length.");
		Contract.Assert(motor != null, @"No motor assigned to tick profile.");
		Contract.Assert(direction != StepDirection.Unknown, @"Unknown step direction.");

	}

	public TickProfile(PrinterMotor aMotor, StepSize aSize, StepDirection aDirection,
		int aStepRate, int aStartTick, int aTickLength)
	{
		Contract.Assert(aMotor != null,
			@"Tried to assign null motor to tick profile.");
		Contract.Assert(aDirection != StepDirection.Unknown,
			@"Tried to assign unknown direction to tick profile.");
		Contract.Assert(aStepRate >= 0,
			@"Negative step rate of {0}.", aStepRate);
		// NOTE: Negative tick profiles are OK
		// when pressurizing, so we don't
		// check for negative start ticks.

		motor = aMotor;
		size = aSize;
		direction = aDirection;
		stepRate = aStepRate;

		startTick = aStartTick;
		tickLength = aTickLength;
	}
	#endregion
	#endregion

	#region Operators
	public void Add(int tickOffset) {
		startTick += tickOffset;
	}
	#endregion

	#region Acceleration
	/// <summary>
	/// Handles even and odd distances to accelerate.
	/// </summary>
	delegate void MiddlePatternHandler(List<TickProfile> forList, ref int totalTicksWaited);

	static void Extrude(Arc anArc, int atRate, int usingPlatformStepRate, List<TickProfile> forList) {
		Contract.Assert(atRate > 0, @"Expected positive extrusion rate, not {0}.", atRate);

		PrinterExtruder extruder = (PrinterExtruder)anArc.motor;
		if (!anArc.hasVariableStepRate) {
			extruder.stepsExtruded += anArc.length;

			// Scale small arc step rate by a constant.
			if (anArc.length < kSmallArcLimit ) {
				atRate = Mathf.RoundToInt(atRate / kSmallArcRateScale);
			}

			TickProfile result = new TickProfile(anArc, usingPlatformStepRate, atRate);
			if (result.tickLength > 0) forList.Add(result);
		}
		else {
			extruder.stepsExtruded += anArc.variableStepLocations.Length;
			foreach(int singleArc in anArc.variableStepLocations) {
				Arc aNewArc = new Arc(anArc);
				aNewArc.startStep = anArc.startStep + singleArc;
				aNewArc.endStep = aNewArc.startStep + 1;
				TickProfile result = new TickProfile(aNewArc, usingPlatformStepRate, usingPlatformStepRate);
				if (result.tickLength > 0) forList.Add(result);
				else Text.Error("Came up with a tickprofile with no movement while processing arc " +aNewArc);
				//Text.Log("For " + aNewArc + " we came up with profile " + result);
			}
		}
	}

	/// <summary>
	/// Optionally accelerates the specified arc using the provided platform step rate
	/// to calculate the initial tick location. Results are added to the provided list.
	/// If the arc is small, an unaccelerated TickProfile is added to the list instead.
	/// </summary>
	/// <param name='anArc'>
	/// An arc to accelerate.
	/// </param>
	/// <param name='usingPlatformStepRate'>
	/// The platform's step-rate.
	/// </param>
	/// <param name='forList'>
	/// The list to update.
	/// </param>
	static int Accelerate(Arc anArc, int usingPlatformStepRate, List<TickProfile> forList) {
		Contract.Assert(usingPlatformStepRate > 0,
			@"Expected a positive platform step rate, not {0}.", usingPlatformStepRate);
		
			TickProfile constantTick = new TickProfile(anArc, usingPlatformStepRate, PrinterExtruder.kDefaultTickRate);
			if (constantTick.tickLength > 0) {
				forList.Add(constantTick);
			}
			return constantTick.tickLength;
	}

	/// <summary>
	/// Does nothing, as even distances don't require additional patterns.
	/// </summary>
	/// <returns>
	/// The number of new patterns added.
	/// </returns>
	/// <param name='forList'>
	/// THe list to add new patterns to if required.
	/// </param>
	/// <param name='totalTicksWaited'>
	/// The total number of ticks we've waited thus far.
	/// </param>
	static void HandleEvenPattern(List<TickProfile> forList, ref int totalTicksWaited)
	{
		// Instead of having two additional tick profiles, just make one longer one.
		TickProfile lastProfile = forList[forList.Count - 1];

		totalTicksWaited += lastProfile.tickLength;
		lastProfile.tickLength *= 2;
	}

	/// <summary>
	/// Inserts the extra required step for odd distances.
	/// </summary>
	/// <returns>
	/// The number of new patterns added.
	/// </returns>
	/// <param name='forList'>
	/// The list to add new patterns to if required.
	/// </param>
	/// <param name='totalTicksWaited'>
	/// The total number of ticks we've waited thus far.
	/// </param>
	static void HandleOddPattern(List<TickProfile> forList, ref int totalTicksWaited) {
		// Step rate is one more in the future; we also create a gap
		// for the odd value, below.
		TickProfile lastProfile = forList[forList.Count - 1];

		int additionalTicks = lastProfile.tickLength + lastProfile.stepRate;
		lastProfile.tickLength += additionalTicks;
		totalTicksWaited += additionalTicks;
	}
	#endregion

	#region Pressure
	static int Pressurize(Arc anArc, int usingPressureSteps, int usingPlatformStepRate,
		List<TickProfile> toList)
	{
		Contract.Assert(usingPressureSteps > 0, "Expected positive pressure steps, not {0}", usingPressureSteps);

		int pressureStart = toList.Count;
		int ticksTaken    = ApplyPressure(anArc.motor, StepDirection.Ccw, usingPressureSteps,
			usingPlatformStepRate, toList);
		int pressureEnd   = toList.Count;

		Contract.Assert(pressureStart < pressureEnd,
			@"No tick profiles created for pressure over {0} step{1}.",
			usingPressureSteps, Text.S(usingPressureSteps));

		// Pressurizing takes place before the arc…
		int arcOrigin = anArc.startStep * usingPlatformStepRate;
		int newOrigin = arcOrigin - ticksTaken;

		// The first profile should be at 0.
		Contract.Assert(toList[pressureStart].startTick == 0,
			@"First pressure arc should be 0 pre-translation: {0}.",
			toList[pressureStart]);

		for (int i = pressureStart; i < pressureEnd; ++i) {
			toList[i].Add(newOrigin);
			Contract.Assert(toList[i].startTick < arcOrigin,
				@"Pressure tick {0} starts after extrusion arc {1}.",
				toList[i], anArc);
			Contract.Assert(toList[i].endTick <= arcOrigin,
				@"Pressure tick {0} ends after extrusion arc {1}.",
				toList[i], anArc);
		}

		Contract.Assert(toList[pressureStart].startTick == newOrigin,
			@"First pressure arc's start should be {0}: {1}.", newOrigin,
			toList[pressureStart]);
		Contract.Assert(toList[toList.Count - 1].endTick == arcOrigin,
			@"Pressure doesn't stop at extrusion arc {0}.", anArc);

		return toList[pressureStart].startTick;
	}

	static void Depressurize(Arc anArc, int usingPressureSteps, int usingPlatformStepRate,
		List<TickProfile> toList)
	{
		Contract.Assert(usingPressureSteps > 0, "Expected positive pressure steps, not {0}", usingPressureSteps);

		int pressureStart = toList.Count;
		int ticksTaken    = ApplyPressure(anArc.motor, StepDirection.Cw, usingPressureSteps,
			usingPlatformStepRate, toList);
		int pressureEnd   = toList.Count;

		Contract.Assert(pressureStart < pressureEnd,
			@"No tick profiles created for pressure over {0} step{1}.",
			usingPressureSteps, Text.S(usingPressureSteps));
		Contract.Assert(ticksTaken > 0,
			@"No ticks required for depressurizing {0} over {1} step{2}.",
			anArc, usingPressureSteps, Text.S (usingPressureSteps));

		// Pressurizing takes place before the arc…
		int newOrigin = anArc.endStep * usingPlatformStepRate;
		for (int i = pressureStart; i < pressureEnd; ++i) {
			toList[i].Add(newOrigin);
		}

		Contract.Assert(toList[pressureStart].startTick == newOrigin,
			@"Depressure {0} doesn't start at extrusion arc end: {1}",
			toList[pressureStart].startTick, newOrigin);
	}

	static int ApplyPressure(PrinterMotor forMotor, StepDirection inDirection,
		int usingPressureSteps, int usingPlatformRotation, List<TickProfile> toList)
	{
		Contract.Assert(usingPressureSteps <= kPressureSteps,
			@"Requested {0} pressure steps; maximum is {1}.",
			usingPressureSteps, kPressureSteps);

		// NOTE: We don't know how many steps it'll take to
		// accelerate over usingPressureSteps. So we need
		// to treat everything as if it stepped from
		// [0, usingPressureSteps) and then scale the
		// resulting ticks by anArc.startingStep.
		Arc pressureArc = new Arc(forMotor, 0);
		pressureArc.startStep = 0;
		pressureArc.endStep   = usingPressureSteps;
		pressureArc.direction = inDirection;

		PrinterExtruder extruder = (PrinterExtruder)forMotor;
		if (inDirection == StepDirection.Ccw) {
			extruder.pressureRequired -= usingPressureSteps;

			Contract.Assert(extruder.pressureRequired >= 0,
				@"Pressurized more than {0} steps.", kPressureSteps);
		}
		else {
			extruder.pressureRequired += usingPressureSteps;

			Contract.Assert(extruder.pressureRequired <= kPressureSteps,
				@"Motor {0} required pressure of {1} exceeds max of {2}.",
				extruder.id, extruder.pressureRequired, kPressureSteps);
		}

		int ticksTaken = Accelerate(pressureArc, usingPlatformRotation, toList);
		Contract.Assert(ticksTaken > 0, @"Non-positive ticks taken over {0} step{1}.",
		                ticksTaken, Text.S(ticksTaken));
		return ticksTaken;
	}
	#endregion

	#region Override
	/// <summary>
	/// Returns a <see cref="System.String"/> that represents the current <see cref="TickProfile"/>.
	/// </summary>
	/// <returns>
	/// A <see cref="System.String"/> that represents the current <see cref="TickProfile"/>.
	/// </returns>
	public override string ToString() {
		string id = motor != null ? motor.id.ToString() : "null";
		return string.Format("[TickProfile motor {0} {1} range: [{2}, {3})={4} step{5} @{6}]",
			id, direction, startTick, endTick, tickLength, Text.S(tickLength), stepRate);
	}
	#endregion
}
