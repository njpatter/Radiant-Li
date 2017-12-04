using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Contains a printer's state.
/// </summary>
[System.Serializable]
public class Printer : System.Object {
	#region Constants
	/// <summary>
	/// Non-extruder motor count.
	/// </summary>
	const int kBaseMotors = 4;

	/// <summary>
	/// The size of the motor queue on the prop.
	/// </summary>
	public const int kMotorQueueSize = 256;

	/// <summary>
	/// The size of the receive buffer in bytes.
	/// </summary>
	public const int kRxBufferSize = 8192;
	#endregion
	/// <summary>
	/// The nozzle diameter in mm.
	/// </summary>
	public float nozzleWidthInMm;

	/// <summary>
	/// The layer height in mm.
	/// </summary>
	public float layerHeightInMm;

	/// <summary>
	/// The height of the first layer in mm.
	/// </summary>
	public float firstLayerHeightMm;

	public int platformRadiusInMm;
	/// <summary>
	/// How far the printer platform can move in one
	/// direction after being centered, in mm.
	/// </summary>
	public float movableDistanceInMm;

	/// <summary>
	/// The distance between each extruder, in rings.
	/// </summary>
	public int extruderSpacingInRings;

	/// <summary>
	/// How far the printing platform can move
	/// in one direction after being centered, in rings.
	/// </summary>
	/// <value>
	/// The movable distance in one direction in rings.
	/// </value>
	public int movableDistanceInRings {
		get { return RingsFor(movableDistanceInMm); }
	}

	/// <summary>
	/// The number of motors that share a heater block.
	/// </summary>
	public int motorsPerHeaterBlock = 4;

	public PrinterMotor         platform;
	public PrinterMotorLinear   horizTrack;
	public PrinterMotorLinear[] vertTrack;
	public PrinterExtruder[]    extruders;

	public bool isAwake;

	/// <summary>
	/// Returns the number of motors in the printer.
	/// </summary>
	/// <value>
	/// The number of motors.
	/// </value>
	public int numberOfMotors {
		get { return kBaseMotors + extruders.Length; }
	}

	/// <summary>
	/// Returns the number of non-extruder motors
	/// in the printer.
	/// </summary>
	/// <value>
	/// The number of non-extruder motors.
	/// </value>
	public int numberOfBaseMotors {
		get { return kBaseMotors; }
	}

	/// <summary>
	/// The last enqueued tx packet from updating state.
	/// </summary>
	public TxPacket lastEnqueuedPacket;

	/// <summary>
	/// Cached arc for horizontal track movement.
	/// </summary>
	Arc m_horizTrackArc;

	/// <summary>
	/// Cached arc for vertical track movement; should
	/// always start at 0 steps.
	/// </summary>
	Arc m_vertTrackArc;

	/// <summary>
	/// The direction that we'll move the horizontal
	/// track during printing; should be set by the
	/// print controller.
	/// </summary>
	public StepDirection horizPrintingDirection;

	/// <summary>
	/// The direction of ring movement based on the printing direction.
	/// </summary>
	/// <value>
	/// The ring delta.
	/// </value>
	int m_ringDelta {
		get { return HorizDeltaFor(horizPrintingDirection); }
	}

	/// <summary>
	/// Gets the absolute, current ring position.
	/// </summary>
	/// <value>
	/// The ring position.
	/// </value>
	public int ringPosition {
		get { return m_ringPosition; }
	}
	int m_ringPosition;
	int m_nextLayer;

	/// <summary>
	/// The last motor we selected.
	/// </summary>
	public int currentMotorId = int.MinValue;

	static readonly GanglionCommand[] kMotorToCommand = new GanglionCommand[] {
		GanglionCommand.Platform,
		GanglionCommand.HorizTrack,
		GanglionCommand.LeftTrack,
		GanglionCommand.RightTrack,
		GanglionCommand.Extruder0,
		GanglionCommand.Extruder1,
		GanglionCommand.Extruder2,
		GanglionCommand.Extruder3,
		GanglionCommand.Extruder4,
		GanglionCommand.Extruder5,
		GanglionCommand.Extruder6,
		GanglionCommand.Extruder7,
	};

