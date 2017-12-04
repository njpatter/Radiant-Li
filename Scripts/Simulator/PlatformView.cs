using UnityEngine;
using System.Collections;
using System.Collections.Generic;
 
public class PlatformView : MonoBehaviour, ICommandConsumer {
	#if !UNITY_IOS && !UNITY_ANDROID
	const int kExtruderBase = 4;
	const float kUncalibrated = float.MinValue;
	
	public PrinterController printController;
	public Vector3 positionOffset;
	public bool onlyCollectExtruded = false;
	
	Printer m_simulation;
	Transform m_trans;
	
	delegate void CommandHandler(int anArg);
	Dictionary<GanglionCommand, CommandHandler> Execute;
	
	public readonly Dictionary<int, StepSize> kStepConversion = new Dictionary<int, StepSize> {
		{ (int)GanglionStep.Whole,     StepSize.Whole },
		{ (int)GanglionStep.Half,      StepSize.Half },
		{ (int)GanglionStep.Quarter,   StepSize.Quarter },
		{ (int)GanglionStep.Eighth,    StepSize.Eighth },
		{ (int)GanglionStep.Sixteenth, StepSize.Sixteenth }
	};
	
	#region Object visualization
	Dictionary<int, List<Vector4>> extrudedPoints;
	
	public bool[] isEnabled = new bool[8];
	
	Color[] colorTable = new Color[] {
		// Extruder 0
		new Color(1.0f, 0.0f, 0.0f, 1.0f), new Color(0.5f, 0.0f, 0.0f, 1.0f),
		// Extruder 1
		new Color(0.0f, 1.0f, 0.0f, 1.0f), new Color(0.0f, 0.5f, 0.0f, 1.0f),
		// Extruder 2
		new Color(0.0f, 0.0f, 1.0f, 1.0f), new Color(0.0f, 0.0f, 0.5f, 1.0f),
		// Extruder 3
		new Color(1.0f, 1.0f, 1.0f, 1.0f), new Color(0.5f, 0.5f, 0.5f, 1.0f),
		// Movement Only Color for Extruder 0
		new Color(1.0f, 0.0f, 1.0f, 1.0f), new Color(1.0f, 0.0f, 1.0f, 1.0f)
	};
	#endregion
	
	#region Simulation State
	int m_tos;
	int m_extruderNum;
	int m_motor;
	int m_heater;
	int[] m_targetTemperatures;
	PrinterMotor[] m_motors;
	int[] m_motorSteps;
	long m_globalSteps;
	int m_powerState;
	#endregion
	
	void Awake() {
		m_trans = transform;
		
		InitializePrinter();
		CreateDispatchTable();
		
		Dispatcher<float>.AddListener(PrinterController.kOnInitializingProgress, 
			OnInitializationProgress);
	}
	
	void OnDestroy() {
		Dispatcher<float>.RemoveListener(PrinterController.kOnInitializingProgress, 
			OnInitializationProgress);
	}
	
	void OnInitializationProgress(float aValue) {
		if (Mathf.Approximately(aValue, 1.0f)) {
			Dispatcher.Broadcast(PrinterController.kOnReadyToPrint);
		}
	}
	
	void InitializePrinter() {
		m_simulation = printController.originalPrinter.Clone();
		movementWithoutExtrusion = new List<Vector4>();
		
		int numExtruders = m_simulation.extruders.Length;
		extrudedPoints = new Dictionary<int, List<Vector4>>();
		
		m_motors = new PrinterMotor[kExtruderBase + numExtruders];
		m_motors[0] = m_simulation.platform;
		m_motors[1] = m_simulation.horizTrack;
		m_motors[2] = m_simulation.vertTrack[0];
		m_motors[3] = m_simulation.vertTrack[1];
		
		for (int anExtruder = kExtruderBase; anExtruder < kExtruderBase + numExtruders; ++anExtruder) {
			m_motors[anExtruder] = m_simulation.extruders[anExtruder - kExtruderBase];
			extrudedPoints[anExtruder] = new List<Vector4>();
		}
		
		m_motorSteps = new int[m_motors.Length];
		for (int i = 0; i < m_motorSteps.Length; ++i) {
			m_motorSteps[i] = 0;	
		}
		m_globalSteps = 0L;
		m_tos = 0;
		m_extruderNum = 0;
		m_motor = 0;
		
		m_simulation.horizTrack.position = kUncalibrated;
	}
	
