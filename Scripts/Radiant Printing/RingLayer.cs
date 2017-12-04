using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Converted arcs from a voxel blob.
/// </summary>
public class RingLayer : System.Object {
	#region Constants
	/// <summary>
	/// Scales the samples per layer; higher = more samples.
	/// </summary>
	const float kSamplingScale = 32.0f;

	/// <summary>
	/// Determines how close a sample needs to be for
	/// us to include it in an arc. Smaller is stricter.
	/// </summary>
	const float kSampleThresholdSqr = 0.501f * 0.501f;
	#endregion

	#region Variables & properties
	/// <summary>
	/// The total number of arcs found in this layer.
	/// </summary>
	/// <value>
	/// The arc count.
	/// </value>
	public int arcCount {
		get { return m_arcCount; }
	}
	int m_arcCount;

	/// <summary>
	/// Returns the total length of all assigned arcs in steps.
	/// </summary>
	/// <value>
	/// The arc length in steps.2
	/// </value>
	public int arcLengthInSteps {
		// Note that since latestStep is exclusive, we don't need
		// to add one here.
		get { return m_latestStep - m_earliestStep; }
	}

	/// <summary>
	/// Returns the earliest step for the current
	/// set of assignments.
	/// </summary>
	/// <value>
	/// The earliest step.
	/// </value>
	public int earliestStep {
		get { return m_earliestStep; }
	}
	int m_earliestStep;

	/// <summary>
	/// Returns the inner-most ring number that contains
	/// arcs.
	/// </summary>
	/// <value>
	/// The inner ring.
	/// </value>
	public int innerRing {
		get { return m_innerRing; }
	}
	int m_innerRing;

	/// <summary>
	/// Returns the latest step for the current
	/// set of assignments.
	/// </summary>
	/// <value>
	/// The latest step.
	/// </value>
	public int latestStep {
		get { return m_latestStep; }
	}
	int m_latestStep;

	/// <summary>
	/// Returns the total width of the space to be printed.
	/// </summary>
	/// <value>
	/// The width of the layer.
	/// </value>
	public int layerWidthInRings {
		get { return m_ringCount == 0 ? 0 : outerRing - innerRing + 1; }
	}

	/// <summary>
	/// Returns the outer-most ring number that contains
	/// arcs.
	/// </summary>
	/// <value>
	/// The outer ring.
	/// </value>
	public int outerRing {
		get { return m_outerRing; }
	}
	int m_outerRing;

	/// <summary>
	/// The number of rings that require printing in the layer.
	/// </summary>
	/// <value>
	/// The ring count.
	/// </value>
	public int ringCount {
		get { return m_ringCount; }
	}
	int m_ringCount;

	/// <summary>
	/// Inverse infill amount; larger numbers -> less infill.
	/// </summary>
#if SURFACING
	public int infillStep = 2;
#else
	public int infillStep = 1;
#endif

	/// <summary>
	/// The arcs that comprise the layer, indexed by the
	/// ring number.
	/// </summary>
	List<Arc>[] m_arcs;

	/// <summary>
	/// The maximum number of rings this layer could have,
	/// based on the nozzle width.
	/// </summary>
	int m_maxRings;

	/// <summary>
	/// The printer state used for conversion.
	/// </summary>
	Printer m_printer;

	/// <summary>
	/// Returns the innermost ring for the given material.
	/// </summary>
	/// <returns>The ring for material.</returns>
	/// <param name="aMaterial">A material.</param>
	public int innerRingForMaterial(byte aMaterial) {
		int value = -1;
		for(int aRing = 0; aRing < m_arcs.Length; aRing++) {
			foreach(Arc anArc in m_arcs[aRing]) {
				if (anArc.material == aMaterial) return aRing;
			}
		}
		return value;
	}

	/// <summary>
	/// Outers the ring for material.
	/// </summary>
	/// <returns>The ring for material.</returns>
	/// <param name="aMaterial">A material.</param>
	public int outerRingForMaterial(byte aMaterial) {
		int value = -1;
		for(int aRing = m_arcs.Length - 1; aRing > -1; aRing--) {
			foreach(Arc anArc in m_arcs[aRing]) {
				if (anArc.material == aMaterial) return aRing;
			}
		}
		return value;
	}
	#endregion