	public GanglionCommand CommandFor(int aMotorId) {
		return kMotorToCommand[aMotorId];
	}

	/// <summary>
	/// Converts from step sizes to propeller step sizes.
	/// </summary>
	static readonly Dictionary<StepSize, int> kStepConversion = new Dictionary<StepSize, int> {
		{ StepSize.Whole,     (int)GanglionStep.Whole     },
		{ StepSize.Half,      (int)GanglionStep.Half      },
		{ StepSize.Quarter,   (int)GanglionStep.Quarter   },
		{ StepSize.Eighth,    (int)GanglionStep.Eighth    },
		{ StepSize.Sixteenth, (int)GanglionStep.Sixteenth }
	};

	static readonly Dictionary<GanglionCommand, GanglionCommand> kMotorStop = new Dictionary<GanglionCommand, GanglionCommand> {
		{ GanglionCommand.Platform,   GanglionCommand.PlatformStop   },
		{ GanglionCommand.HorizTrack, GanglionCommand.HorizTrackStop },
		{ GanglionCommand.LeftTrack,  GanglionCommand.LeftTrackStop  },
		{ GanglionCommand.RightTrack, GanglionCommand.RightTrackStop },
		{ GanglionCommand.Extruder0,  GanglionCommand.Extruder0Stop  },
		{ GanglionCommand.Extruder1,  GanglionCommand.Extruder1Stop  },
		{ GanglionCommand.Extruder2,  GanglionCommand.Extruder2Stop  },
		{ GanglionCommand.Extruder3,  GanglionCommand.Extruder3Stop  }
	};

	static readonly GanglionCommand[] kStepRateMotor = new GanglionCommand[] {
		GanglionCommand.StepRatePlatform,
		GanglionCommand.StepRateHorizTrack,
		GanglionCommand.StepRateLeftTrack,
		GanglionCommand.StepRateRightTrack,
		GanglionCommand.StepRateExtruder0,
		GanglionCommand.StepRateExtruder1,
		GanglionCommand.StepRateExtruder2,
		GanglionCommand.StepRateExtruder3,
	};

	public int GanglionStepSizeFor(StepSize aSize) {
		return kStepConversion[aSize];
	}

	#region Initialization & cleanup
	public Printer() {
		m_horizTrackArc = new Arc(0, 0);
		m_horizTrackArc.motor = horizTrack;

		m_vertTrackArc = new Arc(0, 0);
		isAwake = false;
	}

