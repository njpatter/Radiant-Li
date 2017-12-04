using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class OutlinePathGenerator {

	public const float printedLengthPerStep = 0.269872f; // From the Path to Lionhead Prototype 2 spreadsheet
	public const int kOutliningMovementStepRate = 15;
	public const int kOutliningExtruderStepRate = 3 * kOutliningMovementStepRate;  
	public const float kTargetMmPerSec = 5.0f;
	public const int kTargetHorizFastRate = 384;//192;//15;
	public const int kTargetPlatFastRate = 192;//15;
	public const int kStandardExtruderPressureRate = 15;

	Printer m_workingPrinter;
	PrinterMotorLinear m_horizTrack;
	PrinterMotor m_platform; 
	public int currentStep;
	public List<Outline> layerOutlines;

	public void Init(Printer aPrinter) {
		m_horizTrack = (PrinterMotorLinear)aPrinter.horizTrack; //.Clone();
		m_platform = aPrinter.platform; //.Clone();
		m_workingPrinter = aPrinter; //.Clone();
	}
	
	public IEnumerator GeneratePathFromOutlines(int fromStep, int fromTick,
	                                            List<TickProfile>[] m_assignedProfiles,
	                                            List<Arc>[] m_assignedArcs,
	                                            StepSize originalPlatformStepSize, 
	                                            int[] depressureAmounts,
	                                            bool useConstantSpeedOutlining) 
	{ 
		List<PrintedLine> outlineMovements = new List<PrintedLine>(); 

		int segmentCount = 0;
		foreach(Outline o in layerOutlines) {
			segmentCount+=o.segments.Count;
		}
		/*Debug.LogWarning("Outlining starting at polar pos " + GetPolarPosition(m_workingPrinter.extruders[0], 0, 0) +
		                 " and cartesian pos " + GetCartesianPosition(m_workingPrinter.extruders[0], 0, 0) +
		                 " with horizontal position = " + m_horizTrack.position +
		                 " (rounded: " + (m_workingPrinter.ringPosition * PrinterExtruder.kStandardNozzleWidth + m_workingPrinter.platformRadiusInMm) + ") "  +  
		                 " based on ring number " + m_workingPrinter.ringPosition +
		                 " and error = " + m_horizTrack.locationError +
		                 " with a horizontal step size = " + m_horizTrack.distancePerStep +
		                 " with first line = " + layerOutlines[0].segments[0] + 
		                 " a total outline count of " + layerOutlines.Count + 
		                 " a total segment count of " + segmentCount +
		                 " and a platform step angle = " + m_platform.degreesPerStep);*/

		float extrudedMaterialDistance = 0;
		int extruderStepCount = 0;
		byte prevMaterial = 0;

		foreach(Outline outline in layerOutlines) {
			PrinterExtruder m_currentExtruder = GetExtruderForMaterial(outline.material);
			if (prevMaterial != outline.material) {
				extrudedMaterialDistance = 0;
				//Text.Log("Resetting extruded material distance of " + extrudedMaterialDistance + 
				//         " from material " + prevMaterial + " to material " + outline.material);
				prevMaterial = outline.material;
			}

			//if(m_workingPrinter.vertTrack[0].position < -3.75f) Dispatcher<Outline, float>.Broadcast(PointLineVisualizer.kOnAddOutline, outline, m_workingPrinter.vertTrack[0].position);

			for(int segIndex = 0; segIndex < outline.segments.Count; segIndex++) {
				/// We need to put the cartesian segment into coordinates usable by the outlining routines, specifically printer coords
				if ((GetCartesianPosition(m_currentExtruder, 0, 0) - outline.segments[segIndex].p0).magnitude > 0.1f &&
				    (Mathf.Abs( Mathf.Abs(GetCartesianPosition(m_currentExtruder, 0, 0).x - outline.segments[segIndex].p0.x)  - 75f) > 0.1f)) 
				{
					//CartesianSegment csLast = segIndex == 0 ? null : outline.segments[segIndex - 1];
					//Debug.LogError("Extruder " + m_currentExtruder.id + " is at " + GetCartesianPosition(m_currentExtruder, 0, 0) + " but we are seeking segment index " + segIndex +
					//          " = " + outline.segments[segIndex].p0.ToString() + " and the last segment = " + (csLast == null ? "null " : csLast.ToString()));
				}

				bool skipSegmentStartingPoint = 
					(segIndex > 0 && 
					 Mathf.Approximately(outline.segments[segIndex - 1].p1.x, outline.segments[segIndex].p0.x) && 
					 Mathf.Approximately(outline.segments[segIndex - 1].p1.y, outline.segments[segIndex].p0.y));
				Vector2 aStartingPoint = outline.segments[segIndex].p0 - Vector2.one * m_workingPrinter.platformRadiusInMm;
				CartesianSegment modCs = 
					new CartesianSegment(aStartingPoint,
					                     outline.segments[segIndex].p1 - Vector2.one * m_workingPrinter.platformRadiusInMm, 
					                     outline.segments[segIndex].material);

				/// Now we move to and along the cartesian segment
				outlineMovements.AddRange(MoveToAndAlongSegment(modCs, m_currentExtruder, 
				                                                ref extrudedMaterialDistance,
				                                                skipSegmentStartingPoint,
				                                                useConstantSpeedOutlining)); 
			}
			if (Scheduler.ShouldYield()) yield return null;
		}

		float totalMovementMade = 0f;
		foreach(PrintedLine pl in outlineMovements) {
			if (!pl.isExtruderUsed) continue;
			extruderStepCount += pl.numExtruderSteps;
			foreach(PrintedOutlineSegment pos in pl.segments) {
				totalMovementMade += pos.length;
			}
		}

		Contract.Assert((int)m_workingPrinter.platform.stepDirection == m_lastPlatformStep,
		                "Platform direction {0} and final platform step direction {1} do not match!",
		                m_workingPrinter.platform.stepDirection, 
		                (StepDirection)m_lastPlatformStep);
		Contract.Assert((int)m_workingPrinter.horizTrack.stepDirection == -m_lastHorizStep,
		                "Horizontal direction {0} and final Horizontal step direction {1} do not match!",
		                m_workingPrinter.horizTrack.stepDirection, 
		                (StepDirection)(-m_lastHorizStep));

		/// Now we need to move to the closest ring and set the ring for the printer to operate correctly
		//Debug.LogWarning("!!!!!!!!!!!!!!!!!!!!!!!!!!!! About to move to closest ring from: \n" + CurrentPositionString);
		Vector2 currentPos = GetPolarPosition(m_workingPrinter.extruders[0], 0, 0);
		int targetRing = Mathf.RoundToInt(currentPos[0] / 
		                                  PrinterExtruder.kStandardNozzleWidth);


		int posDistance = m_platform.rotationInSteps % ((int)m_platform.stepSize / (int)originalPlatformStepSize);
		int negDistance = ((int)m_platform.stepSize / (int)originalPlatformStepSize) - 
			m_platform.rotationInSteps % ((int)m_platform.stepSize / (int)originalPlatformStepSize) ;
		int distToClosestSharedStep = posDistance < negDistance ? posDistance : -negDistance;


		float targetAngle = currentPos.y + Mathf.Deg2Rad * m_platform.degreesPerStep * distToClosestSharedStep;
		Vector2 targetCartPos = GetCartesianPosition(new Vector2(targetRing * PrinterExtruder.kStandardNozzleWidth, targetAngle));// currentPos.y));

		CartesianSegment segmentOnRing = new CartesianSegment(targetCartPos, targetCartPos, 
		                                                      (byte)m_workingPrinter.extruders[0].materialNumber);
		List<PrintedLine> movementToRing = MoveToAndAlongSegment(segmentOnRing, 
		                                                         m_workingPrinter.extruders[0], 
		                                                         ref extrudedMaterialDistance, 
		                                                         false,
		                                                         useConstantSpeedOutlining);

		outlineMovements.AddRange(movementToRing);
		//Debug.LogWarning("!!!!!!!!!!!!!!!!!!!!!!!!!!!! Ending outline \n" + CurrentPositionString);
		m_workingPrinter.UpdateRingPositionBasedOnHorizontalPosition();

		float currentRingPos = ((float)m_workingPrinter.horizTrack.position - m_workingPrinter.platformRadiusInMm) / PrinterExtruder.kStandardNozzleWidth;
		m_horizTrack.locationError = (Mathf.Round(currentRingPos) - currentRingPos) * PrinterExtruder.kStandardNozzleWidth;



		Contract.Assert((int)m_workingPrinter.platform.stepDirection == m_lastPlatformStep,
		                "Platform direction {0} and final platform step direction {1} do not match!",
		                m_workingPrinter.platform.stepDirection, 
		                (StepDirection)m_lastPlatformStep);
		Contract.Assert((int)m_workingPrinter.horizTrack.stepDirection == -m_lastHorizStep,
		                "Horizontal direction {0} and final Horizontal step direction {1} do not match!",
		                m_workingPrinter.horizTrack.stepDirection, 
		                (StepDirection)(-m_lastHorizStep));
		
		if (useConstantSpeedOutlining) {
			fromTick = ConvertStepsToProfiles(fromTick, m_workingPrinter, outlineMovements, m_assignedProfiles, depressureAmounts);
			Contract.Assert(m_workingPrinter.platform.stepDirection == 
			                m_assignedProfiles[0][m_assignedProfiles[0].Count - 1].direction,
			                "Platform direction {0} and final platform profile direction {1} do not match!",
			                m_workingPrinter.platform.stepDirection, 
			                m_assignedProfiles[0][m_assignedProfiles[0].Count - 1].direction);

			Contract.Assert(m_workingPrinter.horizTrack.stepDirection == 
			                m_assignedProfiles[1][m_assignedProfiles[1].Count - 1].direction,
			                "Platform direction {0} and final platform profile direction {1} do not match!",
			                m_workingPrinter.platform.stepDirection, 
			                m_assignedProfiles[1][m_assignedProfiles[1].Count - 1].direction);
		}
		else {
			currentStep = ConvertStepsToArcs(fromStep, m_workingPrinter, m_assignedArcs, outlineMovements);
			Contract.Assert(m_workingPrinter.platform.stepDirection == 
		                m_assignedArcs[0][m_assignedArcs[0].Count - 1].direction,
		                "Platform direction {0} and final platform arc direction {1} do not match!",
		                m_workingPrinter.platform.stepDirection, 
		                m_assignedArcs[0][m_assignedArcs[0].Count - 1].direction);
			Contract.Assert(m_workingPrinter.horizTrack.stepDirection == 
		                m_assignedArcs[1][m_assignedArcs[1].Count - 1].direction,
		                "Horizontal direction {0} and final Horizontal arc direction {1} do not match!",
		                m_workingPrinter.horizTrack.stepDirection, 
		                m_assignedArcs[1][m_assignedArcs[1].Count - 1].direction);
		}

		yield return null;
	}
			
	private PrinterExtruder GetExtruderForMaterial(byte aMaterial) {
		for(int i = 0; i < m_workingPrinter.extruders.Length; i++) {
			if (m_workingPrinter.extruders[i].materialNumber == aMaterial) {
				return m_workingPrinter.extruders[i];
			}
		}
		return null;
	}

	int m_lastPlatformStep = 0;
	int m_lastHorizStep = 0;
	
	private List<PrintedLine> MoveToAndAlongSegment(CartesianSegment aSegment, PrinterExtruder anExtruder,
	                                                ref float extrudedMaterialDistance, bool ignoreStartingPoint,
	                                                bool useConstantSpeedOutlining) {
		List<PrintedLine> printedOutlines = new List<PrintedLine>();

		int aStartingPointIndex = ignoreStartingPoint ? 1 : 0;

		if (ignoreStartingPoint) {
			if ((GetCartesianPosition(anExtruder, 0, 0) - aSegment.p0).magnitude > 0.1f) {
				Debug.LogError("Skipping move to initial point, but p0 = " + aSegment.p0 + " and current position = " +  GetCartesianPosition(anExtruder, 0,0));
			}
		}

		for(int pointIndex = aStartingPointIndex; pointIndex < 2; pointIndex++) {
			if (pointIndex == 0) {
				MoveToLineStart(anExtruder, aSegment, useConstantSpeedOutlining, printedOutlines);
				continue;
			}
			if (aSegment.p0 == aSegment.p1) continue;

			PrintAlongLine(anExtruder, aSegment, printedOutlines, ref extrudedMaterialDistance);
		}
		//Debug.Log(m_workingPrinter.vertTrack[0].position);
		return printedOutlines;
	}

	private void PrintAlongLine(PrinterExtruder anExtruder, CartesianSegment aSegment, List<PrintedLine> printedOutlines, ref float extrudedMaterialDistance) {
		Vector2 fromPoint = aSegment.p0;
		Vector2 targetPoint = aSegment.p1;
		//lineColor = Color.green;
		PrintedLine currentLine = new PrintedLine(true);
		printedOutlines.Add(currentLine); 
		
		currentLine.extruder = anExtruder;
		
		PrintedOutlineSegment pos = null;
		float distancePrintedPerStep = anExtruder.volumePerStep / (m_workingPrinter.layerHeightInMm *
		                                                           m_workingPrinter.nozzleWidthInMm);
		//Debug.Log("Using a distance printed per step = " + distancePrintedPerStep);

		int extruderStepCount = 0;
		
		PrintedOutlineSegment prevSegment = null;
		
		//Vector2 initialExtruderPosition = GetCartesianPosition(anExtruder,0,0);

		while(true) {
			
			if (prevSegment != null && 
			    prevSegment.horizontalStep == -pos.horizontalStep &&
			    prevSegment.platformStep == -pos.platformStep) {
				Debug.LogError("An opposite segment occured  " + prevSegment + "   " + pos + 
				               " when trying to Find best step combination " );
				break;
			}
			prevSegment = pos;
			
			pos = FindBestStepCombination(anExtruder, targetPoint, fromPoint, prevSegment, aSegment);
			extrudedMaterialDistance -= pos.length;

			if (extrudedMaterialDistance < 0) {
				extruderStepCount++;
				pos.extruderStep = 1;
				extrudedMaterialDistance += distancePrintedPerStep;
			}
			
			if (pos.horizontalStep == 0 && pos.platformStep == 0) {
				if (prevSegment == null) Debug.Log("Stopping at currentPosition = " + GetCartesianPosition(anExtruder, 0, 0) + " due to 0-0 combination even without taking a step");
				break;
			}
			
			if (pos.horizontalStep != 0) {
				m_horizTrack.stepDirection = (pos.horizontalStep > 0) ? StepDirection.Ccw : StepDirection.Cw;
				m_horizTrack.Step(1);
				m_lastHorizStep = pos.horizontalStep;
			}
			
			if (pos.platformStep != 0) {
				m_platform.stepDirection = (pos.platformStep > 0) ? StepDirection.Cw : StepDirection.Ccw;
				m_platform.Step(1);
				m_lastPlatformStep = pos.platformStep;
			}
			currentLine.Add(pos);
		}
		currentLine.numExtruderSteps = extruderStepCount;
		
		Contract.Assert(currentLine.tickLength > 0, "Created a 0-length line for printing along a line: " + currentLine.ToString());
		Contract.Assert(currentLine.platformSteps > 0 || currentLine.horizontalSteps > 0 || currentLine.segments.Count > 0, 
		                "Created a line that has no platform, horizontal steps and no segments to use");
	}
	
	/// <summary>
	/// Finds the best step combination to move closer to the target position.
	/// </summary>
	/// <returns>
	/// The best step combination and distance covered.
	/// </returns>
	/// <param name='targetPosition'>
	/// For target position.
	/// </param>
	private PrintedOutlineSegment FindBestStepCombination(PrinterExtruder anExtruder,
		Vector2 targetPosition, Vector2 fromPosition, PrintedOutlineSegment ignoreOppositeOfThisSegment, CartesianSegment aSegment) 
	{
		int bestHorizMove = 0;
		int bestPlatMove = 0;
		float minimumStepError = float.MaxValue;
		int platformStepMultiplier = (PrinterMotor.kMinStepSizeCountPerWholeStep / (int)m_platform.stepSize);

		Vector2 currentPosition = GetCartesianPosition(anExtruder,0,0);
		float bestDistance = (targetPosition - currentPosition).sqrMagnitude;
		float currentDistance = bestDistance;

		/// Check to see if we are less than or equal to 1/2 step away from target 
		float platStepDistance = (GetCartesianPosition(anExtruder, 0, 1 * platformStepMultiplier) - currentPosition).sqrMagnitude; 
		float horizStepDistnce = (GetCartesianPosition(anExtruder, 1, 0) - currentPosition).sqrMagnitude; 
		float maxHalfStepDist = (platStepDistance + horizStepDistnce) / 2f;

		if (bestDistance > maxHalfStepDist * 1.05f) {
			for(int hStep = -1; hStep < 2; hStep++) {
				for(int pStep = -1; pStep < 2; pStep++) {
					if (hStep == 0 && pStep == 0) continue;


					Vector2 nextStepPosition = GetCartesianPosition(anExtruder, hStep, pStep * platformStepMultiplier);
					float nextStepDistance = (targetPosition - nextStepPosition).sqrMagnitude;

					if (bestDistance > nextStepDistance && !Mathf.Approximately(nextStepDistance, currentDistance)) {
						if (ignoreOppositeOfThisSegment != null &&
						    hStep == -ignoreOppositeOfThisSegment.horizontalStep && 
						    pStep == -ignoreOppositeOfThisSegment.platformStep) 
						{
							/*Text.Error("Printing along line (" + aSegment.ToString() + 
							           ") from current position " + currentPosition + 
							           " with a best distance = " + bestDistance +
							           " and nextStepDistance = " + nextStepDistance +
							           ": Taking the easy way out instead of triggering Mathf.Approximately correctly");*/
							continue;
						}
						Vector2 projectedPoint = MathUtil.ProjectPointOntoLine(fromPosition, 
							targetPosition, nextStepPosition);
						float nextStepError = (projectedPoint - nextStepPosition).sqrMagnitude;
						
						if (nextStepError < minimumStepError) {
							minimumStepError = nextStepError;
							//bestDistance = nextStepDistance;
							bestHorizMove = hStep;
							bestPlatMove = pStep;
						}
					}
				}
			}
		}
		else {
			//Debug.LogWarning("Exiting with no step when at position : " + currentPosition + " and target position " + targetPosition + "\n" + "best, plat, horiz distances: " + bestDistance + ", " + platStepDistance + ", " + horizStepDistnce);
		}

		float bestMoveDistance = (GetCartesianPosition(anExtruder,0,0) - 
			GetCartesianPosition(anExtruder, bestHorizMove, bestPlatMove)).magnitude;
		int targetStepRate = Mathf.CeilToInt((bestMoveDistance / kTargetMmPerSec) *
		                                     (TickProfile.kCyclesPerSecond / TickProfile.kMotorDelayInCycles)); 
		// mm  /  (mm / s)  *  ((cycles / s) / (cycles / tick)) = s * (ticks / s) = ticks

		return new PrintedOutlineSegment(bestPlatMove, bestHorizMove, bestMoveDistance, targetStepRate, targetStepRate);
	}
	
	/// <summary>
	/// Gets the cartesian position for the step deltas provided.
	/// </summary>
	/// <returns>
	/// A cartesian position.
	/// </returns>
	/// <param name='horizStepDelta'>
	/// Horiz step delta.
	/// </param>
	/// <param name='platformStepDelta'>
	/// Platform step delta.
	/// </param>
	public Vector2 GetCartesianPosition(PrinterExtruder anExtruder, int horizStepDelta, int platformStepDelta) {
		Vector2 polarPos = GetPolarPosition(anExtruder, horizStepDelta, platformStepDelta);
		//Debug.Log("Cartesian Position for h,p " + horizStepDelta + ", " + platformStepDelta + "  is " + 
		//	MathUtil.PolarToCartesian(m_workingPrinter, polarPos.x, polarPos.y));
		return MathUtil.PolarToCartesian(polarPos.x, polarPos.y);
	}

	private Vector2 GetCartesianPosition(Vector2 aPolarPosition) {
		return MathUtil.PolarToCartesian(aPolarPosition.x, aPolarPosition.y);
	}
	
	/// <summary>
	/// Gets the polar position for the given extruder and step deltas.
	/// </summary>
	/// <returns>
	/// The polar position.
	/// </returns>
	/// <param name='anExtruder'>
	/// An extruder.
	/// </param>
	/// <param name='horizStepDelta'>
	/// Horiz step delta.
	/// </param>
	/// <param name='platformStepDelta'>
	/// Platform step delta.
	/// </param>
	private Vector2 GetPolarPosition(PrinterExtruder anExtruder, int horizStepDelta, int platformStepDelta) {
		float r = ((float)m_horizTrack.position - m_workingPrinter.platformRadiusInMm ) +
					horizStepDelta * Mathf.Abs(m_horizTrack.distancePerStep) +
					anExtruder.ringNumber * PrinterExtruder.kStandardNozzleWidth;

		float theta = (float)(m_platform.rotationInSteps + platformStepDelta) / 
			(float)m_platform.stepsPerRotation * Mathf.PI * 2f;
		if (r < 0) {
			r = Mathf.Abs(r);
			theta -= Mathf.PI;
		}
		//Debug.Log("Horizontal Position " + m_horizTrack.position + " and ringNumber " +
		//	anExtruder.ringNumber + " result in a Polar Position = " + r + ",   " + theta);
		return new Vector2(r, theta);
	}
	
	#region Movement to points not along line

	private void MoveToLineStart(PrinterExtruder anExtruder, CartesianSegment aSegment, bool useConstantSpeedOutlining, List<PrintedLine> printedOutlines) {
		//Debug.Log("Moving to initial point: " + aSegment.p0 + " from " +  GetCartesianPosition(anExtruder, 0,0));
		//Vector2 fromPoint = GetCartesianPosition(anExtruder, 0,0);
		Vector2 targetPoint = aSegment.p0;
		
		Vector2 currPolarPos = GetPolarPosition(anExtruder, 0, 0);
		Vector2 targetPolarPos = MathUtil.CartesianToPolar(targetPoint);
		Vector2 targetEndPolarPos = MathUtil.CartesianToPolar(aSegment.p1);
		
		float targetTheta = targetPolarPos.y;
		float distanceToMove = currPolarPos.x - targetPolarPos.x;
		bool isOnPositiveSideOfPlatform = m_horizTrack.position > m_workingPrinter.platformRadiusInMm - 
			PrinterExtruder.kStandardNozzleWidth * anExtruder.ringNumber;
		
		int numHorizSteps = Mathf.RoundToInt(Mathf.Abs((distanceToMove) / m_horizTrack.distancePerStep));
		int horizDir = 0;
		if (isOnPositiveSideOfPlatform) {
			horizDir = (targetPolarPos.x > currPolarPos.x) ? 1 : -1;
		}
		else {
			horizDir = (targetPolarPos.x > currPolarPos.x) ? -1 : 1;
		}
		
		float maxRadius = PrinterExtruder.kStandardNozzleWidth * anExtruder.ringNumber + m_workingPrinter.movableDistanceInMm;
		/// if either start or end is past the max radius, we should move to the negative side of the platform
		if (isOnPositiveSideOfPlatform && 
		    (targetPolarPos.x > maxRadius || targetEndPolarPos.x > maxRadius)) 
		{
			Debug.LogWarning("Moving to opposite side this time");
			/// We need to target the negative side position
			/// and add PI to the angle
			targetTheta += Mathf.PI;
			targetTheta = targetTheta % (2f * Mathf.PI);
			
			float modDistanceToMove = Mathf.Abs(currPolarPos.x) + Mathf.Abs(targetPolarPos.x);
			numHorizSteps = Mathf.RoundToInt(Mathf.Abs((modDistanceToMove) / m_horizTrack.distancePerStep));
			horizDir =  -1;
		}
		
		float angleToMove = (currPolarPos.y - targetTheta) % (2f * Mathf.PI);
		if (angleToMove > Mathf.PI) angleToMove = -(2f * Mathf.PI - angleToMove);
		else if (angleToMove < - Mathf.PI) angleToMove = (2f * Mathf.PI + angleToMove);
		
		int numPlatSteps = Mathf.RoundToInt(Mathf.Abs((angleToMove) / m_platform.radiansPerStep));
		int platDir = angleToMove < 0 ? 1 : -1;
		
		
		if (useConstantSpeedOutlining) {
			
			if (numPlatSteps > 0 || numHorizSteps > 0) {
				PrintedLine plTest = new PrintedLine(false, numPlatSteps, platDir, numHorizSteps, horizDir);
				plTest.extruder = anExtruder;
				printedOutlines.Add(plTest);

				if (numHorizSteps > 0) {
					m_horizTrack.stepDirection = plTest.horizontalDirection;
					m_horizTrack.Step(plTest.horizontalSteps);
					m_lastHorizStep = -(int)plTest.horizontalDirection;
				}
				if (numPlatSteps > 0) {
					m_platform.stepDirection = plTest.platformDirection;
					m_platform.Step(plTest.platformSteps);
					m_lastPlatformStep = (int)plTest.platformDirection;
				}
				
				plTest.tickLength = Mathf.Max(plTest.platformSteps * kTargetPlatFastRate, plTest.horizontalSteps * kTargetHorizFastRate);

				Contract.Assert(plTest.platformSteps <= m_platform.stepsPerRotation / 2f, "For some reason we are rotating {0} steps, " +
				                "which is more than 1/2 way around the platform ({1} steps)", plTest.platformSteps, m_platform.stepsPerRotation / 2f);
				Contract.Assert(plTest.horizontalSteps * m_horizTrack.distancePerStep <= 70f, "We just moved {0} mm horizontally to the start of a line, " +
					"which is abnormally far... are there multiple small objects placed far apart? - we should try to figure out why this happened", 
				                plTest.horizontalSteps * m_horizTrack.distancePerStep);

				Contract.Assert(plTest.tickLength > 0, "Created a 0-length line for moving to the start of a line: " + plTest.ToString());
				Contract.Assert(plTest.horizontalSteps > 0 || plTest.platformSteps > 0, "Created a line with 0 horizontal and 0 platform steps");
			}
		}
		else {

			PrintedLine currentLine = new PrintedLine(false);//printedOutlines[0];
			printedOutlines.Add(currentLine);
			currentLine.extruder = anExtruder;
			
			int maxLength = Mathf.Max(numPlatSteps, numHorizSteps);
			
			for (int i = 0; i < maxLength; i++) {
				PrintedOutlineSegment seg = new PrintedOutlineSegment(0,0,0,0,0);
				if (i < numHorizSteps) {
					seg.horizontalStep = horizDir;
					m_horizTrack.stepDirection = (seg.horizontalStep > 0) ? StepDirection.Ccw : StepDirection.Cw;
					m_horizTrack.Step(1);
					m_lastHorizStep = horizDir;
					
				}
				if (i < numPlatSteps) {
					seg.platformStep = platDir;
					m_platform.stepDirection = (seg.platformStep > 0) ? StepDirection.Cw : StepDirection.Ccw;
					m_platform.Step(1);
					m_lastPlatformStep = platDir;
				}
				currentLine.Add(seg);
			}
		}
	}

	string CurrentPositionString {
		get {
			Vector2 p =  GetPolarPosition(m_workingPrinter.extruders[2], 0, 0);
			Vector2 c = GetCartesianPosition(m_workingPrinter.extruders[2], 0, 0);
			return "Current Polar Position: (" + p.x + ", " + p.y + ") and Current Cartesian Position: (" + c.x + ", " + c.y + ")" ;
		}
	}

	#endregion
	
	#region Conversion to Profiles

	public int ConvertStepsToProfiles(int fromTick,  Printer withPrinter,
	                                  List<PrintedLine> lines, 
	                                  List<TickProfile>[] m_assignedProfiles, int[] currentPressureAmounts) {
		int currentTick = fromTick;

		/// Optimize press/depress using this and the search below 
		for(int lineIndex = 0; lineIndex < lines.Count; lineIndex++) {
			PrintedLine currentLine = lines[lineIndex];

			/// Searchto find when each extruder is used next  
			Dictionary<PrinterExtruder, int> ticksTilExtruderUsed = new Dictionary<PrinterExtruder, int>();
			int tempTickCount = 0;
			for (int searchLine = lineIndex; searchLine < lines.Count; searchLine++) {
				PrinterExtruder pe = lines[searchLine].extruder;
				if (pe == null) continue;

				if (lines[searchLine].isExtruderUsed) {
					if (!ticksTilExtruderUsed.ContainsKey(pe)) ticksTilExtruderUsed.Add(pe, tempTickCount);
				}
				tempTickCount += lines[searchLine].tickLength;
			}
			//Debug.LogWarning("Used count " + ticksTilExtruderUsed.Count + " with outline.count and index = " + lines.Count + ", " + lineIndex);


			int prelinePressureShift = 0;
			foreach(PrinterExtruder pe in m_workingPrinter.extruders) {
				if (ticksTilExtruderUsed.ContainsKey(pe) && ticksTilExtruderUsed[pe] == 0 && pe.pressureRequired > 0) { // currentPressureAmounts[pe.id] < TickProfile.kPressureSteps) {
					int numStepsReq = pe.pressureRequired; //TickProfile.kPressureSteps - currentPressureAmounts[pe.id];
					prelinePressureShift = Mathf.Max(prelinePressureShift, numStepsReq * PrinterExtruder.kDefaultTickRate);
				}
			}
			Contract.Assert(prelinePressureShift <= PrinterExtruder.kDefaultTickRate * TickProfile.kPressureSteps, 
			                "Created a shift {0} that is bigger than the max number of pressure steps {1}",
			                prelinePressureShift, PrinterExtruder.kDefaultTickRate * TickProfile.kPressureSteps);
			Contract.Assert(prelinePressureShift >= 0, "Created a pressure shift less than zero = {0}", prelinePressureShift);

			for(int p = 0; p < m_workingPrinter.extruders.Length; p++) {
				PrinterExtruder pe = m_workingPrinter.extruders[p];
				if (!ticksTilExtruderUsed.ContainsKey(pe) && pe.pressureRequired == TickProfile.kPressureSteps) continue; //  currentPressureAmounts[pe.id] == 0) continue;

				int nextTickStartForExtruder = (ticksTilExtruderUsed.ContainsKey(pe) ? prelinePressureShift + ticksTilExtruderUsed[pe] : int.MaxValue);
				int pressureRequiredForExtruder = pe.pressureRequired; // TickProfile.kPressureSteps - currentPressureAmounts[pe.id];

				if (nextTickStartForExtruder == pressureRequiredForExtruder * PrinterExtruder.kDefaultTickRate ) {

					// Is the amount that we have to pressurize equal to or greater than the shift and ticks til the extruder is used?
					// Assert that it is not greater than... if it is that means we are not setting the shift correctly
					Contract.Assert(nextTickStartForExtruder == pressureRequiredForExtruder * PrinterExtruder.kDefaultTickRate,
					                "Error in creating pressure shift... \nPressure shift = {0}, Ticks til extruder.id {1} is used = {2}, depressure req = {3}",
					                prelinePressureShift, m_workingPrinter.extruders[p].id, ticksTilExtruderUsed[pe], pe.pressureRequired); // currentPressureAmounts[m_workingPrinter.extruders[p].id]);

					if (pressureRequiredForExtruder > 0) {
						int ticksToPressurizeExtruder = pressureRequiredForExtruder * PrinterExtruder.kDefaultTickRate;
						m_assignedProfiles[pe.id].Add(new TickProfile(pe, pe.stepSize, StepDirection.Ccw,
							PrinterExtruder.kDefaultTickRate, currentTick, ticksToPressurizeExtruder));
						pe.pressureRequired = 0;
						//currentPressureAmounts[pe.id] = TickProfile.kPressureSteps;
						Contract.Assert(ticksToPressurizeExtruder > 0, "Extruder pressure required was negative {0}", pressureRequiredForExtruder);
						Contract.Assert(ticksToPressurizeExtruder < TickProfile.kPressureSteps * PrinterExtruder.kDefaultTickRate, 
						                "Extruder pressurized too much... calculated {0}, but max is {1}",
						                ticksToPressurizeExtruder, TickProfile.kPressureSteps * PrinterExtruder.kDefaultTickRate);
					}

				}
				else {
					// Find out when we need to start moving forward, then given the remaining number of ticks, how much we can backup
					int tickCountTilPressurization = Mathf.Max(0, nextTickStartForExtruder - pressureRequiredForExtruder * PrinterExtruder.kDefaultTickRate);

					int maxDepressureStepsPossible = Mathf.FloorToInt(((float)tickCountTilPressurization / (float)PrinterExtruder.kDefaultTickRate) / 2f);
					int depressureStepsToTake = Mathf.Min(TickProfile.kPressureSteps - pe.pressureRequired, maxDepressureStepsPossible); //     currentPressureAmounts[pe.id], maxDepressureStepsPossible);
					depressureStepsToTake = Mathf.Min(depressureStepsToTake, 
					                                  Mathf.FloorToInt((prelinePressureShift + currentLine.tickLength) / (float)PrinterExtruder.kDefaultTickRate));

					Contract.Assert(depressureStepsToTake <= TickProfile.kPressureSteps - pe.pressureRequired, // currentPressureAmounts[pe.id], 
					                "Depressurizing {0} which is too much ({1})!", depressureStepsToTake, 
					                TickProfile.kPressureSteps - pe.pressureRequired); //currentPressureAmounts[pe.id]);
					Contract.Assert(depressureStepsToTake >= 0, "Depressurizing {0} which is negative!", depressureStepsToTake);

					if(depressureStepsToTake > 0) {
						m_assignedProfiles[pe.id].Add(new TickProfile(pe, pe.stepSize, StepDirection.Cw,
							PrinterExtruder.kDefaultTickRate, currentTick, depressureStepsToTake * PrinterExtruder.kDefaultTickRate));
						pe.pressureRequired += depressureStepsToTake;
						//currentPressureAmounts[pe.id] -= depressureStepsToTake;
					}

					Contract.Assert(pe.pressureRequired <= TickProfile.kPressureSteps, //  currentPressureAmounts[pe.id] >= 0, 
					                "Backed up {0}, which is too much resulting in depressureAmt = {1}", 
					                depressureStepsToTake, pe.pressureRequired); // currentPressureAmounts[pe.id]);
					Contract.Assert(pe.pressureRequired >= 0, // currentPressureAmounts[pe.id] <= TickProfile.kPressureSteps, 
					                "Pressurized too much, now at: {0}", pe.pressureRequired); //currentPressureAmounts[pe.id]);
					Contract.Assert(depressureStepsToTake >= 0, "Backup calculated was negative {0}", depressureStepsToTake);

					// If we backup that much, do we need to start pressurizing before the end of the current line?
					int newPressureRequired = pe.pressureRequired; // TickProfile.kPressureSteps - currentPressureAmounts[pe.id];
					if (nextTickStartForExtruder - newPressureRequired * PrinterExtruder.kDefaultTickRate < prelinePressureShift + currentLine.tickLength) {
						int pressureStartTick = Mathf.Max(0, nextTickStartForExtruder - newPressureRequired * PrinterExtruder.kDefaultTickRate);
						int numTicksForStepping = (prelinePressureShift + currentLine.tickLength) - pressureStartTick;
						numTicksForStepping = Mathf.Max(0, numTicksForStepping);
						int pressureStepCount = Mathf.FloorToInt((float)numTicksForStepping / (float)PrinterExtruder.kDefaultTickRate);
						numTicksForStepping = pressureStepCount * PrinterExtruder.kDefaultTickRate;

						if (numTicksForStepping > 0) {
							m_assignedProfiles[pe.id].Add(new TickProfile(pe, pe.stepSize, StepDirection.Ccw,
								PrinterExtruder.kDefaultTickRate, currentTick + pressureStartTick, numTicksForStepping));
							pe.pressureRequired -= pressureStepCount;
							//currentPressureAmounts[pe.id] += pressureStepCount;
						}

						Contract.Assert(pressureStartTick >= 0, "Calculated a pressure start that was negative {0}", pressureStartTick);
						Contract.Assert(pressureStartTick <= prelinePressureShift + currentLine.tickLength, "Starting line in {0} ticks, when it should be before {1} ticks",
						                pressureStartTick, prelinePressureShift + currentLine.tickLength);
						Contract.Assert(numTicksForStepping > 0, "Number of ticks calculated for depressurizing = " + numTicksForStepping);
						Contract.Assert(numTicksForStepping <= TickProfile.kPressureSteps * PrinterExtruder.kDefaultTickRate, 
						                "Number of ticks for movement {0}, starting at {2}, was larger than the max number of ticks we would need to max out pressure {1}",
						                numTicksForStepping, TickProfile.kPressureSteps * PrinterExtruder.kDefaultTickRate, pressureStartTick);
					}
				}
			}
			currentTick += prelinePressureShift;
			

			// Handle line to line movement
			if (!currentLine.isExtruderUsed) {
				// Handle platform and horizontal movement here
				if (currentLine.platformSteps > 0) m_assignedProfiles[0].Add(new TickProfile(m_platform, m_platform.stepSize, currentLine.platformDirection,
				                                          kTargetPlatFastRate, currentTick, currentLine.platformSteps * kTargetPlatFastRate));
				if (currentLine.horizontalSteps > 0) m_assignedProfiles[1].Add(new TickProfile(m_horizTrack, m_horizTrack.stepSize, currentLine.horizontalDirection,
				                                          kTargetHorizFastRate, currentTick, currentLine.horizontalSteps * kTargetHorizFastRate));
				int maxTicksNeededForMovement = Mathf.Max(currentLine.platformSteps * kTargetPlatFastRate, currentLine.horizontalSteps * kTargetHorizFastRate);

				currentTick += maxTicksNeededForMovement; // Mathf.Max(aLine.platformSteps * kTargetPlatFastRate, aLine.horizontalSteps * kTargetHorizFastRate);

				continue;
			}

			// Calculate the extrusion rate
			int extruderStepRate = Mathf.RoundToInt(1f / ((kTargetMmPerSec * PrinterExtruder.kStandardNozzleWidth * withPrinter.layerHeightInMm / 
			                                               currentLine.extruder.volumePerStep) * TickProfile.kMotorDelayInCycles / TickProfile.kCyclesPerSecond)); 
			Contract.Assert(extruderStepRate > 0, "Extruder rate = {0} will not work for printing along a line.", extruderStepRate);
			// ticks = 1 /  (mm^3 / s   /   mm^3 / step   *   (cycles / tick) / (cycles / s)) = 1 / (step / s   *   s / tick) = 1 / (step / tick) = ticks / step
			//Debug.Log("Extruder rate = " + extruderStepRate);

			/// come up with the extruder profile length
			int extruderProfileLength = currentLine.tickLength - (currentLine.tickLength % extruderStepRate);
			if (extruderProfileLength < extruderStepRate) {
				extruderProfileLength = currentLine.tickLength;
				extruderStepRate = currentLine.tickLength;
			}
			Contract.Assert(extruderProfileLength > 0, "Extruder profile length = {0} at rate {2} will not work for printing along line: {1}", 
			                extruderProfileLength,  currentLine.ToString(), extruderStepRate);
			m_assignedProfiles[currentLine.extruder.id].Add(new TickProfile(currentLine.extruder, currentLine.extruder.stepSize, StepDirection.Ccw,
				                                                          extruderStepRate, currentTick, extruderProfileLength));

			Contract.Assert(extruderStepRate > 383, "Created line extrusion profile with steprate < 384 : " + new TickProfile(currentLine.extruder, currentLine.extruder.stepSize, StepDirection.Ccw,
			                                                                                                                  extruderStepRate, currentTick, extruderProfileLength).ToString());


			Arc platArc, horizArc;
			Arc prevPlatArc = null;
			Arc prevHorizArc = null;
			PrintedOutlineSegment segment = null;
			PrintedOutlineSegment prevSegment = null;

			for(int i = 0; i < currentLine.segments.Count ; i++) { //   each(PrintedOutlineSegment segment in aLine.segments) {
				segment = currentLine.segments[i];
				platArc = segment.PlatformArc(m_platform, 0);
				horizArc = segment.HorizontalArc(m_horizTrack, 0);

				if (platArc != null) {
					if (prevSegment != null && prevPlatArc != null && segment.platformStepRate == prevSegment.platformStepRate &&
					    prevPlatArc.direction == platArc.direction) {
						m_assignedProfiles[0][m_assignedProfiles[0].Count - 1].tickLength += segment.platformStepRate;
					}
					else {
						m_assignedProfiles[0].Add(new TickProfile(m_platform, m_platform.stepSize, platArc.direction, segment.platformStepRate,
				                                          currentTick, segment.platformStepRate));
					}
				}

				if (horizArc != null) {
					if (prevSegment != null && prevHorizArc != null && segment.horizontalStepRate == prevSegment.horizontalStepRate &&
					    prevHorizArc.direction == horizArc.direction) {
						m_assignedProfiles[1][m_assignedProfiles[1].Count - 1].tickLength += segment.horizontalStepRate;
					}
					else {
						m_assignedProfiles[1].Add(new TickProfile(m_horizTrack, m_horizTrack.stepSize, horizArc.direction, segment.horizontalStepRate,
						                                          currentTick, segment.horizontalStepRate));
					}
				}

				/// This next line assumes that platformsteprate = horizontal step rate !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
				currentTick += segment.horizontalStepRate;
				prevPlatArc = platArc;
				prevHorizArc = horizArc;
			    prevSegment = segment;
			}


		}

		for(int p = 0; p < m_workingPrinter.extruders.Length; p++) {
			PrinterExtruder pe = m_workingPrinter.extruders[p];
			currentPressureAmounts[pe.id] = TickProfile.kPressureSteps - pe.pressureRequired;
		}
			
		return currentTick;
	}


	public int ConvertStepsToArcs(int fromStep, Printer withPrinter, 
	                              List<Arc>[] m_assignedArcs,
	                            List<PrintedLine> lines) 
	{
		/// Conversion example
		/// TickProfile.Convert(m_assignedArcs[0], m_assignedProfiles[0],
		/// 	platformStepRate, false);
		/// m_assignedArcs[0].Clear();

		int currentStep = fromStep;

		for(int lineIndex = 0; lineIndex < lines.Count; lineIndex++) {
			if (!lines[lineIndex].shouldAccelerateMovement && lines[lineIndex].segments.Count == 0) {
				//Text.Log("segments was empty for lineIndex = " + lineIndex);
				currentStep++;
				continue;
			}

			Arc aPlatArc, aHorizArc, anExtruderArc;

			if (!lines[lineIndex].isExtruderUsed && lines[lineIndex].shouldAccelerateMovement) {
				if (lines[lineIndex].platformSteps != 0) {
					aPlatArc = new Arc(m_platform, currentStep);
					aPlatArc.endStep = currentStep + lines[lineIndex].platformSteps;
					aPlatArc.direction = lines[lineIndex].platformDirection;
					m_assignedArcs[0].Add(aPlatArc);
				}
				if (lines[lineIndex].horizontalSteps != 0) {
					aHorizArc = new Arc(m_horizTrack, currentStep);
					aHorizArc.endStep = currentStep + lines[lineIndex].horizontalSteps;
					aHorizArc.direction = lines[lineIndex].horizontalDirection;
					m_assignedArcs[1].Add(aHorizArc);
				}
				currentStep += Mathf.Max(lines[lineIndex].horizontalSteps, lines[lineIndex].platformSteps);
				continue;
			}

			// Create the platform arc and add if not null
			aPlatArc = lines[lineIndex].segments[0].PlatformArc(withPrinter.platform, currentStep);
			if (aPlatArc != null) m_assignedArcs[0].Add(aPlatArc);

			// Create the horizontal arc and add if not null
			aHorizArc = lines[lineIndex].segments[0].HorizontalArc(withPrinter.horizTrack, currentStep);
			if (aHorizArc != null) m_assignedArcs[1].Add(aHorizArc);

			// Create the extruder arc and add if not null
			//Contract.Assert(anExtruderArc == null || lines[lineIndex].isExtruderUsed, "Error in setting up outline extrusion arcs");
			anExtruderArc = null;
			if (lines[lineIndex].isExtruderUsed) {
				anExtruderArc = lines[lineIndex].segments[0].ExtruderArc(lines[lineIndex].extruder, currentStep);
				anExtruderArc.endStep += lines[lineIndex].segments.Count - 1;
				m_assignedArcs[anExtruderArc.motor.id].Add(anExtruderArc);
				int extruderArcCount = m_assignedArcs[anExtruderArc.motor.id].Count;
				m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1].hasVariableStepRate = true;
				m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1].SetNumberOfVariableSteps(
					lines[lineIndex].numExtruderSteps);
				if (lines[lineIndex].segments[0].extruderStep != 0) {
					m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1].AddStepLocation(0);
				}
			}

			currentStep++;
			for(int segIndex = 1; segIndex < lines[lineIndex].segments.Count; segIndex++) {
				// Check platform step
				if (lines[lineIndex].segments[segIndex].platformStep ==
				    lines[lineIndex].segments[segIndex - 1].platformStep &&
				    aPlatArc != null) 
				{
					m_assignedArcs[0][m_assignedArcs[0].Count - 1].endStep++;
				}
				else {
					aPlatArc = lines[lineIndex].segments[segIndex].PlatformArc(
						withPrinter.platform, currentStep);
					if (aPlatArc != null) m_assignedArcs[0].Add(aPlatArc);
				}
				// Check horizontal step
				if (lines[lineIndex].segments[segIndex].horizontalStep ==
				    lines[lineIndex].segments[segIndex - 1].horizontalStep &&
				    aHorizArc != null) 
				{
					m_assignedArcs[1][m_assignedArcs[1].Count - 1].endStep++;
				}
				else {
					aHorizArc = lines[lineIndex].segments[segIndex].HorizontalArc(
						withPrinter.horizTrack, currentStep);
					if (aHorizArc != null) m_assignedArcs[1].Add(aHorizArc);
				}
				// Check Extruder step
				if (lines[lineIndex].segments[segIndex].extruderStep == 1 && anExtruderArc != null) {
					int extruderArcCount = m_assignedArcs[anExtruderArc.motor.id].Count;
					m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1].hasVariableStepRate = true;
					m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1].AddStepLocation(
						currentStep - m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1].startStep);
					Contract.Assert(currentStep - m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1].startStep > 0,
					                "Setting up an incorrect step of (" + 
					                (currentStep - m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1].startStep) + 
					                ") for " + m_assignedArcs[anExtruderArc.motor.id][extruderArcCount - 1]);
					//Debug.LogWarning("Adding relative step at " + (currentStep - initialStep) + " to arc " + anExtruderArc);
				}

				currentStep++;
			}
		
		}

		return currentStep;
	}

	#endregion


	public class PrintedLine {
		public List<PrintedOutlineSegment> segments;
		public bool isExtruderUsed;
		public PrinterExtruder extruder;
		public int numExtruderSteps = 0;
		public bool shouldAccelerateMovement = false;
		public int tickLength = 0;

		public int platformSteps = 0;
		public StepDirection platformDirection = StepDirection.Unknown;
		public int horizontalSteps = 0;
		public StepDirection horizontalDirection = StepDirection.Unknown;


		public PrintedLine(bool isPrintingOnLine) {
			this.segments = new List<PrintedOutlineSegment>();
			this.isExtruderUsed = isPrintingOnLine;
		}

		public PrintedLine(bool extruderUsed, int numPlatformSteps, 
		                   int platformDir, int numHorizSteps, int horizDir) {
			this.isExtruderUsed = extruderUsed;
			platformSteps = numPlatformSteps;
			platformDirection = platformDir == 1 ? StepDirection.Cw : StepDirection.Ccw;
			horizontalSteps = numHorizSteps;
			horizontalDirection = horizDir == 1 ? StepDirection.Ccw : StepDirection.Cw;
			this.segments = new List<PrintedOutlineSegment>(); 
		}

		public void Add(PrintedOutlineSegment aNewSegment) {
			tickLength += Mathf.Max(aNewSegment.horizontalStepRate, aNewSegment.platformStepRate);
			segments.Add(aNewSegment);
		}

		public override string ToString()
		{
			return string.Format("[PrintedLine]: {5} Platform steps - {0}, {1}, Horizontal steps - {2}, {3}, Tick Length - {4}", 
			                     platformSteps, platformDirection, horizontalSteps, horizontalDirection, tickLength, (isExtruderUsed ? "Extruding for" : "Not Extruding for"));
		}
	}
	
	public class PrintedOutlineSegment {
		
		public PrintedOutlineSegment(int aPlatformStep, int aHorizontalStep, float movementDistance, int aPlatStepRate, int aHorizStepRate) {
			this.platformStep = aPlatformStep;
			this.horizontalStep = aHorizontalStep;
			this.extruderStep = 0;
			this.length = movementDistance;
			this.platformStepRate = aPlatStepRate;
			this.horizontalStepRate = aHorizStepRate;
		}

		public int platformStep;
		public int horizontalStep;
		public int extruderStep = 0;
		public float length;
		public int platformStepRate = -1;
		public int horizontalStepRate = -1;


		public bool isEqualTo(PrintedOutlineSegment pos) {
			return (platformStep == pos.platformStep && horizontalStep == pos.horizontalStep &&
			        extruderStep == pos.extruderStep);
		}

		public Arc PlatformArc(PrinterMotor platform, int currentStep) {
			if (platformStep == 0) return null;
			Arc aPlatArc = new Arc(platform, currentStep);
			aPlatArc.endStep = currentStep + 1;
			aPlatArc.direction = platformStep == 1 ? StepDirection.Cw : StepDirection.Ccw;
			return aPlatArc;
		}

		public Arc HorizontalArc(PrinterMotor horizTrack, int currentStep) {
			if (horizontalStep == 0) return null;
			Arc aHorizArc = new Arc(horizTrack, currentStep);
			aHorizArc.endStep = currentStep + 1;
			aHorizArc.direction = horizontalStep == 1 ? StepDirection.Ccw : StepDirection.Cw;
			return aHorizArc;
		}

		public Arc ExtruderArc(PrinterExtruder anExtruder, int currentStep) {
			//if (extruderStep == 0) return null; /// This is no longer needed because we need to be able to start
			/// arcs that do not have a first step - extruder step
			Arc anExtruderArc = new Arc(anExtruder, currentStep);
			anExtruderArc.endStep = currentStep + 1;
			anExtruderArc.direction = StepDirection.Ccw;
			return anExtruderArc;
		}
		
		public override string ToString() {
			return string.Format("PrintedOutlineSegment : pStep = {0} at rate {3}, hStep = {1} at rate {4}, length = {2} ", this.platformStep, 
			                     this.horizontalStep, this.length, this.platformStepRate, this.horizontalStepRate);
		}
	}


}