	#region Initialization & cleanup
	/// <summary>
	/// Initializes a new instance of the <see cref="RingLayer"/> class.
	/// </summary>
	/// <param name='aPrinter'>
	/// The current printer state.
	/// </param>
	/// <param name='numVoxelsPerSide'>
	/// The number of voxels found per axis; assumes a çube.
	/// </param>
	/// <param name='voxelSizeInMm'>
	/// The voxel size in mm.
	/// </param>
	public RingLayer(Printer aPrinter, int numVoxelsPerSide, float voxelSizeInMm) {
		// Rings use radius instead of diameter, thus the 0.5f.
		// NOTE: We're using Ceil as this is exclusive.
		m_maxRings = Mathf.CeilToInt(Mathf.Sqrt((float)numVoxelsPerSide * numVoxelsPerSide * 2.0f)
			* 0.5f * voxelSizeInMm / aPrinter.nozzleWidthInMm);

		m_printer = aPrinter;
		m_arcs = new List<Arc>[m_maxRings];
		m_ringCount = m_arcCount = 0;

		for (int i = 0; i < m_maxRings; ++i) {
			m_arcs[i] = new List<Arc>();
		}
	}
	#endregion

	#region Conversion
	public IEnumerator CreateArcTest(VoxelRegion region) {
		m_innerRing = int.MaxValue;
		m_outerRing = int.MinValue;

		Clear();

		Text.Log(@"Max rings: {0}=========================<<<<<<<<<<<<<<<<<<<<<<<<", m_maxRings);

		// NOTE:
		// This assumes ring 0 is at the origin of the plate;
		// a slightly better version could have ring 0 start
		// nozzleSize * 0.5f mm away from the origin. This
		// would help account for the slight spill over from
		// the nozzle into adjacent rings…
		for (int forRing = 0; forRing < 72; forRing += 3) {
			Arc testingArc = Extend(null, (byte)1, 0000, forRing);
			Close(testingArc, 1600, forRing);
			Text.Log(@"Ring {0}: Added testing arc {1}.", forRing, testingArc);
			UpdateRingBounds(forRing);

			if (Scheduler.ShouldYield()) yield return null;
		}

		Contract.Assert(m_ringCount <= m_maxRings,
			@"Ring count ({0}) is larger than max rings ({1}).",
			m_ringCount, m_maxRings);
		Contract.Assert(m_ringCount <= layerWidthInRings,
			@"Ring count of {0} larger than width of {1}.",
			m_ringCount, layerWidthInRings);
	}

	bool AreRingsCloseAt(int aRing, int nearTargetRing) {
		return Mathf.Abs(aRing - nearTargetRing) < 4;
	}
	
	/// <summary>
	/// Converts a layer of voxels to a layer of arcs.
	/// </summary>
	/// <param name='aBlob'>
	/// A 3D voxel blob.
	/// </param>
	/// <param name='atLayer'>
	/// The layer to sample at.
	/// </param>
	public IEnumerator ConvertVoxelLayer(VoxelRegion region) {
		m_innerRing = int.MaxValue;
		m_outerRing = int.MinValue;

		Clear();

		// NOTE:
		// This assumes ring 0 is at the origin of the plate;
		// a slightly better version could have ring 0 start
		// nozzleSize * 0.5f mm away from the origin. This
		// would help account for the slight spill over from
		// the nozzle into adjacent rings…
		for (int forRing = 0; forRing < m_maxRings; forRing += infillStep) {
			SampleArcsFrom(region, forRing);

			if (Scheduler.ShouldYield()) yield return null;
		}

		// NOTE for TESTING
		// If we didn't added anything for ring 0, add a complete ring.
		if (m_arcs[0].Count == 0) {
			int origin = Mathf.RoundToInt(region.GetSampleFromMm(m_printer.platformRadiusInMm));
			byte material = region.IsValidPoint(origin, origin)
				? region[origin, origin] : (byte)0;
			Arc currentArc = null;
			Extend(currentArc, material, 0, 0);
			Extend(currentArc, 0, m_printer.platform.stepsPerRotation, 0);
			UpdateRingBounds(0);
		}
		else Text.Log(@"No worries!");
		//*/

		Contract.Assert(m_ringCount <= m_maxRings,
			@"Ring count ({0}) is larger than max rings ({1}).",
			m_ringCount, m_maxRings);
		Contract.Assert(m_ringCount <= layerWidthInRings,
			@"Ring count of {0} larger than width of {1}.",
			m_ringCount, layerWidthInRings);
	}