	/// <summary>
	/// Generates a copy of the printer so we don't
	/// destroy the default settings.
	/// </summary>
	/// <returns>
	/// A deep copy of the printer.
	/// </returns>
	public Printer Clone() {
		Printer product     = new Printer();
		Contract.Assert(product != this, @"Shallow printer copy!");

		product.nozzleWidthInMm    = nozzleWidthInMm;
		product.platformRadiusInMm = platformRadiusInMm;

		product.platform    = platform.Clone();
		product.horizTrack  = (PrinterMotorLinear)horizTrack.Clone();
		product.m_horizTrackArc.motor = product.horizTrack;
		Contract.Assert(product.platform != this.platform, @"Shallow platform copy!");
		Contract.Assert(product.horizTrack != this.horizTrack, @"Shallow horiz track copy!");

		product.layerHeightInMm = layerHeightInMm;
		product.firstLayerHeightMm = firstLayerHeightMm;
		product.vertTrack = new PrinterMotorLinear[vertTrack.Length];
		for (int i = 0; i < vertTrack.Length; ++i) {
			product.vertTrack[i] = (PrinterMotorLinear)vertTrack[i].Clone();
			Contract.Assert(product.vertTrack[i] != this.vertTrack[i], @"Shallow vert track copy!");
		}
		product.m_vertTrackArc = new Arc(0, 0);
		product.m_vertTrackArc.endStep = Mathf.Abs(product.vertTrack[0].StepsForMm(firstLayerHeightMm));
		product.m_vertTrackArc.direction = StepDirection.Cw;

		product.extruders = new PrinterExtruder[extruders.Length];
		for (int i = 0; i < extruders.Length; ++i) {
			product.extruders[i] = (PrinterExtruder)extruders[i].Clone();
			Contract.Assert(product.extruders[i] != extruders[i], @"Shallow extruder copy!");
			product.extruders[i].SetRelativeLocation(0);
		}

		product.movableDistanceInMm = movableDistanceInMm;
		product.m_ringPosition = m_ringPosition;
		product.extruderSpacingInRings = extruderSpacingInRings;
		product.motorsPerHeaterBlock = motorsPerHeaterBlock;

		product.isAwake = isAwake;

		Contract.Assert(product.vertTrack.Length == 2, @"Expected 2 vertical tracks, not {0}.", product.vertTrack.Length);
		Contract.Assert(product.nozzleWidthInMm >= 0.0f, @"Nozzle width should be positive, not {0}.", product.nozzleWidthInMm);
		Contract.Assert(product.layerHeightInMm > 0.0f, @"Non-positive layer height of {0}mm.", product.layerHeightInMm);
		Contract.Assert(product.platformRadiusInMm > 0, @"Platform radius should be positive.");
		Contract.Assert(product.m_horizTrackArc != null, @"Null horizontal track arc.");
		Contract.Assert(product.m_horizTrackArc.motor != null, @"Null horiz track motor.");
		Contract.Assert(product.m_horizTrackArc.motor == product.horizTrack, @"Motor mismatch for horizTrackArc.");
		Contract.Assert(product.movableDistanceInMm > 0, @"Non-positive movable distance in mm.");
		Contract.Assert(product.extruderSpacingInRings > 0, @"Non-positive extruder spacing in rings.");

		Contract.Assert(product.m_vertTrackArc != null, @"Null vert track arc.");
		Contract.Assert(product.m_vertTrackArc.startStep == 0, @"Vert track doesn't start at step 0.");
		Contract.Assert(product.m_vertTrackArc.length != 0, @"No vert track arc length.");
		Contract.Assert(product.m_vertTrackArc.direction == StepDirection.Cw, @"Vert track direction incorrect: {0}.",
			product.m_vertTrackArc.direction);
		Contract.Assert(product.motorsPerHeaterBlock > 0, @"Non-positive number of motors per heater block: {0}.",
			product.motorsPerHeaterBlock);

		#if UNITY_EDITOR
		foreach (PrinterExtruder firstExtruder in product.extruders) {
			bool isCorrectDistance = false;
			foreach (PrinterExtruder secondExtruder in product.extruders) {
				int distance = Mathf.Abs(firstExtruder.ringNumber - secondExtruder.ringNumber);
				if (distance == product.extruderSpacingInRings) {
					isCorrectDistance = true;
					break;
				}
			}
			Contract.Assert(isCorrectDistance,
				@"No extruder found {0} ring{1} away from extruder {2} at {3}.",
				product.extruderSpacingInRings, Text.S(product.extruderSpacingInRings),
				firstExtruder.id, firstExtruder.ringNumber);
		}
		#endif

		return product;
	}

	/// <summary>
	/// Updates extruders for a new layer.
	/// </summary>
	public void NewLayer() {
		UpdateRelativeExtruderPositions();

		PrinterExtruder.layerScale = (m_nextLayer == 0)
			? PrinterExtruder.kBaseLayerScale : PrinterExtruder.kDefaultLayerScale;
		++m_nextLayer;
	}

	public void NewPrint() {
		m_nextLayer = 0;
		m_vertTrackArc.endStep = Mathf.Abs(vertTrack[0].StepsForMm(firstLayerHeightMm));
	    // float scaling = firstLayerHeightMm / layerHeightInMm;
	    foreach (PrinterExtruder e in extruders) {
			e.ScaleRateConstant(1.0f); //scaling);
	    }

		m_lastNumberSent = int.MaxValue;
	}
	#endregion

	#region Utility methods
	int HorizDeltaFor(StepDirection aDirection) {
		int result = -(int)aDirection;
		return result;
	}