	void CreateDispatchTable() {
		Execute = new Dictionary<GanglionCommand, CommandHandler>();
		
		Execute[GanglionCommand.Error] = CmdError;
		Execute[GanglionCommand.BufferFreeP] = CmdBufferFreeP;
		Execute[GanglionCommand.Button] = CmdButton;
		Execute[GanglionCommand.Calibrate] = CmdCalibrate;
		Execute[GanglionCommand.Center] = CmdCenter;
		Execute[GanglionCommand.Clockwise] = CmdClockwise;
		Execute[GanglionCommand.CounterClockwise] = CmdCounterClockwise;
		Execute[GanglionCommand.Cycle] = CmdCycle;
		Execute[GanglionCommand.Data] = CmdData;
		Execute[GanglionCommand.DirectionP] = CmdDirectionP;
		Execute[GanglionCommand.Extruder0] = CmdExtruder0;
		Execute[GanglionCommand.Extruder1] = CmdExtruder1;
		Execute[GanglionCommand.Extruder2] = CmdExtruder2;
		Execute[GanglionCommand.Extruder3] = CmdExtruder3;
		Execute[GanglionCommand.Extruder4] = CmdExtruder4;
		Execute[GanglionCommand.Extruder5] = CmdExtruder5;
		Execute[GanglionCommand.Extruder6] = CmdExtruder6;
		Execute[GanglionCommand.Extruder7] = CmdExtruder7;
		Execute[GanglionCommand.G] = CmdG;
		Execute[GanglionCommand.Heat] = CmdHeat;
		Execute[GanglionCommand.Heater] = CmdHeater;
		Execute[GanglionCommand.HorizTrack] = CmdHorizTrack;
		Execute[GanglionCommand.LeftTrack] = CmdLeftTrack;
		Execute[GanglionCommand.Motor] = CmdMotor;
		Execute[GanglionCommand.MotorP] = CmdMotorP;
		Execute[GanglionCommand.Off] = CmdOff;
		Execute[GanglionCommand.On] = CmdOn;
		Execute[GanglionCommand.Ping] = CmdPing;
		Execute[GanglionCommand.Platform] = CmdPlatform;
		Execute[GanglionCommand.PressedP] = CmdPressedP;
		Execute[GanglionCommand.Reboot] = CmdReboot;
		Execute[GanglionCommand.RightTrack] = CmdRightTrack;
		Execute[GanglionCommand.Seek] = CmdSeek;
		Execute[GanglionCommand.Sleep] = CmdSleep;
		Execute[GanglionCommand.StackP] = CmdStackP;
		Execute[GanglionCommand.Step] = CmdStep;
		Execute[GanglionCommand.StepRate] = CmdStepRate;
		Execute[GanglionCommand.StepRateStep] = CmdStepRateStep;
		Execute[GanglionCommand.StepSize] = CmdStepSize;
		Execute[GanglionCommand.StepSizeP] = CmdStepSizeP;
		Execute[GanglionCommand.Stop] = CmdStop;
		Execute[GanglionCommand.TemperatureP] = CmdTemperatureP;
		Execute[GanglionCommand.TemperatureTargetP] = CmdTemperatureTargetP;
		Execute[GanglionCommand.Text] = CmdText;
		Execute[GanglionCommand.TokenP] = CmdTokenP;
		Execute[GanglionCommand.Value] = CmdValue;
		Execute[GanglionCommand.VersionP] = CmdVersionP;
		Execute[GanglionCommand.Wake] = CmdWake;
		Execute[GanglionCommand.WaitHeated] = CmdWaitHeated;
		Execute[GanglionCommand.WordsP] = CmdWords;

		Execute[GanglionCommand.StepRatePlatform]   = delegate(int anArg) { CmdStepRate(anArg); CmdPlatform(anArg);	};
		Execute[GanglionCommand.StepRateHorizTrack] = delegate(int anArg) { CmdStepRate(anArg); CmdHorizTrack(anArg); };
		Execute[GanglionCommand.StepRateLeftTrack]  = delegate(int anArg) { CmdStepRate(anArg); CmdLeftTrack(anArg);  };
		Execute[GanglionCommand.StepRateRightTrack] = delegate(int anArg) { CmdStepRate(anArg); CmdRightTrack(anArg); };
		Execute[GanglionCommand.StepRateExtruder0]  = delegate(int anArg) { CmdStepRate(anArg); CmdExtruder0(anArg); };
		Execute[GanglionCommand.StepRateExtruder1]  = delegate(int anArg) { CmdStepRate(anArg); CmdExtruder1(anArg); };
		Execute[GanglionCommand.StepRateExtruder2]  = delegate(int anArg) { CmdStepRate(anArg); CmdExtruder2(anArg); };
		Execute[GanglionCommand.StepRateExtruder3]  = delegate(int anArg) { CmdStepRate(anArg); CmdExtruder3(anArg); };
		Execute[GanglionCommand.StepRateExtruder4]  = delegate(int anArg) { CmdStepRate(anArg); CmdExtruder4(anArg); };
		Execute[GanglionCommand.StepRateExtruder5]  = delegate(int anArg) { CmdStepRate(anArg); CmdExtruder5(anArg); };
		Execute[GanglionCommand.StepRateExtruder6]  = delegate(int anArg) { CmdStepRate(anArg); CmdExtruder6(anArg); };
		Execute[GanglionCommand.StepRateExtruder7]  = delegate(int anArg) { CmdStepRate(anArg); CmdExtruder7(anArg); };
		Execute[GanglionCommand.PlatformStepRateStep]   = delegate(int anArg) { CmdPlatform(anArg);   CmdStepRateStep(anArg); };
		Execute[GanglionCommand.HorizTrackStepRateStep] = delegate(int anArg) { CmdHorizTrack(anArg); CmdStepRateStep(anArg); };
		Execute[GanglionCommand.LeftTrackStepRateStep]  = delegate(int anArg) { CmdLeftTrack(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.RightTrackStepRateStep] = delegate(int anArg) { CmdRightTrack(anArg); CmdStepRateStep(anArg); };
		Execute[GanglionCommand.Extruder0StepRateStep]  = delegate(int anArg) { CmdExtruder0(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.Extruder1StepRateStep]  = delegate(int anArg) { CmdExtruder1(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.Extruder2StepRateStep]  = delegate(int anArg) { CmdExtruder2(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.Extruder3StepRateStep]  = delegate(int anArg) { CmdExtruder3(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.Extruder4StepRateStep]  = delegate(int anArg) { CmdExtruder4(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.Extruder5StepRateStep]  = delegate(int anArg) { CmdExtruder5(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.Extruder6StepRateStep]  = delegate(int anArg) { CmdExtruder6(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.Extruder7StepRateStep]  = delegate(int anArg) { CmdExtruder7(anArg);  CmdStepRateStep(anArg); };
		Execute[GanglionCommand.PlatformStop]   = delegate(int anArg) { CmdPlatform(anArg);   CmdStop(anArg); };
		Execute[GanglionCommand.HorizTrackStop] = delegate(int anArg) { CmdHorizTrack(anArg); CmdStop(anArg); };
		Execute[GanglionCommand.LeftTrackStop]  = delegate(int anArg) { CmdLeftTrack(anArg);  CmdStop(anArg); };
		Execute[GanglionCommand.RightTrackStop] = delegate(int anArg) { CmdRightTrack(anArg); CmdStop(anArg); };
		Execute[GanglionCommand.Extruder0Stop]  = delegate(int anArg) { CmdExtruder0(anArg);  CmdStop(anArg); };
		Execute[GanglionCommand.Extruder1Stop]  = delegate(int anArg) { CmdExtruder1(anArg);  CmdStop(anArg); };
		Execute[GanglionCommand.Extruder2Stop]  = delegate(int anArg) { CmdExtruder2(anArg);  CmdStop(anArg); };
		Execute[GanglionCommand.Extruder3Stop]  = delegate(int anArg) { CmdExtruder3(anArg);  CmdStop(anArg); };
		Execute[GanglionCommand.Extruder4Stop]  = delegate(int anArg) { CmdExtruder4(anArg);  CmdStop(anArg); };
		Execute[GanglionCommand.Extruder5Stop]  = delegate(int anArg) { CmdExtruder5(anArg);  CmdStop(anArg); };
		Execute[GanglionCommand.Extruder6Stop]  = delegate(int anArg) { CmdExtruder6(anArg);  CmdStop(anArg); };
		Execute[GanglionCommand.Extruder7Stop]  = delegate(int anArg) { CmdExtruder7(anArg);  CmdStop(anArg); };
	}
	
	#region Command Handlers
	void CmdError(int anArg) {
		Text.Error("Received an error command from print controller.");
	}
	void CmdBufferFreeP(int anArg) { 
		Text.Log("Buffer check.");
	}
	void CmdButton(int anArg) {
		Text.Error("button not implemented.");
		//m_buttonNum = m_tos;
	}
	// TODO: Y-heights incorrect.
	void CmdCalibrate(int anArg) {
		//m_trans.localScale = new Vector3(m_simulation.platformRadiusInMm * 2,
		//	1, m_simulation.platformRadiusInMm * 2);
		CmdSeek(anArg);
		CmdCenter(anArg);
		//m_trans.localPosition = new Vector3(m_simulation.horizTrack.position,
		//	m_simulation.vertTrack[0].position, 0.0f);
	}
	void CmdCenter(int anArg) {
		m_simulation.horizTrack.position = m_simulation.platformRadiusInMm;
		Vector3 pos = m_trans.localPosition;
		pos.x = (float)m_simulation.horizTrack.position;
		m_trans.localPosition = pos;
	}
	void CmdClockwise(int anArg) {
		m_motors[m_motor].stepDirection = StepDirection.Cw;
	}
	void CmdCm(int anArg) {
		m_tos *= 10000;
	}
	void CmdCounterClockwise(int anArg) {
		m_motors[m_motor].stepDirection = StepDirection.Ccw;
	}
	void CmdCycle(int anArg) {
		Text.Warning("Cycle not supported in the simulator; should only be used in BST.");
	}
	void CmdData(int anArg) {
		// NOP - No feedback
	}
	void CmdDeg(int anArg) {
		Text.Warning("Received deg command, which should only be used interactively.");
	}
	void CmdDirectionP(int anArg) {
		Text.Log("Motor {0} direction: {1}", m_motor, m_motors[m_motor].stepDirection);
	}
	void CmdExtruder0(int anArg) {
		m_motor = kExtruderBase;
	}
	void CmdExtruder1(int anArg) {
		m_motor = kExtruderBase + 1;
	}
	void CmdExtruder2(int anArg) {
		m_motor = kExtruderBase + 2;
	}
	void CmdExtruder3(int anArg) {
		m_motor = kExtruderBase + 3;
	}
	void CmdExtruder4(int anArg) {
		m_motor = kExtruderBase + 4;
	}
	void CmdExtruder5(int anArg) {
		m_motor = kExtruderBase + 5;
	}
	void CmdExtruder6(int anArg) {
		m_motor = kExtruderBase + 6;
	}
	void CmdExtruder7(int anArg) {
		m_motor = kExtruderBase + 7;
	}
	void CmdG(int anArg) {
		Text.Error("g not suported.");
	}
	void CmdGearingDenominator(int anArg) {
		m_motors[m_motor].gearingDenominator = m_tos;
	}
	void CmdGearingNumerator(int anArg) {
		m_motors[m_motor].gearingNumerator = m_tos;
	}
	void CmdGearingP(int anArg) {
		PrinterMotor currentMotor = m_motors[m_motor];
		Text.Log(string.Format("Motor {0}'s gearing is {1}:{2}", m_motor, 
			currentMotor.gearingNumerator, currentMotor.gearingDenominator));
	}
	void CmdGlobalStepsP(int anArg) {
		Text.Error("global-steps? not implemented.");
	}
	void CmdGlobalTargetP(int anArg) {
		Text.Error("global-target? not implemented.");
	}
	void CmdGoto(int anArg) {
		while (m_globalSteps < (long)m_tos) {
			StepMotors();
		}
	}
	void CmdHeat(int anArg) {
		Text.Log(@"Heating {0} to {1}C.", m_heater, m_tos);
	}
	void CmdHeater(int anArg) {
		m_heater = Mathf.Clamp(m_tos, 0, 1);
	}
	void CmdHorizTrack(int anArg) {
		m_motor = m_simulation.horizTrack.id;
	}
	void CmdInches(int anArg) {
		m_tos *= 25400;
	}
	void CmdLeftTrack(int anArg) {
		m_motor = m_simulation.vertTrack[0].id;	
	}
	void CmdMm(int anArg) {
		m_tos *= 1000;
	}
	void CmdMotor(int anArg) {
		m_motor = m_tos;
	}
	void CmdMotorP(int anArg) {
		Text.Log("Motor {0} selected.", m_motor);
	}
	void CmdMove(int anArg) {
		Text.Error("move not implemented.");
	}
	void CmdOff(int anArg) {
		m_powerState &= ~m_tos;	
	}
	void CmdOn(int anArg) {
		m_powerState |= m_tos;
	}
	void CmdPing(int anArg) {
		m_readLine = "pong";
	}
	void CmdPlatform(int anArg) {
		m_motor = m_simulation.platform.id;
	}
	void CmdPressedP(int anArg) {
		// Always pressed.
		m_readLine = "T";
	}
	void CmdReboot(int anArg) {
		InitializePrinter();
	}
	void CmdResetSteps(int anArg) {
		m_globalSteps = 0L;
	}
	void CmdRightTrack(int anArg) {
		m_motor = m_simulation.vertTrack[1].id;	
	}
	void CmdSeek(int anArg) {
		GanglionEdge edge = (GanglionEdge)m_tos;
		Vector3 pos = m_trans.localPosition;
		
		switch (edge) {
			case GanglionEdge.Bottom:
				m_simulation.vertTrack[0].position 
					= m_simulation.vertTrack[1].position 
					= pos.y
					= VoxelBlob.kVoxelSizeInMm * VoxelBlob.kTestSize;
				break;
			case GanglionEdge.Top:
				m_simulation.vertTrack[0].position 
					= m_simulation.vertTrack[1].position
					= pos.y
					= 0;
				break;
			case GanglionEdge.Left:
				m_simulation.horizTrack.position = 0;
				pos.x = 0;
				break;
			case GanglionEdge.Right:
				m_simulation.horizTrack.position = pos.x = m_simulation.platformRadiusInMm * 2;
				break;
			default:
				Text.Error("Unknown edge: {0}", edge);
				break;
		}
		m_trans.localPosition = pos;
	}
	void CmdSleep(int anArg) {
		Text.Log("Sleeping printer.");
	}
	void CmdStackP(int anArg) {
		Text.Log("TOS: {0}", m_tos);
	}
	void CmdStep(int anArg) {
		int numSteps = m_tos;

		while (numSteps-- > 0) StepMotors();
	}
	void CmdSteps(int anArg) {
		m_motorSteps[m_motor] = m_tos;
	}
	void CmdStepsP(int anArg) {
		Text.Log("Motor {0} queued {1} step{2}.",
			m_motor, m_motorSteps[m_motor], Text.S(m_motorSteps[m_motor]));
	}
	void CmdStepRate(int anArg) {
		m_motors[m_motor].stepRate = m_tos;
		m_motors[m_motor].stepCounter = 1;
		m_motorSteps[m_motor] = 0;
	}
	void CmdStepRateStep(int anArg) {
		CmdStepRate(anArg);
		CmdStep(anArg);
	}
	void CmdStepSize(int anArg) {
		m_motors[m_motor].stepSize = kStepConversion[m_tos];
	}
	void CmdStepSizeP(int anArg) {
		Text.Log("Motor {0}'s step size is {1}", m_motor,
			m_motors[m_motor].stepSize);
	}
	void CmdStop(int anArg) {
		m_motors[m_motor].stepRate = 0;
		m_motors[m_motor].stepCounter = 1;
		m_motorSteps[m_motor] = 0;
	}
	void CmdTemperatureP(int anArg) {
		Text.Warning("No heater feedback.");
	}
	void CmdTemperatureTargetP(int anArg) {
		Text.Log("Extruder {0}'s target is {1}°C.",
			m_extruderNum, m_simulation.extruders[m_extruderNum].targetTemperatureC);
	}
	void CmdText(int anArg) {
		// NOP. Always in data mode.
	}
	void CmdTokenP(int anArg) {
		Text.Warning("Not implemented.");
	}
	void CmdUm(int anArg) {
		// Already in um.
	}
	void CmdValue(int anArg) {
		m_tos = anArg;
	}
	void CmdVersionP(int anArg) {
		Text.Log("v. simulator");
	}
	void CmdVertTrack(int anArg) {
		// Vert tracks are 2 & 3.
		m_motor = m_simulation.vertTrack[0].id + m_tos;
	}
	void CmdWake(int anArg) {
		Text.Log("Woke printer.");
	}
	void CmdWaitHeated(int anArg) {
		// NOP. Always ready.
	}
	void CmdWords(int anArg) {
		Text.Log("Defined words:");
		foreach (GanglionCommand aCmd in Execute.Keys) {
			Text.Log(aCmd.ToString());	
		}
	}
	#endregion
	
	const float kSqrThreshold = 1.0f;
	Vector4 gap = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
	void Extrude(PrinterExtruder anExtruder) {		
		List<Vector4> points = extrudedPoints[anExtruder.id];
		int prevIndex = points.Count - 1;
		
		int pressureDelta = anExtruder.stepDirection == StepDirection.Ccw ? 1 : -1;
		anExtruder.pressureRequired = Mathf.Clamp(anExtruder.pressureRequired - pressureDelta, 
			0, TickProfile.kPressureSteps);
		
		if (onlyCollectExtruded && anExtruder.pressureRequired > 0) {
			if (prevIndex >= 0 && points[prevIndex] != gap) {
				points.Add(gap);	
			}
			return;
		}
		
		float relativePosition = -(m_simulation.platformRadiusInMm - (float)m_simulation.horizTrack.position);
		float distFromCenter = anExtruder.DistanceFromPlatCenterInMm(relativePosition);
		float angleInRad = m_simulation.platform.rotationInDegrees * Mathf.Deg2Rad;
		
		float colorIndex = ((anExtruder.id - kExtruderBase) * 2) 
			+ ((anExtruder.stepDirection == StepDirection.Ccw) ? 0 : 1);
		
		Vector4 point = new Vector4(distFromCenter * Mathf.Cos(angleInRad) + m_simulation.platformRadiusInMm,
						m_trans.position.y,
						distFromCenter * Mathf.Sin(angleInRad),
						colorIndex);
		
		/*
		if (points.Count > 1
			&& (points[prevIndex].w == point.w)
			&& ((Vector3)(point - points[prevIndex])).sqrMagnitude < kSqrThreshold) return;
		*/
		extrudedPoints[anExtruder.id].Add(point);
	}

	List<Vector4> movementWithoutExtrusion;
	void StepMotors() {
		//Text.Log(string.Format("Steps remaining: {0}, {1}", m_motorSteps[0], m_motorSteps[1]));
		for (int aMotorId = 0; aMotorId < m_motors.Length; ++aMotorId) {
			PrinterMotor aMotor = m_motors[aMotorId];
			if (aMotor.stepRate == 0) continue;
			
			--aMotor.stepCounter;
			if (aMotor.stepCounter == 0) {
				aMotor.stepCounter = aMotor.stepRate;
				m_motorSteps[aMotorId] = 1;
			}
		}
		bool hasMoved = false;
		for (int aMotorId = 0; aMotorId < kExtruderBase; ++aMotorId) {
			if (m_motorSteps[aMotorId] > 0) {
				m_motors[aMotorId].Step();
				--m_motorSteps[aMotorId];
				hasMoved = true;
			}
		}
		for (int aMotorId = kExtruderBase; aMotorId < m_motorSteps.Length; ++aMotorId) {
			if (m_motorSteps[aMotorId] > 0) {
				m_motors[aMotorId].Step();
				--m_motorSteps[aMotorId];
				Extrude((PrinterExtruder)m_motors[aMotorId]);
				hasMoved = false;
			}
		}

		if (hasMoved && !onlyCollectExtruded) {
			PrinterExtruder anExtruder = (PrinterExtruder)m_motors[4];
			float relativePosition = m_simulation.platformRadiusInMm -(float) m_simulation.horizTrack.position;
			float distFromCenter = anExtruder.DistanceFromPlatCenterInMm(relativePosition);
			float angleInRad = m_simulation.platform.rotationInDegrees * Mathf.Deg2Rad;

			Vector4 point = new Vector4(distFromCenter * Mathf.Cos(angleInRad) + m_simulation.platformRadiusInMm,
			                            -m_trans.position.y,
			                            distFromCenter * Mathf.Sin(angleInRad),
			                            4);
			movementWithoutExtrusion.Add(point);
		}

		m_globalSteps = (m_globalSteps + 1) & 0xFFFFFFFF;
		
		Vector3 platformPosition = new Vector3(-(float)m_simulation.horizTrack.position,
		                                       (float)m_simulation.vertTrack[0].position, 0);

		m_trans.position = platformPosition;
		m_trans.rotation = Quaternion.Euler(0, m_simulation.platform.rotationInDegrees, 0);
	}

	/// <summary>
	/// Prepare to send packets.
	/// </summary>
	public void BeginSendingPackets() { }
	
	/// <summary>
	/// Finished sending packets for now.
	/// </summary>
	public void EndSendingPackets() { }

	/// <summary>
	/// Returns the number of packets still queued.
	/// </summary>
	/// <value>The number of unsent packets.</value>
	public int packetsRemaining { get { return 0; } }

	/// <summary>
	/// Returns the last temperature of the
	/// requested heater.
	/// </summary>
	/// <returns>The temperature of heater #index.</returns>
	/// <param name="index">The heater index, 0-based.</param>
	public int HeaterTemp(int index) { return 0; }
	
	/// <summary>
	/// Returns the bytes available to receive.
	/// </summary>
	/// <value>The rx bytes available.</value>
	public int rxBytesAvailable { get { return 0; } }
	
	/// <summary>
	/// Returns the number of motor queue slots open.
	/// </summary>
	/// <value>The motor queue slots available.</value>
	public int motorQueueAvailable { get { return 0; } }
	
	
	/// <summary>
	/// Returns whether or not the consumer is seeking.
	/// </summary>
	/// <value><c>true</c> if is seeking; otherwise, <c>false</c>.</value>
	public bool isSeeking { get { return false; } }

	/// <summary>
	/// Gets a value indicating whether this <see cref="ICommandConsumer"/> provides feedback.
	/// </summary>
	/// <value>
	/// <c>true</c> if provides feedback; otherwise, <c>false</c>.
	/// </value>
	public bool providesFeedback { get { return false; } }
	
	/// <summary>
	/// Prepares the next line from the consumer.
	/// </summary>
	/// <returns>
	/// The line.
	/// </returns>
	public IEnumerator NextLine() { yield break; }
	
	/// <summary>
	/// Returns the read line.
	/// </summary>
	/// <value>
	/// The read line.
	/// </value>
	public string readLine { get { return m_readLine; } }
	string m_readLine = "";
	
	/// <summary>
	/// Queues a packet to send.
	/// </summary>
	/// <param name='aCommand'>
	/// A command.
	/// </param>
	public void SendPacket(GanglionCommand aCommand) {
		SendPacket(aCommand, 0);	
	}
	
	/// <summary>
	/// Queues a packet to send.
	/// </summary>
	/// <param name='aCommand'>
	/// A command.
	/// </param>
	/// <param name='anArg'>
	/// An argument.
	/// </param>
	public void SendPacket(GanglionCommand aCommand, int anArg) {
		try {
			Execute[aCommand](anArg);
		}
		catch {
			Text.Error("Unknown token: {0} with arg {1}.", aCommand, anArg);
		}
	}
	
	/// <summary>
	/// Queues a packet to send.
	/// </summary>
	/// <param name="datum">
	/// A pre-created packet of command, arg, and callback.
	/// </param>
	public void SendPacket(TxPacket datum) {
		SendPacket(datum.aCmd, datum.anArgument);
	}

	public void SendComment(string format, params object[] args) {
		// Nop.
	}

	public void ClearRxBuffer() {

	}

	public void ClearTxBuffer() {
		Text.Error("Not implemented.");
	}

	void OnDrawGizmosSelected() {
		//Debug.Log(m_simulation + "    " + extrudedPoints.Count);
		if (m_simulation == null || extrudedPoints == null) return;
		
		foreach (PrinterExtruder anExtruder in m_simulation.extruders) {
			Gizmos.color = colorTable[(anExtruder.id - 4) * 2];
			Vector3 p = new Vector3(m_simulation.platformRadiusInMm + anExtruder.ringNumber * m_simulation.nozzleWidthInMm,
				1.0f, 0.0f);
			Gizmos.DrawSphere(p, 5.0f);
		}
		
		foreach (KeyValuePair<int, List<Vector4>> aPair in extrudedPoints) {
			if (aPair.Value == null || aPair.Key >= isEnabled.Length || !isEnabled[aPair.Key]) continue;
			List<Vector4> points = aPair.Value;
			
			//Text.Log(string.Format("Extruder {0}: {1} points", aPair.Key, points.Count));

			for (int aVertex = 1; aVertex < points.Count; ++aVertex) {
				Gizmos.color = colorTable[(int)points[aVertex].w];
				Vector4 p0 = points[aVertex - 1];
				Vector4 p1 = points[aVertex];
				
				if (float.IsNaN(p0.x) || float.IsNaN(p1.x)) continue;
				
				Gizmos.DrawLine((Vector3)p0, (Vector3)p1);
			}
		} 
		for(int i = 1; i < movementWithoutExtrusion.Count; i++) {
			Gizmos.color = colorTable[(int)movementWithoutExtrusion[i].w];
			Gizmos.DrawLine((Vector3)movementWithoutExtrusion[i-1], (Vector3)movementWithoutExtrusion[i]);
		}
	}
#endif
}