	/// <summary>
	/// Samples the blob for the given [ring, layer] by sampling
	/// kSamplingScale * the circumference points moving in a
	/// positive (ccw) angle.
	/// </summary>
	/// <param name='aBlob'>
	/// A BLOB.
	/// </param>
	/// <param name='atLayer'>
	/// At layer.
	/// </param>
	/// <param name='forRing'>
	/// For ring.
	/// </param>
	void SampleArcsFrom(VoxelRegion region, int forRing) {
		Contract.Assert(forRing >= 0, @"Negative ring: {0}", forRing);
		Contract.Assert(forRing < m_maxRings, @"Ring {0} out of bounds.", forRing);

		float platformStepInDeg = m_printer.platform.degreesPerStep;
		int   stepsPerRotation  = m_printer.platform.stepsPerRotation;

		int   samplesToTake       = Mathf.CeilToInt(forRing * MathUtil.kTau * kSamplingScale);
		float sampleSizeInRadians = MathUtil.kTau / samplesToTake;

		Arc currentArc = null;

		// Origin of the platform, in voxels.
		int origin = Mathf.RoundToInt(region.GetSampleFromMm(m_printer.platformRadiusInMm));

		// Rings -> Mm -> Voxels
		float voxelRadius = region.GetSampleFromMm(forRing * m_printer.nozzleWidthInMm);

		for (int sampleIndex = 0; sampleIndex < samplesToTake; ++sampleIndex) {
			float radians = sampleIndex * sampleSizeInRadians;

			// Since the number of samples we're taking depends on the radius,
			// we can't pre-compute the angles and use them for everything…
			float sampleX = voxelRadius * Mathf.Cos(radians);
			float sampleY = voxelRadius * Mathf.Sin(radians);

			// NOTE: This should probably be rounded, not floored.
			int sampleXInt = Mathf.RoundToInt(sampleX);
			int sampleYInt = Mathf.RoundToInt(sampleY);

			float sampleXFractional = sampleX - sampleXInt;
			float sampleYFractional = sampleY - sampleYInt;

			float xFractionalSqr = sampleXFractional * sampleXFractional;
			float yFractionalSqr = sampleYFractional * sampleYFractional;

			// If the sample is sufficiently good, then take the sample
			// and update the arc.
			if (xFractionalSqr + yFractionalSqr <= kSampleThresholdSqr) {
				int sampleAtWidth = sampleXInt + origin;
				int sampleAtDepth = sampleYInt + origin;
				byte material = region.IsValidPoint(sampleAtWidth, sampleAtDepth)
					? region[sampleAtWidth, sampleAtDepth]
					: (byte)0;
				// NOTE: If we round to int, then we can get the same step
				// number even if we sample different points! Doing so
				// could lead to 0-length arcs.
				int platformStep = Mathf.FloorToInt(radians * Mathf.Rad2Deg / platformStepInDeg);
				platformStep = Mathf.Min(platformStep, stepsPerRotation);

				currentArc = Extend(currentArc, material, platformStep, forRing);
			}
		}

		// Close off the arc if open.
		Extend(currentArc, 0, stepsPerRotation, forRing);
		UpdateRingBounds(forRing);
	}