	public StepDirection HorizDirectionFor(int aDelta) {
		StepDirection result = aDelta < 0 ? StepDirection.Cw : StepDirection.Ccw;
		return result;
	}

	int RingsFor(float mm) {
		return Mathf.FloorToInt(mm / nozzleWidthInMm);
	}

	public PrinterMotor GetBaseMotor(int i) {
		switch (i) {
			case 0: return platform;
			case 1: return horizTrack;
			case 2: return vertTrack[0];
			case 3: return vertTrack[1];
			default:
				Text.Error(@"Invalid base motor {0} requested.", i);
				return null;
		}
	}

	public PrinterMotor GetMotor(int i) {
		if (i < numberOfBaseMotors) return GetBaseMotor(i);
		return extruders[i - numberOfBaseMotors];
	}

	public Vector3 PolarPosition {
		get {
			float r = horizTrack.position - platformRadiusInMm;
			float theta = platform.rotationInDegrees * Mathf.PI / 180.0f;
			/*if (r < 0) {
				r = Mathf.Abs(r);
				theta -= Mathf.PI;
			}*/
			return new Vector3(r, theta, vertTrack[0].position);
		}
	}

	public Vector3 CartesianPosition {
		get {
			return MathUtil.PolarToCartesian(PolarPosition);
		}
	}

	public Vector3 GetExtruderPolarPosition(PrinterExtruder anExtruder) {
		return PolarPosition + new Vector3(PrinterExtruder.kStandardNozzleWidth * anExtruder.ringNumber, 0, 0);
	}

	public Vector3 GetExtruderCartesianPosition(PrinterExtruder anExtruder) {
		return MathUtil.PolarToCartesian(GetExtruderPolarPosition(anExtruder));
	}
	#endregion

	#region Horizontal movement
	/// <summary>
	/// Scales the most recent horizontal movement arc
	/// to start at fromStep and returns the track's arc.
	/// NOTE: Should be called after Move() and NextRing.
	/// </summary>
 	/// <param name='fromStep'>
	/// The current step.
	/// </param>
	public Arc FinishedMoving(int fromStep) {
		//Debug.Log("Finished Moving at fromStep " + fromStep);
		m_horizTrackArc.direction = horizPrintingDirection;

		Contract.Assert(m_horizTrackArc.motor != null,
			@"Haven't assigned motor to horiz track arc.");
		Arc result = new Arc(m_horizTrackArc);
		result.startStep += fromStep;
		result.endStep   += fromStep;

		m_horizTrackArc.startStep = m_horizTrackArc.endStep = 0;

		//Text.Log(@"Moving {0} step{1} to ring {2}.",
		//	result.length, Text.S(result.length), m_ringPosition);

		/// This is to help Outlining work...
		//horizTrack.position = m_ringPosition * PrinterExtruder.kStandardNozzleWidth + platformRadiusInMm; // + horizTrack.locationError;


		return result;
	}

	/// <summary>
	/// Moves the specified number of rings in the horizPrintingDirection.
	/// </summary>
	/// <param name='fromStep'>
	/// The starting global step.
	/// </param>
	/// <param name='byRings'>
	/// The number of rings to move by.
	/// </param>
	public Arc Move(int fromStep, int byRings) {
		float distanceToMove = (horizPrintingDirection == StepDirection.Ccw) 
			?  Mathf.Abs(byRings) * nozzleWidthInMm 
			: -Mathf.Abs(byRings) * nozzleWidthInMm;
		int stepsToTake = Mathf.Abs(horizTrack.StepsForMm(distanceToMove));

		Arc movementArc       = new Arc(horizTrack, fromStep);
		movementArc.direction = HorizDirectionFor(byRings);
		movementArc.endStep   = fromStep + stepsToTake;

		horizTrack.stepDirection = movementArc.direction;
		m_ringPosition += byRings;

		// NOTE:
		// This is primarily used when moving to the starting
		// ring; as such, we need to update the extruder
		// positions.
		UpdateRelativeExtruderPositions();

		// Reset the horiz track.
		m_horizTrackArc.startStep = m_horizTrackArc.endStep = 0;

		return movementArc;
	}