	/// <summary>
	/// Updates the inner and outer rings of the area to print.
	/// </summary>
	/// <param name='forRing'>
	/// For ring.
	/// </param>
	void UpdateRingBounds(int forRing) {
		if (m_arcs[forRing].Count > 0) {
			m_innerRing = Mathf.Min(m_innerRing, forRing);
			m_outerRing = Mathf.Max(m_outerRing, forRing);
			++m_ringCount;
		}
	}

	#endregion

	#region Arc management
	/// <summary>
	/// Returns the number of arcs remaining in the specified ring.
	/// </summary>
	/// <returns>
	/// The arc count.
	/// </returns>
	/// <param name='aRing'>
	/// A ring, may be positive or negative.
	/// </param>
	public int ArcsRemainingIn(int aRing) {
		int targetRing = Mathf.Abs(aRing);
		if (targetRing >= m_maxRings) return 0;

		return m_arcs[targetRing].Count;
	}

	/// <summary>
	/// Returns true if arcs exist at the requested ring.
	/// </summary>
	/// <returns>
	/// The arcs.
	/// </returns>
	/// <param name='atRing'>
	/// If set to <c>true</c> at ring.
	/// </param>
	public bool AreArcs(int atRing, int ofMaterial) {
		// NOTE: atRing can be positive or negative
		// since it's a relative ring.
		atRing = Mathf.Abs(atRing);

		if (atRing < m_maxRings) {
			foreach (Arc anArc in m_arcs[atRing]) {
				if (anArc.material == ofMaterial) return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Clears the arcs from each ring.
	/// </summary>
	void Clear() {
		foreach (List<Arc> arcList in m_arcs) {
			arcList.Clear();
		}
		m_ringCount = 0;
		m_arcCount = 0;
	}

	/// <summary>
	/// Terminates the provided arc, enqueueing it at
	/// the appropriate ring and updating the arc count.
	/// </summary>
	/// <param name='anArc'>
	/// An arc to close.
	/// </param>
	/// <param name='atStep'>
	/// The arc's final (non-step-rate) platform step.
	/// </param>
	/// <param name='forRing'>
	/// The ring of the arc.
	/// </param>
	void Close(Arc anArc, int atStep, int forRing) {
		Contract.Assert(anArc.motor == null,
			@"Motor assigned too early to arc {0}.", anArc);
		Contract.Assert(anArc.direction != StepDirection.Unknown,
			@"Motor direction not assigned.");
		Contract.Assert(anArc.startStep != Arc.kNotInitialized,
			@"Missing start step for {0}.", anArc);
		Contract.Assert(anArc.startStep >= 0, @"Negative start step for {0}.",
			anArc);

		if (atStep == anArc.startStep) {
			// Zero-length arc; ignore it.
			return;
		}

		anArc.endStep = atStep;
		m_arcs[forRing].Add(anArc);
		++m_arcCount;

		Contract.Assert(anArc.endStep != Arc.kNotInitialized,
			@"Missing end step for {0}.", anArc);
		Contract.Assert(anArc.endStep >= 0, @"Negative end step for {0}.",
			anArc);
		Contract.Assert(anArc.endStep > anArc.startStep,
			@"Failed to close arc: start occurs after end in {0}.", anArc);
		Contract.Assert(anArc.endStep <= m_printer.platform.stepsPerRotation,
			@"Arc exceeds platform rotation of {0}.", m_printer.platform.stepsPerRotation);

		if (!anArc.Validate()) Text.Error(@"Invalid arc: {0}.", anArc);
	}

	/// <summary>
	/// Extends the provided current arc if possible or enqueues
	/// it and starts a new arc if necessary.
	/// </summary>
	/// <param name='currentArc'>
	/// The arc currently being processed.
	/// </param>
	/// <param name='withMaterial'>
	/// The material of the new arc.
	/// </param>
	/// <param name='atStep'>
	/// Our current platform step value EXCLUDING step-rate.
	/// </param>
	/// <param name='forRing'>
	/// The ring we're currently processing.
	/// </param>
	Arc Extend(Arc currentArc, byte withMaterial, int atStep, int forRing) {
		Arc result;

		// Close the arc, if any, when we hit 'nothing'.
		if (withMaterial == 0) {
			if (currentArc != null) {
				// NOTE: We're using atStep here instead
				// of currentArc.endStep since endStep is
				// EXCLUSIVE; essentially, we won't want
				// to print beyond this number, but we
				// want to print as much as we can up-to
				// it based on our (later) step-rate.
				Close(currentArc, atStep, forRing);
			}
			result = null;
		}
		// Start a new arc if we haven't already.
		else if (currentArc == null) {
			result = new Arc(withMaterial, atStep);
			result.direction = StepDirection.Ccw;
		}
		// Extend the current arc since the material is the same.
		else if (currentArc.material == withMaterial) {
			currentArc.endStep = atStep;
			result = currentArc;
		}
		// Different material; save the current arc and start a
		// new one.
		else {
			Close(currentArc, atStep, forRing);
			result = new Arc(withMaterial, atStep);
			result.direction = StepDirection.Ccw;
		}

		return result;
	}

	#region Extracting Arcs
	/// <summary>
	/// Fills the provided queue with normalized arcs for the
	/// provided extruder. Should be called after StartExtraction().
	/// </summary>
	/// <param name='anExtruder'>
	/// The extruder we're searching for.
	/// </param>
	/// <param name='destination'>
	/// The final arc location.
	/// </param>
	public void ExtractArcsFor(PrinterExtruder anExtruder, List<Arc>[] destination) {
		int atRing = Mathf.Abs(anExtruder.relativeRing);

		// We're done if it's more than the outer ring.
		if (atRing > m_outerRing) return;

		List<Arc> arcs = m_arcs[atRing];

		int initialArcCount = m_arcCount;
		for (int anArcId = 0; anArcId < arcs.Count; /* NOP */) {
			Arc anArc = arcs[anArcId];

			if (anArc.motor == anExtruder) {
				destination[anExtruder.id].Add(anArc);
				arcs.RemoveAt(anArcId);

				m_earliestStep = Mathf.Min(m_earliestStep, anArc.startStep);
				m_latestStep   = Mathf.Max(m_latestStep, anArc.endStep);
				--m_arcCount;
			}
			else ++anArcId;
		}

		Contract.Assert(m_arcCount == initialArcCount ? true : m_earliestStep <= m_latestStep,
			@"Ring {0}: Earliest step ({1}) larger than latest step ({2})",
			atRing, m_earliestStep, m_latestStep);

		// Did we empty a ring?
		if (m_arcCount < initialArcCount && arcs.Count == 0) {
			--m_ringCount;
		}
	}

	/// <summary>
	/// Signal the start of extracting arcs; should be called
	/// before calling ExtractArcsFor().
	/// </summary>
	public void StartExtraction() {
		m_earliestStep = int.MaxValue;
		m_latestStep = int.MinValue;
	}
	#endregion

	/// <summary>
	/// Sets arcs so their start is relative to extruder positions.
	/// </summary>
	/// <param name='forRing'>
	/// The ring we're normalizing.
	/// </param>
	/// <param name='usingStepsPerRotation'>
	/// Steps required for 1 platform rotation (excluding step-rate).
	/// </param>
	public void NormalizeArcs(int forRing, int usingStepsPerRotation, int currentPlatformRotation) {
		Contract.Assert(forRing >= 0,
			@"Expected a positive ring, not {0}.", forRing);
		Contract.Assert(forRing < m_maxRings,
			@"forRing ({0}) larger than maxRings ({0}).",
			forRing, m_maxRings);
		Contract.Assert(forRing >= 0,
			@"forRing ({0}) less than zero.", forRing);

		int stepsPerHalfRotation = usingStepsPerRotation / 2;

		List<Arc> arcs = m_arcs[forRing];
		for (int anArcId = 0; anArcId < arcs.Count; ++anArcId) {
			Arc anArc = arcs[anArcId];

			// If the arc hasn't been assigned because we haven't gotten to that ring yet,
			// we don't need to worry about it.
			if (anArc.motor == null) continue;

			// Only need to worry about extruders that are negative…
			if (((PrinterExtruder)anArc.motor).relativeRing <= 0) {
				if (anArc.startStep == 0 && anArc.endStep == usingStepsPerRotation) {
					// Case 1: It's a full ring; don't make changes.
				}
				else if (anArc.startStep < stepsPerHalfRotation && anArc.endStep > stepsPerHalfRotation) {
					// Case 2: The arc straddles the extruder position.
					// Split it into two: One that happens immediately
					// and the other that'll happen at the very end
					// of the rotation.
					Arc secondArc = new Arc(anArc);
					secondArc.startStep += stepsPerHalfRotation;
					secondArc.endStep = usingStepsPerRotation;

					Contract.Assert(secondArc.startStep >= 0,
						@"Negative arc start: {0}.", anArc);
					Contract.Assert(secondArc.endStep <= usingStepsPerRotation,
						@"Arc continues past {0}: {1}.", usingStepsPerRotation, anArc);

					arcs.Insert(anArcId + 1, secondArc);
					++anArcId;

					// anArc starts immediately until the
					// end of the original arc.
					anArc.startStep = 0;
					anArc.endStep -= stepsPerHalfRotation;
				}
				else if (anArc.startStep < stepsPerHalfRotation) {
					// Case 3: The start step < 1/2 rotation; we need to ADD
					// the offset.
					anArc.startStep += stepsPerHalfRotation;
					anArc.endStep   += stepsPerHalfRotation;
				}
				else if (anArc.startStep >= stepsPerHalfRotation) {
					// Case 4: The start step >= 1/2 rotation; we
					// need to SUBTRACT the offset.
					anArc.startStep -= stepsPerHalfRotation;
					anArc.endStep   -= stepsPerHalfRotation;
				}
				// NOTE: In the future, we could add another case
				// where we join arcs that have the same starting
				// and stopping steps (which would occur, e.g.,
				// if we have a [0, 10) and a [190, 200) arc assigned
				// to an extruder with a negative ring position.
				else {
					// Something's wrong…
					Text.Error(@"Couldn't normalize arc {0} on ring {1}.",
						anArc, forRing);
				}
			}
			else Text.Log(@"Not normalizing {0}.", anArc);

			Contract.Assert(anArc.startStep >= 0, @"Negative arc start: {0}.", anArc);
			Contract.Assert(anArc.endStep <= usingStepsPerRotation, @"Arc continues too long: {0}.", anArc);
			Contract.Assert(anArc.endStep >= anArc.startStep, @"Start step after end step: {0}.", anArc);
		}

		// Ensure the arcs are in the correct order––they
		// may have changed relationships due to normalization.
		arcs.Sort(Arc.CompareArcs);
	}

	/// <summary>
	/// Assign the specified extruder to arcs if the relative
	/// ring matches; otherwise, uses the existing extruder.
	/// </summary>
	/// <param name='forSecondExtruder'>
	/// The extruder we're trying to assign.
	/// </param>
	/// <param name='usingStepsPerRotation'>
	/// Steps required for 1 platform rotation (excluding step-rate).
	/// </param>
	public void ResolveAssignments(PrinterExtruder forSecondExtruder, int usingStepsPerRotation) {
		PrinterExtruder firstExtruder = (PrinterExtruder)m_arcs[Mathf.Abs(forSecondExtruder.relativeRing)][0].motor;

		Contract.Assert(firstExtruder.materialNumber == forSecondExtruder.materialNumber,
			@"Extruder materials differ (#{0}: {1}; #{2}: {3}).",
			firstExtruder.id, firstExtruder.materialNumber,
			forSecondExtruder.id, forSecondExtruder.materialNumber);

		PrinterExtruder positiveExtruder = (firstExtruder.relativeRing < 0) ? forSecondExtruder : firstExtruder;
		PrinterExtruder negativeExtruder = (firstExtruder.relativeRing < 0) ? firstExtruder  : forSecondExtruder;

		Contract.Assert(positiveExtruder != negativeExtruder,
			@"Extruder ids {0} and {1} are identical.", firstExtruder.id, forSecondExtruder.id);
		Contract.Assert(positiveExtruder.relativeRing > 0,
			@"Relative ring ({0}) isn't positive for extruder {1}.",
			positiveExtruder.relativeRing, positiveExtruder.id);
		Contract.Assert(negativeExtruder.relativeRing < 0,
			@"Relative ring ({0}) isn't negative for extruder {1}.",
			negativeExtruder.relativeRing, negativeExtruder.id);
		Contract.Assert(positiveExtruder.relativeRing == -negativeExtruder.relativeRing,
			@"Extruder {0}'s relative ring ({1}) doesn't match extruder {2}'s -relative ring ({3}).",
			positiveExtruder.id, positiveExtruder.relativeRing,
			negativeExtruder.id, negativeExtruder.relativeRing);


		List<Arc> arcs = m_arcs[positiveExtruder.relativeRing];
		int stepsPerHalfRotation = usingStepsPerRotation / 2;

		for (int arcId = 0; arcId < arcs.Count; ++arcId) {
			Arc anArc = arcs[arcId];
			// Skip arcs if they are for different materials.
			if (anArc.material != positiveExtruder.materialNumber) continue;

			if (anArc.startStep < stepsPerHalfRotation && anArc.endStep < stepsPerHalfRotation) {
				// Case 1: Everything happens before 180°, so use the positive extruder.
				anArc.motor = positiveExtruder;
			}
			else if (anArc.startStep >= stepsPerHalfRotation && anArc.endStep >= stepsPerHalfRotation) {
				// Case 2: Everything happens after 180°, so use the negative extruder.
				anArc.motor = negativeExtruder;
			}
			else {
				// Case 3: The arc straddles the 180° line; split it into two.
				Arc secondHalf = new Arc(anArc);

				// NOTE: We can't normalize the arcs yet because arcs
				// assigned to a single extruder with a negative
				// relative ring also need to get normalized.
				secondHalf.startStep = stepsPerHalfRotation;
				secondHalf.motor = negativeExtruder;

				anArc.endStep = stepsPerHalfRotation;
				anArc.motor = positiveExtruder;
				arcs.Insert(arcId + 1, secondHalf);

				// Skip the arc we just added.
				++arcId;
			}
		}
	}

	/// <summary>
	/// Tries assigning an extruder to the arcs.
	/// </summary>
	/// <returns>
	/// True if successful; false if another extruder
	/// has already been assigned.
	/// </returns>
	/// <param name='anExtruder'>
	/// The extruder to assign.
	/// </param>
	/// <param name='toRing'>
	/// The ring number we care about.
	/// </param>
	public bool TryAssigning(PrinterExtruder anExtruder) {
		int toRing = Mathf.Abs(anExtruder.relativeRing);

		foreach (Arc anArc in m_arcs[toRing]) {
			if (anArc.material == anExtruder.materialNumber) {
				// If we find an arc with another motor assigned,
				// then we have to handle the conflicts.
				if (anArc.motor == null) {
					anArc.motor = anExtruder;
				}
				else {
					Contract.Assert((PrinterExtruder)anArc.motor != anExtruder,
						@"Arc's motor {0} same as tested extruder; TryAssigning() called multiple times for same ring {1}.",
						anArc.motor, toRing);
					return false;
				}
			}
		}

		// The arcs, if any, were successfully assigned.
		return true;
	}
	#endregion

	#region Overrides
	/// <summary>
	/// Displays the region to print of the ring layer.
	/// </summary>
	/// <returns>
	/// A <see cref="System.String"/> that represents the current <see cref="RingLayer"/>.
	/// </returns>
	public override string ToString() {
		return string.Format(@"[RingLayer rings in [{0}, {1}] with steps [{2}, {3}).]",
			m_innerRing, m_outerRing, m_earliestStep, m_latestStep);
	}
	#endregion
}