	/// <summary>
	/// Returns the arc to move another ring.
	/// </summary>
	/// <returns>
	/// The ring.
	/// </returns>
	public void NextRing() {
		Contract.Assert(m_ringDelta != 0, @"Ring moves by 0.");
		horizTrack.stepDirection = horizPrintingDirection;
		float distanceToMove = (horizTrack.stepDirection == StepDirection.Ccw) ? nozzleWidthInMm : -nozzleWidthInMm;
		int stepsToTake = Mathf.Abs(horizTrack.StepsForMm(distanceToMove));
		Contract.Assert(stepsToTake >= 0, "Calculated a negative set of steps to move {0} mm",
		                nozzleWidthInMm);

		m_horizTrackArc.endStep += stepsToTake;
		m_ringPosition += m_ringDelta;

		// Sanity check.
		Contract.Assert(horizPrintingDirection == m_horizTrackArc.direction, "Printing and horizontal track directions don't match up");
		m_horizTrackArc.direction = horizPrintingDirection;

		/// End help for outlining
		UpdateRelativeExtruderPositions();
	}
	#endregion

	#region Vertical movement
	/// <summary>
	/// Fills each queue with arcs to move to the next layer (+y).
	/// </summary>
	/// <param name='forTrack0'>
	/// Queue for vertTrack[0].
	/// </param>
	/// <param name='forTrack1'>
	/// Queue for vertTrack[1].
	/// </param>
	public void NextLayer(List<Arc> forTrack0, List<Arc> forTrack1, int currentLayer) {
		NextLayer(vertTrack[0], forTrack0);
		NextLayer(vertTrack[1], forTrack1);

		m_vertTrackArc.endStep = Mathf.Abs(vertTrack[0].StepsForMm(layerHeightInMm));

		//Text.Log("Moving {0} steps after layer {1}; {2} arc{3}", m_vertTrackArc.length, 
		//         currentLayer, forTrack0.Count, Text.S(forTrack0.Count));
		foreach (PrinterExtruder e in extruders) {
			e.ScaleRateConstant(1.0f);
		}
	}

	/// <summary>
	/// Enqueues a copy of the vertical track arc to move one layer.
	/// </summary>
	/// <param name='forMotor'>
	/// The motor to move.
	/// </param>
	/// <param name='toQueue'>
	/// The queue to fill.
	/// </param>
	void NextLayer(PrinterMotor forMotor, List<Arc> toQueue) {
		Arc anArc = new Arc(m_vertTrackArc);
		anArc.motor = forMotor;
		forMotor.Step(anArc.length);
		toQueue.Add(anArc);
		Contract.Assert(toQueue.Count == 1, @"Expected 1, not {0} vertical arc{1}", 
		                toQueue.Count, Text.S(toQueue.Count));
	}
	#endregion

	#region Extruder management
	/// <summary>
	/// Updates the relative extruder positions.
	/// </summary>
	void UpdateRelativeExtruderPositions() {
		foreach (PrinterExtruder anExtruder in extruders) {
			anExtruder.SetRelativeLocation(m_ringPosition);
		}
	}

	public void UpdateRingPositionBasedOnHorizontalPosition() {
		m_ringPosition = Mathf.RoundToInt(((float)horizTrack.position - platformRadiusInMm) /
		                                  PrinterExtruder.kStandardNozzleWidth);
		UpdateRelativeExtruderPositions();
	}

	public PrinterExtruder[] GetExtrudersWithMaterial(byte aMaterial) {
		List<PrinterExtruder> extWithMat = new List<PrinterExtruder>();
		for(int i = 0; i < extruders.Length; i++) {
			if (extruders[i].materialNumber == aMaterial) {
				extWithMat.Add(extruders[i]);
			}
		}
		return extWithMat.ToArray();
	}

	#endregion

	#region Command and state management
	int m_lastNumberSent;
	public int UpdateState(TickProfile fromProfile, List<TxPacket> forList) {
		int targetMotorId = fromProfile.motor.id;
		PrinterMotor targetMotor = targetMotorId < kBaseMotors
			? GetBaseMotor(targetMotorId) : extruders[targetMotorId - kBaseMotors];
		Contract.Assert(targetMotor.id == targetMotorId, @"Found motor {0} but id isn't {1}.",
			targetMotor, targetMotorId);

		bool shouldSendDirection = targetMotor.stepDirection != fromProfile.direction;
		bool shouldSendStepSize  = targetMotor.stepSize      != fromProfile.size;
		bool shouldSendStepRate  = targetMotor.stepRate      != fromProfile.stepRate;

		if (shouldSendDirection || shouldSendStepSize || shouldSendStepRate) {
			if (currentMotorId != targetMotorId) {
				// Select the motor; note that we don't change the stack.
				if (lastEnqueuedPacket != null && lastEnqueuedPacket.aCmd == GanglionCommand.StepRate) {
					lastEnqueuedPacket.aCmd = kStepRateMotor[targetMotorId];
				}
				else {
					lastEnqueuedPacket = new TxPacket(kMotorToCommand[targetMotorId]);
					forList.Add(lastEnqueuedPacket);
				}
				currentMotorId = targetMotorId;
			}
			Contract.Assert(currentMotorId == targetMotor.id,
				@"Motor id mismatch: {0} != {1}", currentMotorId, targetMotor.id);

			if (shouldSendDirection) {
				lastEnqueuedPacket = new TxPacket(fromProfile.direction == StepDirection.Ccw
				                                  ? GanglionCommand.CounterClockwise 
				                                  : GanglionCommand.Clockwise);
				forList.Add(lastEnqueuedPacket);
				targetMotor.stepDirection = fromProfile.direction;
			}
			if (shouldSendStepSize) {
				int ganglionSize = kStepConversion[fromProfile.size];
				lastEnqueuedPacket = new TxPacket(GanglionCommand.Value, ganglionSize);
				forList.Add(lastEnqueuedPacket);

				lastEnqueuedPacket = new TxPacket(GanglionCommand.StepSize);
				forList.Add(lastEnqueuedPacket);
				targetMotor.stepSize = fromProfile.size;

				m_lastNumberSent = ganglionSize;
			}
			if (shouldSendStepRate) {
				if (fromProfile.stepRate == 0) {
					if (lastEnqueuedPacket != null && kMotorStop.ContainsKey(lastEnqueuedPacket.aCmd)) {
						lastEnqueuedPacket.aCmd = kMotorStop[lastEnqueuedPacket.aCmd];
					}
					else {
						lastEnqueuedPacket = new TxPacket(GanglionCommand.Stop);
						forList.Add(lastEnqueuedPacket);
					}
				}
				else {
					if (m_lastNumberSent != fromProfile.stepRate) {
						lastEnqueuedPacket = new TxPacket(GanglionCommand.Value, fromProfile.stepRate);
						forList.Add(lastEnqueuedPacket);
						m_lastNumberSent = fromProfile.stepRate;
					}
					lastEnqueuedPacket = new TxPacket(GanglionCommand.StepRate);
					forList.Add(lastEnqueuedPacket);
				}
				targetMotor.stepRate = fromProfile.stepRate;
			}
		}

		Contract.Assert(targetMotor.stepDirection == fromProfile.direction,
			@"Direction mismatch: {0} != {1}.", targetMotor.stepDirection, fromProfile.direction);
		Contract.Assert(targetMotor.stepSize == fromProfile.size,
			@"Step size mismatch: {0} != {1}", targetMotor.stepSize, fromProfile.size);
		Contract.Assert(targetMotor.stepRate == fromProfile.stepRate,
			@"Step rate mismatch: {0} != {1}.", targetMotor.stepRate, fromProfile.stepRate);

		return m_lastNumberSent;
	}

	/// <summary>
	/// Updates the last number sent. Generally called after enqueuing a 
	/// value token.
	/// </summary>
	/// <param name="number">The value token's argument.</param>
	public void UpdateLastSentNumber(int number) {
		m_lastNumberSent = number;
	}
	#endregion
}
