using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum Change { 
	Queue,
	Execute
};

/// <summary>
/// Handles all printer-level communication over ICommandConsumers.
/// </summary>
public class PrinterController : MonoBehaviour {
	#if !UNITY_IOS && !UNITY_ANDROID
	#region Constants 
	#region Event constants
	/// <summary>
	/// Event to update the preparation progress.
	/// </summary>
	public const string kOnInitializingProgress = "OnInitializingProgress";

	/// <summary>
	/// Signals the plastic has been cleared away.
	/// </summary>
	public const string kOnReadyToPrint = "OnReadyToPrint";

	/// <summary>
	/// Event to update printing progress.
	/// </summary>
	public const string kOnPrintingProgress = "OnPrintingProgress";

	public const string kOnPrinterStartingPrint = "OnPrinterStartingPrint";
	#endregion

	/// <summary>
	/// Offset added to floats to be rounded…helps fix some odd
	/// choices by Unity's Mathf.Round().
	/// </summary>
	const float kRoundingCorrection = .00001f;

	/// <summary>
	/// The index of our general support material.
	/// </summary>
	public const int kSupportMaterial = 1;

	/// <summary>
	/// How much do we need to scale the step-rate for
	/// immediate mode?
	/// </summary>
	public const float kImmediateModeScale = 3.0f;

	public const float kImmediateVerticalScale = 3.0f;

	public const float kImmediateExtruderScale = 110.0f;

	public const int   kPrimingCap = 30;
	#endregion

	#region Variables
	public bool isDebug = false;
	public bool useArcTest = false;
	public bool useConstantSpeedOutlining = false;

	public int numShells = 0;
	public StepSize platformOutliningStepSize = StepSize.Sixteenth;
	public GanglionPowered[] lightsForPrinting;

	/// <summary>
	/// Delegate for converting voxels to ring layers.
	/// </summary>
	delegate IEnumerator ConversionDelegate(VoxelRegion r);

	///<summary>
	/// Delegate for vertical movement at the *start* of
	/// a region.
	/// </summary>
	delegate int    SeekDelegate(RingLayer onLayer, Printer withPrinter);
	SeekDelegate    SeekRegionStart;
	/// <summary>
	/// Whether or not the last vertical tracks should be shifted to start
	/// after the platform rotation.
	/// </summary>
	bool m_shouldShiftVertical;

	/// <summary>
	/// Delegate for changing heaters at the end of the layer.
	/// </summary>
	delegate EndLayerDelegate EndLayerDelegate(Printer p);

	OutlinePathGenerator m_pather;
	List<List<CartesianSegment>> m_outlineSegments;

	/// <summary>
	/// Maintains the printer state.
	/// </summary>
	public Printer originalPrinter;

	/// <summary>
	/// The printer's state on the propeller.
	/// </summary>
	Printer m_propellerState;

	Segmenter m_segmenter;

	/// <summary>
	/// Scripts that consume the Ganglion commands.
	/// </summary>
	public MonoBehaviour[] consumerScripts;

	/// <summary>
	/// Internally used consumers (Unity work around).
	/// </summary>
	ICommandConsumer[] m_consumers;

	/// <summary>
	/// Cache of the serial controller, if any.
	/// </summary>
	SerialController m_serialController;
	public SerialController serialController {
		get {
			return m_serialController;
		}
	}

	/// <summary>
	/// Buffer of TxPackets with Ganglion commands.
	/// </summary>
	List<TxPacket> m_packets;

	/// <summary>
	/// Delegate to handle responses from the printer (e.g.,
	/// reaching temperature).
	/// </summary>
	public delegate void ResponseHandler();

	/// <summary>
	/// Arcs assigned to each motor.
	/// </summary>
	List<Arc>[] m_assignedArcs;

	/// <summary>
	/// Tick profiles assigned to each motor.
	/// </summary>
	List<TickProfile>[] m_assignedProfiles;

	/// <summary>
	/// The profiles that are currently active for
	/// a print layer.
	/// </summary>
	TickProfile[] m_activeProfiles;

	/// <summary>
	/// Indicates whether arcs have been normalized
	/// (relative to an extruder) or not.
	/// </summary>
	HashSet<int> m_areNormalizedArcs;

	/// <summary>
	/// Indicates that we've begun printing…or not.
	/// </summary>
	bool m_isPrinting;

	/// <summary>
	/// Printer for buffered commands.
	/// </summary>
	Printer m_bufferedPrinter;

	/// <summary>
	/// The global ticks used with the public buffered interface.
	/// </summary>
	int m_bufferedGlobalSteps;

	/// <summary>
	/// The number of steps each extruder should decelerate.
	/// </summary>
	int[] m_depressureStepsRemaining;

	/// <summary>
	/// Changes the step-rate of the platform for the first layer.
	/// Should be [0, 1].
	/// </summary>
	public float firstLayerRateScale = 0.5f;

	/// <summary>
	/// True when the plastic's been cleared away.
	/// </summary>
	bool m_readyToPrint = false;

	int m_packetsSent;

	Printer m_workingPrinter;

	/// <summary>
	/// The steps per layer to use for vertical movement
	/// between regions.
	/// </summary>
	int m_stepsPerLayer;
	#endregion

	#region Public interface
	public IEnumerator Initialize(Printer aPrinter, ResponseHandler OnInitialized) {
		m_stepsPerLayer = Mathf.Abs(aPrinter.vertTrack[0].StepsForMm(aPrinter.layerHeightInMm));

		WakePrinter(aPrinter);
		Transmit = TransmitPackets;
		m_lastNumberSent = int.MaxValue;

		Dispatcher<string>.Broadcast(
			PanelController.kEventShowMessage, "Lowering platform for object removal"
		);
		
		foreach (GanglionPowered gp in lightsForPrinting) {
			TurnFetOnOff(gp, true);
		}
		if (!isDebug) {
			yield return Scheduler.StartCoroutine(Seek(GanglionEdge.Bottom));
		}
		Dispatcher<Panel>.Broadcast(PanelController.kEventClosePanel, Panel.Message);

		m_readyToPrint = false;
		/// Make sure the user has checked the platform and it is empty
		Dispatcher<string, PanelController.Handler, PanelController.Handler>.Broadcast(
			PanelController.kEventConfirm, 
			"Remove all objects from the platform",
			OnReadyToPrint, 
			delegate { CancelBeforePrinting(); });
		while (!m_readyToPrint) yield return null;

		Dispatcher<string, string, PanelController.Handler, PanelController.Handler>.Broadcast(
			PanelController.kEventShowProgress,
			PrinterController.kOnInitializingProgress, "Centering platform and heating printheads...", 
			OnReadyToPrint,
			delegate { CancelBeforePrinting(); }
		);
		m_readyToPrint = false;

		// Create a new segmenter for outline processing
		m_segmenter = new Segmenter();

		// Start heating up and calibrate top
		int heaters = aPrinter.extruders.Length / aPrinter.motorsPerHeaterBlock;
		if (!isDebug) {
			// Start heating extruders and calibrating.
			for (int i = 1; i <= heaters; ++i) {
				Heat(i, aPrinter, true);
			}

			yield return Scheduler.StartCoroutine(Seek(GanglionEdge.Right), this);
			yield return Scheduler.StartCoroutine(Seek(GanglionEdge.Top), this);
			Text.Log("Done with initial top calibration");
		}
		
		Dispatcher<float>.Broadcast(kOnInitializingProgress, 0.10f);

		// Done calibrating; initialize motor state.
		InitializeMotors(originalPrinter);
		BeginMotorChanges(aPrinter);
		MoveVerticallyBy(60.0f, Change.Execute);
		EndMotorChanges();

		if (!isDebug) {
			yield return Scheduler.StartCoroutine(Center(aPrinter), this);
			Dispatcher<float>.Broadcast(kOnInitializingProgress, 0.30f);

			for (int i = 1; i <= heaters; ++i) {
				yield return Scheduler.StartCoroutine(WaitForHeaterBlock(i, aPrinter), this);
			}
		}
		Text.Log("Heaters at temperature.");

		// Extrude some plastic to make sure we have the printheads ready
		BeginMotorChanges(aPrinter);
		foreach (PrinterExtruder anExtruder in aPrinter.extruders) {
			Extrude(anExtruder.id, 80.0f, Change.Queue);
			anExtruder.pressureRequired = TickProfile.kPressureSteps;
		}
		EndMotorChanges();

		// Wait until the printer is done moving
		if (!isDebug) {
			yield return Scheduler.StartCoroutine(WaitUntilDoneMoving(), this);
		}
		Text.Log("Done waiting for initial extrusion and movement");
		Dispatcher<float>.Broadcast(kOnInitializingProgress, 1.0f);

		m_readyToPrint = false;
		Dispatcher<string, PanelController.Handler, PanelController.Handler>.Broadcast(
			PanelController.kEventConfirm, 
			"After removing plastic from the platform, click OK.",
			OnReadyToPrint, 
			delegate { CancelBeforePrinting(); });
		while (!m_readyToPrint) yield return null;
		Dispatcher.Broadcast(PrinterController.kOnPrinterStartingPrint);

		aPrinter.platform.ResetRotation(); // We're at the origin after initializing.
		Text.Log("Done waiting for user input during initialization");

		if (!isDebug) {
			yield return Scheduler.StartCoroutine(Seek(GanglionEdge.Top), this);
			Text.Log("Done seeking top during initialization");
		}

		Transmit = IgnorePackets;
	}

	/// <summary>
	/// Initializes the motors for the original printer.
	/// </summary>
	public void InitializeMotors() {
		InitializeMotors(originalPrinter);
	}

	/// <summary>
	/// Initializes the motors for the provided printer.
	/// </summary>
	/// <param name='forPrinter'>
	/// For printer.
	/// </param>
	public void InitializeMotors(Printer forPrinter) {
		m_propellerState = forPrinter.Clone();

		for (int i = 0; i < forPrinter.numberOfMotors; ++i) {
			Initialize(forPrinter.GetMotor(i), forPrinter);
		}
	}

	/// <summary>
	/// Initialize the specified motor from the provided printer.
	/// </summary>
	/// <param name='aMotor'>
	/// A motor.
	/// </param>
	/// <param name='forPrinter'>
	/// For printer.
	/// </param>
	public void Initialize(PrinterMotor aMotor, Printer forPrinter) {
		GanglionCommand motorCommand = forPrinter.CommandFor(aMotor.id);
		GanglionCommand directionCommand = aMotor.stepDirection == StepDirection.Ccw ?
			GanglionCommand.CounterClockwise : GanglionCommand.Clockwise;
		int size = forPrinter.GanglionStepSizeFor(aMotor.stepSize);

		foreach (ICommandConsumer aConsumer in m_consumers) {
			aConsumer.BeginSendingPackets();
			aConsumer.SendPacket(motorCommand);
			aConsumer.SendPacket(directionCommand);
			aConsumer.SendPacket(GanglionCommand.Value, size);
			aConsumer.SendPacket(GanglionCommand.StepSize);
			aConsumer.SendPacket(GanglionCommand.Value, 0);
			aConsumer.SendPacket(GanglionCommand.StepRate);
			aConsumer.EndSendingPackets();
		}
		m_packetsSent += 6;

		PrinterMotor ganglionMotor = m_propellerState.GetMotor(aMotor.id);
		ganglionMotor.stepDirection = aMotor.stepDirection;
		ganglionMotor.stepSize = aMotor.stepSize;
		ganglionMotor.stepRate = 0;
		m_propellerState.currentMotorId = int.MinValue; // aMotor.id;

		forPrinter.currentMotorId = int.MinValue; // aMotor.id;
	}

	/// <summary>
	/// Called before making motor changes.
	/// </summary>
	public void BeginMotorChanges(Printer withPrinter) {
		m_bufferedPrinter = withPrinter;
		m_bufferedGlobalSteps = 0;
		
		foreach (List<Arc> arcs in m_assignedArcs) {
			arcs.Clear();
		}
	}
	
	/// <summary>
	/// Called after making motor changes.
	/// </summary>
	public void EndMotorChanges() {
		ConvertArcsToProfiles(0, 0, m_bufferedPrinter);
		// NOTE: We want to send the propeller state since we've been
		// updating withPrinter's horizontal & rotational states.
		// PropellerState should only be passed to ConvertToCommands to
		// ensure we clearly update the state on the propeller.
		ConvertToCommands(m_assignedProfiles, m_packets, m_bufferedPrinter);
		TransmitPackets(m_packets);
		
		for (int i = m_bufferedPrinter.numberOfBaseMotors; i < m_bufferedPrinter.numberOfMotors;
		     ++i)
		{
			if (m_depressureStepsRemaining[i] > 0) {
				CompleteDepressurization(m_bufferedPrinter);
				break;
			}
		}
		m_bufferedPrinter = null;
	}

	#region Motor changes
	// These should all be called between BeginMotorChanges()
	// and EndMotorChanges().
	delegate void ChangeHandler(Arc anArc);

	Dictionary<Change, ChangeHandler> CommitChange;

	void QueueChange(Arc anArc) {
		m_assignedArcs[anArc.motor.id].Add(anArc);
	}

	void ExecuteChange(Arc anArc) {
		QueueChange(anArc);
		m_bufferedGlobalSteps = anArc.endStep;
	}

	public void RotateBySteps(int aMotorId, int bySteps, StepDirection inDirection, Change byMeans) {
		// Rotate based on the steps provided.
		PrinterMotor aMotor = m_bufferedPrinter.GetMotor(aMotorId);
		Arc rotationArc = new Arc(aMotor, m_bufferedGlobalSteps);
		rotationArc.endStep = m_bufferedGlobalSteps + Mathf.Abs(bySteps);
		rotationArc.direction = inDirection; //(bySteps > 0 ? StepDirection.Ccw : StepDirection.Cw);
		CommitChange[byMeans](rotationArc);
	}

	public void ChangeBufferedExtruderScale(int extruderMotorId, float byAmount) {
		PrinterExtruder anExtruder = (PrinterExtruder)m_bufferedPrinter.GetMotor(extruderMotorId);
		anExtruder.ScaleRateConstant(byAmount);
	}

	public void Extrude(int fromMotor, float mmOfMaterial, Change byMeans) {
		// Pressurize, maintain speed, depressurize.
		PrinterExtruder anExtruder = (PrinterExtruder)m_bufferedPrinter.GetMotor(fromMotor);
		int numberOfSteps = anExtruder.StepsForMm(mmOfMaterial);
		RotateBySteps(fromMotor, numberOfSteps, 
		              mmOfMaterial >= 0 ? StepDirection.Ccw : StepDirection.Cw, 
		              byMeans);
	}

	public void MoveVerticallyBy(float mm, Change byMeans) {
		PrinterMotorLinear aMotor = m_bufferedPrinter.vertTrack[0];

		// Note that we need the *opposite* rotation from standard motors
		// (i.e., > 0 is Cw, < 0 is Ccw), so invert the sign before
		// passing it along.
		int numberOfSteps = Mathf.Abs(aMotor.StepsForMm(mm));
		StepDirection inDirection = (mm > 0) ? StepDirection.Cw : StepDirection.Ccw;

		// Since we need to update two motors, queue the first
		// and then do whatever the user wanted.
		RotateBySteps(m_bufferedPrinter.vertTrack[0].id, numberOfSteps, inDirection, Change.Queue);
		RotateBySteps(m_bufferedPrinter.vertTrack[1].id, numberOfSteps, inDirection, byMeans);
	}

	public void MoveHorizontallyBy(float mm, Change byMeans) {
		PrinterMotorLinear aMotor = m_bufferedPrinter.horizTrack;

		int numberOfSteps = Mathf.Abs(aMotor.StepsForMm(mm));
		StepDirection inDirection = (mm > 0) ? StepDirection.Ccw : StepDirection.Cw;

		Text.Log(@"Number of steps: {0}.", numberOfSteps);
		RotateBySteps(aMotor.id, numberOfSteps, inDirection, byMeans);
	}

	public IEnumerator WaitUntilDoneMoving() {
		// Ensure that we give the RX thread enough time
		// to update.
		yield return new WaitSeconds(1.1f);

		Text.Log("Waiting until done moving...");
		foreach (ICommandConsumer aConsumer in m_consumers) {
			if (!aConsumer.providesFeedback) continue;

			// Wait until we've sent everything; could still have
			// a seek or something that blocks.
			while (aConsumer.packetsRemaining > 0) {
				yield return new WaitSeconds(1.0f);
			}

			while (aConsumer.isSeeking) {
				yield return new WaitSeconds(1.0f);
			}

			while (aConsumer.rxBytesAvailable < Printer.kRxBufferSize) {
				yield return new WaitSeconds(1.0f);
			}

			while (aConsumer.motorQueueAvailable < Printer.kMotorQueueSize) {
				yield return new WaitSeconds(1.0f);
			}
		}
		Text.Log("All data has been sent and processed.");
	}

	#endregion

	#region Unbuffered commands
	// These are all handled immediately; they can be called at
	// any time.

	#region Calibration
	public IEnumerator CalibrateBottom(Printer forPrinter) {
		yield return Scheduler.StartCoroutine(Calibrate(forPrinter, GanglionEdge.Bottom), this);
	}

	public IEnumerator CalibrateTop(Printer forPrinter) {
		yield return Scheduler.StartCoroutine(Calibrate(forPrinter, GanglionEdge.Top), this);
	}

	IEnumerator Calibrate(Printer aPrinter, GanglionEdge anEdge) {
		Text.Log("Calibrating...");
		foreach (ICommandConsumer aConsumer in m_consumers) {
			aConsumer.BeginSendingPackets();
			aConsumer.SendPacket(GanglionCommand.Value, (int)anEdge);
			aConsumer.SendPacket(GanglionCommand.Calibrate);
			aConsumer.EndSendingPackets();

			yield return Scheduler.StartCoroutine(WaitUntilDoneMoving());
		}

		aPrinter.horizTrack.position = aPrinter.platformRadiusInMm;
		m_packetsSent += 2;

		Text.Log("Done calibrating.");
	}
	#endregion

	/// <summary>
	/// Move the platform to the specified edge.
	/// </summary>
	/// <param name='anEdge'>
	/// An edge to seek.
	/// </param>
	public IEnumerator Seek(GanglionEdge anEdge) {
		Text.Log("Seeking {0}", anEdge);

		byte theEdge = (byte)anEdge;
		foreach (ICommandConsumer aConsumer in m_consumers) {
			aConsumer.BeginSendingPackets();
			aConsumer.SendPacket(GanglionCommand.Value, theEdge);
			aConsumer.SendPacket(GanglionCommand.Seek);
			aConsumer.EndSendingPackets();
			yield return Scheduler.StartCoroutine(WaitUntilDoneMoving());
		}

		m_propellerState.currentMotorId = int.MinValue;
		if(anEdge == GanglionEdge.Left || anEdge == GanglionEdge.Right) {
			m_propellerState.horizTrack.stepSize = StepSize.Quarter;
		}
		m_packetsSent += 2;

		for (int i = 0; i < m_propellerState.numberOfBaseMotors; ++i) {
			PrinterMotor aMotor = m_propellerState.GetBaseMotor(i);
			aMotor.stepDirection = StepDirection.Unknown;
		}
		Text.Log("Seek complete.");
	}

	/// <summary>
	/// Centers the platform
	/// </summary>
	public IEnumerator Center(Printer forPrinter) {
		Text.Log("Starting centering...");
		SendUnbufferedCommand(GanglionCommand.Center);
		yield return Scheduler.StartCoroutine(WaitUntilDoneMoving());

		m_propellerState.currentMotorId = int.MinValue;
		m_propellerState.horizTrack.stepSize = StepSize.Quarter;

		for (int i = 0; i < m_propellerState.numberOfBaseMotors; ++i) {
			PrinterMotor aMotor = m_propellerState.GetBaseMotor(i);
			aMotor.stepDirection = StepDirection.Unknown;
		}

		forPrinter.horizTrack.position = forPrinter.platformRadiusInMm;
		Text.Log("Centered.");
	}

	public void WakePrinter(Printer withPrinter) {
		SendUnbufferedCommand(GanglionCommand.Wake);
		withPrinter.isAwake = true;
	}

	public void SleepPrinter(Printer withPrinter) {
		SendUnbufferedCommand(GanglionCommand.Sleep);
		withPrinter.isAwake = false;
	}

	void SendUnbufferedCommand(GanglionCommand aCommand) {
		foreach (ICommandConsumer aConsumer in m_consumers) {
			aConsumer.BeginSendingPackets();
			aConsumer.SendPacket(aCommand);
			aConsumer.EndSendingPackets();
		}
		m_packetsSent++;
	}
	
	public void TurnFetOnOff(GanglionPowered aFet, bool turnOn) {
		int fetMask = 1 << (int)aFet;
		foreach (ICommandConsumer aConsumer in m_consumers) {
			aConsumer.BeginSendingPackets();
			aConsumer.SendPacket(GanglionCommand.Value, fetMask);
			aConsumer.SendPacket(turnOn ? GanglionCommand.On : GanglionCommand.Off);
			aConsumer.EndSendingPackets();
		}
		m_packetsSent += 2;
	}

	/// <summary>
	/// Turns the backlight off.
	/// </summary>
	public void TurnBacklightOff() {
		int backlightMask = 0;
		backlightMask += 1 << (int)GanglionPowered.pScannerBackLight;// .BacklightPatternPin;
		backlightMask += 1 << (int)GanglionPowered.pScannerMidLight;// .BacklightScanningPin;
		backlightMask += 1 << (int)GanglionPowered.pScannerFrontLight;// Fet4Pin;
		backlightMask += 1 << (int)GanglionPowered.pPrintheadLight;// Fet7Pin;
		foreach (ICommandConsumer aConsumer in m_consumers) {
			aConsumer.BeginSendingPackets();
			aConsumer.SendPacket(GanglionCommand.Value, backlightMask);
			aConsumer.SendPacket(GanglionCommand.Off);
			aConsumer.EndSendingPackets();
		}
		m_packetsSent += 2;
	}

	#region Heating & Temperatures
	public int GetTemperatureOf(int aHeaterBlock) {
		int temperature = int.MaxValue;
		for (int i = 0; i < m_consumers.Length; i++) {
			ICommandConsumer aConsumer = m_consumers[i];
			if (!aConsumer.providesFeedback) continue;

			temperature = Mathf.Min(temperature, aConsumer.HeaterTemp(aHeaterBlock));
		}
		return temperature;
	}

	/// <summary>
	/// Heat the specified heater block using the
	/// provided printer state.
	/// </summary>
	/// <param name='aHeaterBlock'>
	/// The heater block to heat.
	/// </param>
	/// <param name='forPrinter'>
	/// The printer state we're using.
	/// </param>
	public void Heat(int aHeaterBlock, Printer forPrinter, bool isFirstLayer) {
		Contract.Assert(aHeaterBlock >= 0, @"Requested negative heater block {0}.", aHeaterBlock);

		int motorNumber = aHeaterBlock * forPrinter.motorsPerHeaterBlock;
		PrinterExtruder extruder = (PrinterExtruder)forPrinter.GetMotor(motorNumber);

		SetTargetTemperature(aHeaterBlock, isFirstLayer
			? extruder.firstLayerTemperature : extruder.targetTemperatureC, forPrinter);
	}

	/// <summary>
	/// Sets the target temperature of the provided motor.
	/// </summary>
	/// <param name='aHeaterBlock'>
	/// The heater block id (0 or 1).
	/// </param>
	/// <param name='toTemperature'>
	/// The target temperature.
	/// </param>
	/// <param name='forPrinter'>
	/// The printer we're manipulating.
	/// </param>
	public void SetTargetTemperature(int aHeaterBlock, int toTemperature, Printer forPrinter) {
		foreach (ICommandConsumer aConsumer in m_consumers) {
			aConsumer.BeginSendingPackets();
			aConsumer.SendPacket(GanglionCommand.Value, aHeaterBlock);
			aConsumer.SendPacket(GanglionCommand.Heater);
			aConsumer.SendPacket(GanglionCommand.Value, toTemperature);
			aConsumer.SendPacket(GanglionCommand.Heat);
			aConsumer.EndSendingPackets();
		}
		m_packetsSent += 4;
	}

	public IEnumerator WaitForHeaterBlock(int aHeaterBlock, Printer forPrinter) {
		for (int i = 0; i < m_consumers.Length; i++) {
			ICommandConsumer aConsumer = m_consumers[i];
			if (!aConsumer.providesFeedback) {
				continue;
			}
			// TODO(kevin): Remove heating values from the extruders
			// and make heater blocks on printers.
			int motorNumber = aHeaterBlock * forPrinter.motorsPerHeaterBlock;
			PrinterExtruder motor = (PrinterExtruder)forPrinter.GetMotor(motorNumber);

			while (aConsumer.HeaterTemp(aHeaterBlock) < motor.targetTemperatureC) {
				yield return new WaitSeconds(1.0f);
			}
		}
	}
	#endregion
	#endregion

	/// <summary>
	/// Schedules a voxel blob for printing.
	/// </summary>
	/// <param name='aBlob'>
	/// A BLOB.
	/// </param>
	public void SchedulePrint(VoxelBlob aBlob) {
		Contract.Assert(m_isPrinting == false, @"Possible to start print while printing.");
		Scheduler.StartCoroutine(Print(aBlob), this);
	}

	public void CancelBeforePrinting() {
		Text.Log("---- Cancelling ----");
		Scheduler.StopCoroutines(this);
		m_isPrinting = false;
	}

	public void CancelPrinting() {
		Text.Log("---- Cancelling ----");
		Scheduler.StopCoroutines(this);
		if (m_serialController != null) {
			//m_serialController.ClearRxBuffer();
			m_serialController.ClearTxBuffer();
		}

		m_packets.Clear();

		// Sleep shuts down all the cogs, then wake again to complete printing.
		SendUnbufferedCommand(GanglionCommand.Sleep);
		SendUnbufferedCommand(GanglionCommand.Wake);
		Scheduler.StartCoroutine(CompletePrintingWith(m_workingPrinter));
	}

	#endregion

	#region Initialization and cleanup
	/// <summary>
	/// Initializes our variables.
	/// </summary>
	void Awake() {
		#if SOFTWAREONLYBUILD
		this.active = false;
		gameObject.SetActive(false);
		return;
		#endif

		CommitChange = new Dictionary<Change, ChangeHandler>{
			{ Change.Queue, QueueChange },
			{ Change.Execute, ExecuteChange }
		};

		m_packets = new List<TxPacket>(1024);

		m_areNormalizedArcs = new HashSet<int>();

		m_assignedArcs     = new List<Arc>[originalPrinter.numberOfMotors];
		m_assignedProfiles = new List<TickProfile>[originalPrinter.numberOfMotors];
		m_activeProfiles   = new TickProfile[originalPrinter.numberOfMotors];

		// NOTE: This could be fewer motors, but exchanging 4 ints for
		// fewer calculations later is worthwhile.
		m_depressureStepsRemaining = new int[originalPrinter.numberOfMotors];

		for (int i = 0; i < originalPrinter.numberOfMotors; ++i) {
			m_assignedArcs[i] = new List<Arc>();
			m_assignedProfiles[i] = new List<TickProfile>();
			m_depressureStepsRemaining[i] = 0;
		}

		m_consumers = new ICommandConsumer[consumerScripts.Length];
		for (int i = 0; i < m_consumers.Length; ++i) {
			m_consumers[i] = (ICommandConsumer)consumerScripts[i];
			if (m_consumers[i] as SerialController != null) {
				m_serialController = (SerialController)m_consumers[i];
			}
		}

		Dispatcher.AddListener(kOnReadyToPrint, OnReadyToPrint);
	}

	void OnDestroy() {
		Dispatcher.RemoveListener(kOnReadyToPrint, OnReadyToPrint);
	}

	void OnReadyToPrint() {
		m_readyToPrint = true;
	}
	#endregion

	#region High-level printer control
	/// <summary>
	/// Delegate for sending or keeping packets.
	/// </summary>
	delegate void TransmitHandler(List<TxPacket> commands);

	/// <summary>
	/// Points to our transmission function.
	/// </summary>
	TransmitHandler Transmit;

	/// <summary>
	/// Turns off heaters, switches to immediate mode, and lowers the
	/// print platform.
	/// </summary>
	/// <param name='aPrinter'>
	/// A printer.
	/// </param>
	IEnumerator CompletePrintingWith(Printer aPrinter) {
#if UNITY_EDITOR
		foreach (PrinterExtruder anExtruder in aPrinter.extruders) {
			Text.Log(@"Pressure required for {0}: {1}", anExtruder.id,
				anExtruder.pressureRequired);
		}
#endif
		SetTargetTemperature(1, 0, aPrinter);

		if (!isDebug) {
			yield return Scheduler.StartCoroutine(CalibrateBottom(aPrinter));
		}
		foreach(GanglionPowered gp in lightsForPrinting) {
			TurnFetOnOff(gp, false);
		}
		yield return new WaitSeconds(0.5f);

		m_isPrinting = false;

		// Signal that we're done.
		Dispatcher<float>.Broadcast(kOnPrintingProgress, 1f);
		m_packets.Clear();
		SleepPrinter(aPrinter);
	}

	public void MoveMotorAtSteprateForTicks(Printer aPrinter, PrinterMotor aMotor,
	                                        StepSize aSize, StepDirection aDirection,
	                                        int aSteprate, int numTicks) 
	{
		TickProfile aProfile = new TickProfile(aMotor, aSize, aDirection, aSteprate, 0, numTicks);
		//Debug.Log("Created " + aProfile.ToString() + " for motor " + aMotor.id);
		foreach(List<TickProfile> listProfiles in m_assignedProfiles) listProfiles.Clear();
		m_assignedProfiles[aMotor.id].Add(aProfile);
		ConvertToCommands(m_assignedProfiles, m_packets, aPrinter);
		TransmitPackets(m_packets);
	}

	public void AddTickProfileForMotorAndSend(Printer aPrinter, PrinterMotor aMotor, TickProfile aProfile) {
		foreach(List<TickProfile> listProfiles in m_assignedProfiles) listProfiles.Clear();
		m_assignedProfiles[aMotor.id].Add(aProfile);
		ConvertToCommands(m_assignedProfiles, m_packets, aPrinter);
		TransmitPackets(m_packets);
	}


	void CompleteDepressurization(Printer forPrinter) {
		// Conclude any depressurization we need.
		for (int i = forPrinter.numberOfBaseMotors; i < forPrinter.numberOfMotors; ++i) {
			PrinterExtruder theExtruder = (PrinterExtruder)forPrinter.GetMotor(i);

			int depressureOverSteps = Mathf.Min(m_depressureStepsRemaining[i],
				Mathf.Max(TickProfile.kPressureSteps - theExtruder.pressureRequired, 0));

			TickProfile.CompleteDepressurization(theExtruder,
				depressureOverSteps,
				Mathf.RoundToInt(forPrinter.platform.stepRate),// * (forPrinter.isSynchronizedMode ? 1 : kImmediateExtruderScale)),
				m_assignedProfiles[i]);
			m_depressureStepsRemaining[i] = 0;
		}
		ConvertToCommands(m_assignedProfiles, m_packets, forPrinter);
		if (Transmit != null) {
			Transmit(m_packets);
		}
		else {
			TransmitPackets(m_packets);
		}
	}

	void ClearExtrudedSteps(Printer forPrinter) {
		foreach (PrinterExtruder anExtruder in forPrinter.extruders) {
			anExtruder.stepsExtruded = 0;
		}
	}

	void ReportExtrudedSteps(Printer forPrinter, int onLayer) {
		Text.Log(@"~~~~~~~~~~~~~~~~~~~~~~~~~~ On layer {0},", onLayer);
		foreach (PrinterExtruder anExtruder in forPrinter.extruders) {
			Text.Log(@"Motor {0} extruded {1} step{2}.", anExtruder.id,
				anExtruder.stepsExtruded, Text.S(anExtruder.stepsExtruded));
		}
	}

	int InitialRegionSeek(RingLayer onLayer, Printer withPrinter) {
		SeekRegionStart = SubsequentRegionSeek;
		if (onLayer.ringCount > 0) {
			return MoveToStartingRing(onLayer, withPrinter, 0);
		}
		m_shouldShiftVertical = false;

		return 0;
	}

	int SubsequentRegionSeek(RingLayer onLayer, Printer withPrinter) {
		int currentStep = 0;
		if (onLayer.ringCount > 0) {
			MoveOneLayer(withPrinter, 0, StepDirection.Cw);
			currentStep = MoveToStartingRing(onLayer, withPrinter, currentStep);

			// NOTE(kevin): This will be shifted later, in EqueuePlatformRotation.
			MoveOneLayer(withPrinter, 0, StepDirection.Ccw);
			m_shouldShiftVertical = true;
		}
		return currentStep;
	}

	int MoveOneLayer(Printer withPrinter, int currentStep, StepDirection direction) {
		// Move up to start the region.
		Arc v0 = new Arc(withPrinter.vertTrack[0], currentStep);
		v0.direction = direction;
		v0.endStep   = currentStep + m_stepsPerLayer;
		m_assignedArcs[2].Add(v0);
		
		Arc v1 = new Arc(withPrinter.vertTrack[1], currentStep);
		v1.direction = direction;
		v1.endStep   = currentStep + m_stepsPerLayer;
		m_assignedArcs[3].Add(v1);
		
		currentStep += m_stepsPerLayer;
		return currentStep;
	}

	/// <summary>
	/// Moves the platform to the starting ring in the provided layer.
	/// </summary>
	/// <returns>
	/// The number of steps to reach the starting ring.
	/// </returns>
	/// <param name='onLayer'>
	/// The ring layer to analyze.
	/// </param>
	/// <param name='withPrinter'>
	/// The current printer state.
	/// </param>
	int MoveToStartingRing(RingLayer onLayer, Printer withPrinter, int step) {
		withPrinter.NewLayer();
		ClearExtrudedSteps(withPrinter);
		int ringMovementAllowance = withPrinter.movableDistanceInRings - withPrinter.ringPosition;

		int kDontMoveThatDir = -100000000;

		int posDistanceToRightMostRing = int.MinValue + 1;
		int posDistanceToLeftMostRing = int.MaxValue;
		int bestPositiveSideDistance = 0;
		StepDirection posSideDir = StepDirection.Unknown;

		foreach (PrinterExtruder anExtruder in withPrinter.extruders) {
			int extruderRing = anExtruder.relativeRing;

			int innerMostRingForExtruder = onLayer.innerRingForMaterial((byte)anExtruder.materialNumber);
			if (innerMostRingForExtruder < 0) continue;

			int outerMostRingForExtruder = onLayer.outerRingForMaterial((byte)anExtruder.materialNumber);
			int positiveSideDistance     = outerMostRingForExtruder - extruderRing;

			posDistanceToRightMostRing = Mathf.Max(posDistanceToRightMostRing, positiveSideDistance);
			positiveSideDistance       = innerMostRingForExtruder - extruderRing;
			posDistanceToLeftMostRing  = Mathf.Min(posDistanceToLeftMostRing, positiveSideDistance);
		}

		if (posDistanceToLeftMostRing > ringMovementAllowance || posDistanceToRightMostRing > ringMovementAllowance) {
			bestPositiveSideDistance = kDontMoveThatDir;
		}
		else {
			if (Mathf.Abs(posDistanceToLeftMostRing) < Mathf.Abs(posDistanceToRightMostRing)) {
				bestPositiveSideDistance = posDistanceToLeftMostRing;
				posSideDir = StepDirection.Ccw;
			}
			else {
				bestPositiveSideDistance = posDistanceToRightMostRing;
				posSideDir = StepDirection.Cw;
			}
		}

		int negDistanceToRightMostRing = int.MinValue + 1;
		int negDistanceToLeftMostRing = int.MaxValue;
		int bestNegativeSideDistance = 0;
		StepDirection negSideDir = StepDirection.Unknown;

		foreach (PrinterExtruder anExtruder in withPrinter.extruders) {
			int extruderRing = anExtruder.relativeRing;

			int innerMostRingForExtruder = onLayer.innerRingForMaterial((byte)anExtruder.materialNumber);
			if (innerMostRingForExtruder < 0) continue;
			int outerMostRingForExtruder = onLayer.outerRingForMaterial((byte)anExtruder.materialNumber);

			int negativeSideDifference = -innerMostRingForExtruder - extruderRing;
			negDistanceToRightMostRing = Mathf.Max(negDistanceToRightMostRing, negativeSideDifference);
			negativeSideDifference     = -outerMostRingForExtruder - extruderRing;
			negDistanceToLeftMostRing  = Mathf.Min(negDistanceToLeftMostRing, negativeSideDifference);
		}

		if (Mathf.Abs(negDistanceToLeftMostRing) < Mathf.Abs(negDistanceToRightMostRing)) {
			bestNegativeSideDistance = negDistanceToLeftMostRing;
			negSideDir = StepDirection.Ccw;
		}
		else {
			bestNegativeSideDistance = negDistanceToRightMostRing;
			negSideDir = StepDirection.Cw;
		}

		int deltaRings = 0;
		if (Mathf.Abs(bestNegativeSideDistance) < Mathf.Abs(bestPositiveSideDistance)) {
			deltaRings = bestNegativeSideDistance;
			withPrinter.horizPrintingDirection = negSideDir;
		}
		else {
			deltaRings = bestPositiveSideDistance;
			withPrinter.horizPrintingDirection = posSideDir;
		}

		if (deltaRings != 0) {
			Contract.Assert(Mathf.Abs(deltaRings) < 1000, "Delta rings found to be " + deltaRings);
			// NOTE that if deltaRings = 0, we don't need
			// to move the horizontal track.
			Arc movementArc = withPrinter.Move(0, deltaRings);		
			withPrinter.horizTrack.stepDirection = movementArc.direction;
			withPrinter.horizTrack.Step(movementArc.length);

			Contract.Assert((deltaRings < 0 && movementArc.direction == StepDirection.Cw)
				|| (deltaRings > 0 && movementArc.direction == StepDirection.Ccw),
				@"Delta rings of {0} doesn't match movement arc direction of {1}.",
				deltaRings, movementArc.direction);

			m_assignedArcs[withPrinter.horizTrack.id].Add(movementArc);
			step = movementArc.endStep;
		}
		else {
			withPrinter.Move(0, 0);	
			//m_assignedArcs[withPrinter.horizTrack.id].Add(movementArc);
			withPrinter.horizTrack.stepDirection = withPrinter.horizPrintingDirection;
		}			
		return step;
	}

	/// <summary>
	/// Prints the specified aBlob.
	/// </summary>
	/// <param name='aBlob'>
	/// A voxel blob to print.
	/// </param>
	IEnumerator Print(VoxelBlob aBlob) {
		m_isPrinting = true;
		m_packetsSent = 0;
		Transmit = IgnorePackets;

		// Initialize the printer; note that we don't send
		// data until the print heads are heated.
		Printer workingPrinter = originalPrinter.Clone();
		m_workingPrinter = workingPrinter;
		m_propellerState = workingPrinter.Clone();

		RingLayer ringLayer = new RingLayer(workingPrinter, aBlob.size.x, VoxelBlob.kVoxelSizeInMm);
		yield return Scheduler.StartCoroutine(Initialize(workingPrinter, null), this);
		float startTime = Time.realtimeSinceStartup;

		// We're scaling the initial platform rotation to help the material stick.
		workingPrinter.platform.stepRate = Mathf.RoundToInt(firstLayerRateScale * originalPrinter.platform.stepRate);

		// NOTE(kevin): As per Nate's suggestion, we're basically going to
		// ignore the *actual* height of the first voxel layer and treat
		// it as if it were the same as every other layer.
		float voxelBlobHeight = ((aBlob.maxVoxelBounds.y != -1) ? (aBlob.maxVoxelBounds.y + 1) : aBlob.height);
		int numLayers = Mathf.CeilToInt((voxelBlobHeight * VoxelBlob.kVoxelSizeInMm) 
		                                / workingPrinter.layerHeightInMm);
		Text.Log("Number of print layers: {0}", numLayers);

		// Massage values if needed for testing.
		VoxelBlob supportedBlob;
		ConversionDelegate ConvertVoxelsToLayer;
		if (useArcTest) {
			// Zero out the blob to prevent outlines.
			supportedBlob = new VoxelBlob(aBlob.width, aBlob.height, aBlob.depth, false);
			numLayers = 5;
			ConvertVoxelsToLayer = ringLayer.CreateArcTest;
		}
		else {
			// TODO: Generate the support material.
			supportedBlob = aBlob.CompactBlob();
			ConvertVoxelsToLayer = ringLayer.ConvertVoxelLayer;
		}

		workingPrinter.NewPrint();
		EndLayerDelegate EndLayer = EndFirstLayer;
		m_outlineSegments = new List<List<CartesianSegment>>();

		// We need 100% infill on the first layer.
		int infillCache = ringLayer.infillStep;
		ringLayer.infillStep = 1;

		for (int printLayer = 0; printLayer < numLayers; ++printLayer) {
			int voxelLayer = Mathf.FloorToInt((printLayer * workingPrinter.layerHeightInMm)
			                                  / VoxelBlob.kVoxelSizeInMm);
			Text.Log("%%%%%%%%%%% Print Layer {0}, Voxel Layer {1}", printLayer, voxelLayer);

			bool foundRegion = false;
			SeekRegionStart = InitialRegionSeek;

			// Loop for each region here, passing information to outlining and infill.
			foreach (VoxelRegion r in supportedBlob.RegionEnumerator(voxelLayer, printLayer % 2 != 0)) {
				foundRegion = true;
				m_outlineSegments.Clear();

				// Get the regular outline first.
				for (int shells = 0; shells < numShells; shells++) {
					List<CartesianSegment> list = m_segmenter.GetSegments(r);
					if (list.Count < 1) {
						break;
					}
					m_outlineSegments.Add(list);
				}
#if SURFACING
				// Next, try handling the top and bottom surfaces, if any.
				foreach (VoxelRegion surface in r.SurfaceEnumerator(supportedBlob,
				                                                    voxelLayer, 
				                                                    printLayer,
				                                                    workingPrinter.layerHeightInMm,
				                                                    printLayer % 2 != 0)) 
				{
					for (;;) {
						List<CartesianSegment> list = m_segmenter.GetSegments(surface);
						if (list.Count < 1) {
							break;
						}
						m_outlineSegments.Add(list);
					}
				}
#endif

				// Convert the voxel layer to arcs on rings
				yield return Scheduler.StartCoroutine(ConvertVoxelsToLayer(r), this);
				if (ringLayer.ringCount < 1 && m_outlineSegments.Count < 1) {
					goto cleanup;
				}

				yield return Scheduler.StartCoroutine(Print(ringLayer, workingPrinter), this);
			}

			// Update the heaters/etc. after the first layer.
			EndLayer = EndLayer(workingPrinter);

			// NOTE: We're just filling the vertical motion here.
			// We'll actually take the steps (or not) when we move
			// to the next initial location. They'll start after
			// global steps is reset to zero, so they should
			// always start at zero.
			if (foundRegion) {
				// NOTE(kevin): If we didn't find a region, it means that we should be done
				// printing…and that we don't need to go to the next layer manually here.
				// This check will ensure we don't break asserts.
				workingPrinter.NextLayer(m_assignedArcs[workingPrinter.vertTrack[0].id],
				                         m_assignedArcs[workingPrinter.vertTrack[1].id], printLayer);
			}

			//Text.Log("Done with layer {0}; vert arcs: {1}, {2}.", aLayer, m_assignedArcs[2].Count, m_assignedArcs[3].Count);
			Dispatcher<float>.Broadcast(kOnPrintingProgress, ((float)printLayer / (float)numLayers)/ 4.0f);
			if (Scheduler.ShouldYield()) yield return null;

			// Restore the requested infill.
			ringLayer.infillStep = infillCache;

			yield return null;
		}
	cleanup:
		Text.Log(@"Finished processing layers..."); 
		yield return null;

		// Wait for printing to finish, then shutdown the printer––turning off heaters,
		// moving the platform to the bottom, etc.
		float consumerCount = Mathf.Max(m_consumers.Length, 1.0f);
		float consumerIndex = 1.0f;
		foreach (ICommandConsumer aConsumer in m_consumers) {
			if (!aConsumer.providesFeedback) {
				consumerIndex += 1.0f;
				continue;
			}

			int maxDataCount = aConsumer.packetsRemaining;
			int remainingDataCount = maxDataCount;
			while (remainingDataCount > 0) {
				yield return new WaitSeconds(0.5f);
				remainingDataCount = aConsumer.packetsRemaining;
				Dispatcher<float>.Broadcast(kOnPrintingProgress, 
				                            0.75f * consumerIndex / consumerCount
				                            * (1.0f - (float)remainingDataCount / (float)(maxDataCount + 1)) + 0.24f);
			}
			consumerIndex += 1.0f;
		}

		Text.Log("All data packets sent; depressurizing.");
		CompleteDepressurization(workingPrinter);
		yield return Scheduler.StartCoroutine(WaitUntilDoneMoving(), this);

		Text.Log(@"Finished printing blob..."); 
		yield return null;
		yield return Scheduler.StartCoroutine(CompletePrintingWith(workingPrinter), this);

		Dispatcher<float>.Broadcast(kOnPrintingProgress, 1.0f);
		Text.Log("######## Done printing after {0} sec; used {1} packet{2}",
		         Time.realtimeSinceStartup - startTime, m_packetsSent, Text.S(m_packetsSent));
	}

	/// <summary>
	/// Updates the heater for general layers & resets platform step rates.
	/// </summary>
	/// <returns>The first layer.</returns>
	/// <param name="p">P.</param>
	EndLayerDelegate EndFirstLayer(Printer p) {
		if (!isDebug) {
			int heaters = p.extruders.Length / p.motorsPerHeaterBlock;

			// Start heating extruders and calibrating.
			for (int i = 1; i <= heaters; ++i) {
				Heat(i, p, false);
				m_propellerState.currentMotorId = 4;
			}
		}
		Text.Log(@"{0} packet{1} pending.", m_packets.Count,
		         Text.S(m_packets.Count));
		Transmit = TransmitPackets;

		return EndGeneralLayer(p);
	}

	/// <summary>
	/// Resets platform step rates.
	/// </summary>
	/// <returns>The general layer.</returns>
	/// <param name="p">P.</param>
	EndLayerDelegate EndGeneralLayer(Printer p) {
		p.platform.stepRate = originalPrinter.platform.stepRate;
		return EndGeneralLayer;
	}
	
	/// <summary>
	/// Prints the specified layer by performing calculations on withPrinter.
	/// Note that withPrinter should *not* represent the state as on the propeller.
	/// </summary>
	/// <param name='onLayer'>
	/// The layer to print.
	/// </param>
	/// <param name='withPrinter'>
	/// The calculation state to use.
	/// </param>
	IEnumerator Print(RingLayer onLayer, Printer withPrinter) {
		// Determine the direction we need to travel in & how far.
		// Move that distance & direction.
		int initialPlatformRotation;
		int horizontalStepCount = 0;

		int currentStep = SeekRegionStart(onLayer, withPrinter);
		if (onLayer.ringCount == 0) {
			ConvertArcsToProfiles(currentStep, 0, withPrinter);
			ConvertToCommands(m_assignedProfiles, m_packets, withPrinter);
			Transmit(m_packets);
		}

		// INFILL
		while (onLayer.ringCount > 0) {
			// Assign the arcs for our current position; note
			// that this clears all assignments from previous
			// rings for all motors.
			int arcsAssigned = AssignArcs(onLayer, withPrinter);

			// Update the number of rings skipped; note that
			// if the number is past some threshold (2?)
			// then we can probably just MoveToStartingRing
			// again to get the biggest clump. Maybe.
			//Debug.LogError("Starting at ring number " + withPrinter.ringPosition);
			if (arcsAssigned == 0) {
				//Text.Log("No arcs assigned for ring {0}; {1} ring{2} left.",
				//	withPrinter.ringPosition, onLayer.ringCount, Text.S(onLayer.ringCount));
				withPrinter.NextRing();
				yield return null;
				continue;
			}

			// Cache this to translate the arcs later.
			initialPlatformRotation = withPrinter.platform.rotationInSteps;

			// Queue the horizontal track movement.
			Arc horizontalTrackArc = withPrinter.FinishedMoving(currentStep);
			if (horizontalTrackArc.length > 0) {
				Contract.Assert(horizontalTrackArc.motor != null,
					@"Null motor in horizontal movement.");

				withPrinter.horizTrack.stepDirection = horizontalTrackArc.direction;
				withPrinter.horizTrack.Step(horizontalTrackArc.length);
				horizontalStepCount += (int) horizontalTrackArc.direction * horizontalTrackArc.length;

				m_assignedArcs[withPrinter.horizTrack.id].Add(horizontalTrackArc);
			}

			// We've found something to print. If we're not
			// resetting the step number, then we'll need to
			// use it as an offset for the arcs that get created.
			currentStep += horizontalTrackArc.length;
			Contract.Assert(currentStep >= 0, @"Negative step value: {0}", currentStep);
			Contract.Assert(horizontalTrackArc.endStep == currentStep,
				@"Expected current step {0} to equal horizontal track end step {1}",
				currentStep, horizontalTrackArc.endStep);

			// Queue up the platform and get the base step for
			// translating extruder arcs.
			EnqueuePlatformRotation(onLayer, currentStep, withPrinter);

			Contract.Assert(m_assignedArcs[0].Count == 2, "We didn't get the correct number of platform arcs!");
			Contract.Assert(m_assignedArcs[0][1].length > 0, "Printing Arc not greater than 0: " + m_assignedArcs[0][1].length);

			// We need to update our step to the latest step from
			// the conversion.
			ConvertArcsToProfiles(currentStep, initialPlatformRotation, withPrinter);

			// NOTE: We want to send the propeller state since we've been
			// updating withPrinter's horizontal & rotational states.
			// PropellerState should only be passed to ConvertToCommands to
			// ensure we clearly update the state on the propeller.
			//Debug.Log("Ring count = " + onLayer.ringCount + " with arcs assigned = " + arcsAssigned);
			ConvertToCommands(m_assignedProfiles, m_packets, withPrinter);
			Transmit(m_packets);

			// After we're done with this set of rings, we're ready for the next.
			if (onLayer.ringCount > 0) withPrinter.NextRing();

			if (Scheduler.ShouldYield()) yield return null;

			// Let's try aligning everything to step=0 since
			// we're only using delta steps anyway…
			currentStep = 0;
		} 

		// SHELLS
		if (m_outlineSegments.Count > 0 ) {
			// We are going to change platform step-size here…might be able to do the same for horizontal movement
			// to speed things up.
			StepSize originalPlatStepSize = withPrinter.platform.stepSize;
			TickProfile aProfile = new TickProfile(withPrinter.platform, platformOutliningStepSize,
			                                       withPrinter.platform.stepDirection, 0, 0, 0);

			withPrinter.platform.stepSize = platformOutliningStepSize;
			m_propellerState.UpdateState(aProfile, m_packets);
			// End step-size changes

			for (int i = 0; i < m_outlineSegments.Count; i++) {
				initialPlatformRotation = withPrinter.platform.rotationInSteps;
				List<CartesianSegment> segments = m_outlineSegments[i]; 

				m_pather = new OutlinePathGenerator();
				m_pather.Init(withPrinter);
				OutlineCreator creator = new OutlineCreator();
				List<Outline> m_ol = creator.CreateOutlines(segments, withPrinter);
				m_pather.layerOutlines = m_ol;

				yield return Scheduler.StartCoroutine(m_pather.GeneratePathFromOutlines(currentStep, 0, 
				                                                                        m_assignedProfiles,
				                                                                        m_assignedArcs,
				                                                                        originalPlatStepSize,
				                                                                        m_depressureStepsRemaining,
				                                                                        useConstantSpeedOutlining),  
				                                      this);
				// Convert the arcs to profiles
				if (!useConstantSpeedOutlining) {
					ConvertArcsToProfiles(currentStep, initialPlatformRotation, withPrinter);
				}

				// Convert the profiles to commands
				ConvertToCommands(m_assignedProfiles, m_packets, withPrinter);
			}

			// Returning to normal stepsize for printing infill...
			aProfile = new TickProfile(withPrinter.platform, originalPlatStepSize,
			                           withPrinter.platform.stepDirection, 0, 0, 0);
			withPrinter.platform.stepSize = originalPlatStepSize;
			
			m_propellerState.UpdateState(aProfile, m_packets);
			// end stepsize change

			Contract.Assert(m_propellerState.horizTrack.stepDirection == withPrinter.horizTrack.stepDirection,
			                "Propeller ({0}) and Working ({1}) printer states for Horizontal track to not match up",
			                m_propellerState.horizTrack.stepDirection, withPrinter.horizTrack.stepDirection);
			// Send the commands
			Transmit(m_packets);
		}
	}

	#region Arc management
	/// <summary>
	/// Assigns the arcs to print to the appropriate extruders.
	/// </summary>
	/// <returns>
	/// The number of arcs assigned.
	/// </returns>
	/// <param name='fromLayer'>
	/// The layer we're reading from.
	/// </param>
	/// <param name='withPrinter'>
	/// The printer state we're using.
	/// </param>
	int AssignArcs(RingLayer fromLayer, Printer withPrinter) {
		foreach (PrinterExtruder anExtruder in withPrinter.extruders) {
			if (fromLayer.AreArcs(anExtruder.relativeRing, anExtruder.materialNumber)) {
				if (!fromLayer.TryAssigning(anExtruder)) {
					Text.Error("Conflict found.");
					fromLayer.ResolveAssignments(anExtruder, withPrinter.platform.stepsPerRotation);
				}
			}
		}

		// Each arc should now have an extruder assigned, and all arcs
		// should be distributed to the correct extruders. We can now
		// normalize the arcs to start at the correct location.
		m_areNormalizedArcs.Clear();
		foreach (PrinterExtruder anExtruder in withPrinter.extruders) {
			int positiveRingNumber = Mathf.Abs(anExtruder.relativeRing);

			if (m_areNormalizedArcs.Contains(positiveRingNumber)
				|| positiveRingNumber > fromLayer.outerRing)
			{
				// No point in going through the rest if the
				// arcs have already been normalized or if
				// the ring is larger than the outer ring.
				continue;
			}

			fromLayer.NormalizeArcs(positiveRingNumber,
				withPrinter.platform.stepsPerRotation,
				withPrinter.platform.rotationInSteps);
			m_areNormalizedArcs.Add(positiveRingNumber);
		}

		// Extract the arcs we're going to be using.
		int arcsAssigned = 0;
		fromLayer.StartExtraction();
		foreach (PrinterExtruder anExtruder in withPrinter.extruders) {
			fromLayer.ExtractArcsFor(anExtruder, m_assignedArcs);
			arcsAssigned += m_assignedArcs[anExtruder.id].Count;
		}

		// NOTE:
		// We're not checking for unassigned arcs anymore because
		// they *could* be unassigned and waiting for another extruder
		// with the correct material.
		return arcsAssigned;
	}

	/// <summary>
	/// Enqueues two arcs, one to rotate to the start
	/// of the printing, and the other to handle rotating
	/// the platform during printing.
	/// </summary>
	/// <returns>
	/// The step where printing begins.
	/// </returns>
	/// <param name='fromLayer'>
	/// The layer we're printing.
	/// </param>
	/// <param name='atStartStep'>
	/// The current global step.
	/// </param>
	/// <param name='withPrinter'>
	/// The current printer state.
	/// </param>
	void EnqueuePlatformRotation(RingLayer fromLayer, int atStartStep, Printer withPrinter) {
		// GOAL: Figure out two arcs:
		//   • Movement to the initial printing location
		//   • Movement to cover all arcs that need printing.
		int stepsPerRotation = withPrinter.platform.stepsPerRotation;
		int platformRotation = withPrinter.platform.rotationInSteps;

		// Step 1. Compare each pair of arcs from extruder A and extruder B
		// to find if we need to move more than the longest single arc. This
		// could occur if there are two arcs that need printing but there's a gap
		// in between them on the same extruder; the actual printing arc
		// should encompass the gap as well as the two arcs to print.
		int bestStartStep = 0;
		int bestEndStep   = 0;
		int lengthOfArcForPrinting = stepsPerRotation;
		for (int e0 = 0; e0 < withPrinter.extruders.Length; e0++) {
			int e0Id = withPrinter.extruders[e0].id;
			List<Arc> e0arcs = m_assignedArcs[e0Id];
		
			for (int e0a = 0; e0a < e0arcs.Count; e0a++) {
				Arc a0 = e0arcs[e0a];
				int longestLength = 0;
				int longestStart  = 0;
				int longestEnd    = 0;

				for (int e1 = 0; e1 < withPrinter.extruders.Length; e1++) {
					int e1Id = withPrinter.extruders[e1].id;
					List<Arc> e1arcs = m_assignedArcs[e1Id];

					// Find the longest arc that covers the two
					// arcs being compared.
					for (int e1a = 0; e1a < e1arcs.Count; e1a++) {
						Arc a1 = e1arcs[e1a];

						// If a0 starts inside of a1, then we can't
						// use it to calculate the distance as the
						// result will be greater than one rotation.
						if (   a0 != a1
						    && a0.startStep >= a1.startStep
						    && a0.startStep < a1.endStep)
						{
							goto ArcGreaterThanOneRotation;
						}

						int length = (a1.endStep + stepsPerRotation - a0.startStep) % stepsPerRotation;

						// A length of 0 indicates a complete rotation.
						if (a1.endStep % stepsPerRotation == a0.startStep % stepsPerRotation) {
							length = stepsPerRotation;
						}
						if (length > longestLength) {
							longestLength = length;
							longestStart  = a0.startStep;
							longestEnd    = a1.endStep;
						}
					}
				}
				// We want to use the smallest arc that covers everything.
				if (   longestLength > 0
				    && longestLength < lengthOfArcForPrinting) 
				{
					lengthOfArcForPrinting = longestLength;
					bestStartStep = longestStart;
					bestEndStep   = longestEnd;
				}
ArcGreaterThanOneRotation:;
			}
		}

		// Step 2. We need to both find the best printing direction
		// AND we need to shift or flip all the arcs such that
		// they are aligned to the best starting printing step = 0.
		// NOTE: End steps are exclusive, so we need an additional value.
		int stepsToStart          = Mathf.Abs(bestStartStep - platformRotation);
		int stepsToEnd            = Mathf.Abs(bestEndStep - platformRotation);
		int stepsViaOriginToStart = Mathf.Abs(stepsPerRotation - stepsToStart);
		int stepsViaOriginToEnd   = Mathf.Abs(stepsPerRotation - stepsToEnd);
		int minSteps = Mathf.Min(stepsToStart, stepsToEnd, stepsViaOriginToStart, stepsViaOriginToEnd);

		// For the initial angle.
		int           targetAngle;
		StepDirection printingDirection;

		// Figure out which way we'll rotate once we hit the printing region.
		if (minSteps == stepsToStart || minSteps == stepsViaOriginToStart) {
			printingDirection = StepDirection.Cw;
			targetAngle       = bestStartStep;

			// Shift arcs to align with bestStartStep.
			for (int e = 0; e < withPrinter.extruders.Length; e++) {
				int eId = withPrinter.extruders[e].id;
				for (int ea = 0; ea < m_assignedArcs[eId].Count; ea++) {
					Arc a = m_assignedArcs[eId][ea];

					int previousLength = a.length;

					a.startStep = (a.startStep - bestStartStep + stepsPerRotation) % stepsPerRotation;
					a.endStep   = a.startStep + previousLength;
				}
				m_assignedArcs[eId].Sort(Arc.CompareArcs);
			}
		}
		else {
			printingDirection = StepDirection.Ccw;
			targetAngle       = bestEndStep;

			for (int e = 0; e < withPrinter.extruders.Length; e++) {
				int eId = withPrinter.extruders[e].id;
				for (int ea = 0; ea < m_assignedArcs[eId].Count; ea++) {
					Arc a = m_assignedArcs[eId][ea];

					int previousEnd    = a.endStep;
					int previousLength = a.length;
					
					a.startStep = (targetAngle - previousEnd + stepsPerRotation) %  stepsPerRotation;
					a.endStep   = a.startStep + previousLength;
				}
				m_assignedArcs[eId].Sort(Arc.CompareArcs);
			}
		}

		// Step 3. Determine the direction we need to move to get to the
		// printing arc.
		StepDirection initialDirection;
		int halfRotation = stepsPerRotation / 2;

		int deltaSteps = targetAngle - platformRotation;
		if (   (deltaSteps > 0 && Mathf.Abs(deltaSteps) <= halfRotation)
		    || (deltaSteps < 0 && Mathf.Abs(deltaSteps) >  halfRotation))
		{
			 initialDirection = StepDirection.Cw;
		}
		else initialDirection = StepDirection.Ccw;

		// Step 4. Queue the initial movement arc and the printing arc.
		QueueArc(withPrinter.platform, initialDirection, atStartStep, minSteps,
		                      stepsPerRotation);
		int printStartStep = atStartStep + minSteps;
		if (m_shouldShiftVertical) {
			Contract.Assert(m_assignedArcs[2].Count == 2, @"Expected two vertical arcs for motor 2, not {0}", m_assignedArcs[2].Count);
			Contract.Assert(m_assignedArcs[3].Count == 2, @"Expected two vertical arcs for motor 3, not {0}", m_assignedArcs[3].Count);
			int length = m_assignedArcs[2][1].length;
			m_assignedArcs[2][1].startStep = printStartStep;
			m_assignedArcs[2][1].endStep   = printStartStep + length;
			m_assignedArcs[3][1].startStep = printStartStep;
			m_assignedArcs[3][1].endStep   = printStartStep + length;
			printStartStep += length;
			m_shouldShiftVertical = false;
		}
		QueueArc(withPrinter.platform, printingDirection, printStartStep,
		        				 lengthOfArcForPrinting, stepsPerRotation);
	}

	/// <summary>
	/// Creates and queues an arc.
	/// </summary>
	/// <returns>
	/// The queued arc.
	/// </returns>
	/// <param name='forMotor'>
	/// The motor to queue the arc for.
	/// </param>
	/// <param name='inDirection'>
	/// The arc's dircetion.
	/// </param>
	/// <param name='fromStep'>
	/// The starting step.
	/// </param>
	/// <param name='withLength'>
	/// The length of the arc.
	/// </param>
	Arc QueueArc(PrinterMotor forMotor, StepDirection inDirection,
		int fromStep, int withLength, int withStepsPerRotation)
	{
		Arc product       = new Arc(forMotor, fromStep);
		product.endStep   = fromStep + withLength;
		product.direction = inDirection;

		Contract.Assert(product.length == withLength, "Created arc " + product + " with a length different than what we wanted: " + withLength);
		Contract.Assert(product.length <= withStepsPerRotation,
			@"Arc {0} exceeds {1} step{2} per rotation.",
			product, withStepsPerRotation, Text.S(withStepsPerRotation));

		// NOTE: Always add the arc here; we'll join arcs
		// moving in the same direction later.
		m_assignedArcs[forMotor.id].Add(product);

		// Update the motor state.
		forMotor.stepDirection = inDirection;
		forMotor.Step(withLength);

		return product;
	}

	int PrimeArcs(List<Arc> arcs, int stepsPerPlatformRotation) {
		if (arcs.Count < 1) return 0;

		PrinterExtruder extruder = (PrinterExtruder)arcs[0].motor;
		int primingSteps = Mathf.Min(Mathf.Abs(extruder.relativeRing), kPrimingCap);

		int shiftRequired = 0;

		foreach (Arc anArc in arcs) {
			Contract.Assert(anArc.startStep >= 0, @"Negative start step for {0}", anArc);

			// We need to shift if the value is < 0.
			anArc.startStep -= primingSteps;
			shiftRequired = Mathf.Min(anArc.startStep, shiftRequired);
		}
		return Mathf.Abs(Mathf.Clamp(shiftRequired, int.MinValue, 0));
	}

	/// <summary>
	/// Shifts arcs such that starting at step 0
	/// becomes starting from withStepBase.
	/// </summary>
	/// <param name='forArcs'>
	/// Arcs to update.
	/// </param>
	/// <param name='withStepOffset'>
	/// The absolute equivalent to step 0 for the arcs.
	/// </param>
	void SetAbsoluteStart(List<Arc> forArcs, int withStepOffset,
		int withStepsPerRotation)
	{
		for(int anArc = 0; anArc < forArcs.Count; anArc++) {

			int originalLength = forArcs[anArc].length;
			forArcs[anArc].startStep += withStepOffset;

			forArcs[anArc].endStep = forArcs[anArc].startStep + originalLength;
			Contract.Assert(forArcs[anArc].motor != null, @"No motor assigned for arc {0}.", forArcs[anArc]);
			Contract.Assert(forArcs[anArc].startStep >= 0, @"Negative step start arc: {0}", forArcs[anArc]);
			Contract.Assert(forArcs[anArc].startStep >= withStepOffset, @"Start step before step base {0}: {1}.",
			                withStepOffset, anArc);
			Contract.Assert(forArcs[anArc].length <= m_propellerState.platform.stepsPerRotation 
			                || forArcs[anArc].motor.id >= m_propellerState.numberOfBaseMotors
			                || forArcs[anArc].hasVariableStepRate,
			                @"End step greater than steps per rotation: {0}.", forArcs[anArc]);

		}
	}
	#endregion

	#region Tick Profile management
	/// <summary>
	/// Converts the queued arcs to tick profiles, clearing
	/// any queued arcs.
	/// </summary>
	/// <param name='fromStep'>
	/// The current starting step, ≥0.
	/// </param>
	/// <param name='withPrinter'>
	/// The current printer state.
	/// </param>
	void ConvertArcsToProfiles(int fromStep, int fromInitialPlatformRotation,
		Printer withPrinter)
	{
		Contract.Assert(fromStep >= 0, @"Negative fromStep.");

		// NOTE: This is where we'd want to change the platform step-rate, if necessary.
		int platformStepRate         = Mathf.RoundToInt(withPrinter.platform.stepRate);
		int stepsPerPlatformRotation = withPrinter.platform.stepsPerRotation;

		for (int p = 0; p < m_assignedProfiles.Length; p++) {
			m_assignedProfiles[p].Clear();
		}

		int logicalStartStepOffset = m_assignedArcs[0].Count == 2 ? m_assignedArcs[0][1].startStep : 0;
		int lastPlatformStep = 0;
		if (m_assignedArcs[0].Count > 0) {
			List<Arc> arcs = m_assignedArcs[0];
			lastPlatformStep = arcs[arcs.Count - 1].endStep;
		}
		Contract.Assert(lastPlatformStep >= logicalStartStepOffset, "last Platform Step " + lastPlatformStep + " was not greater than or equal to the logical Start Step Offset " + logicalStartStepOffset);
		Contract.Assert(logicalStartStepOffset >= 0, "Logical start step offset was less than 0... " + logicalStartStepOffset);

		// NOTE:
		// We don't want the main platform rotation to accelerate
		// ––otherwise, we wouldn't line up printing correctly.
		// So for now, we won't accelerate *any* platform rotation.
		// Also, note that the platform defines the provided fromStep,
		// so we don't need to adjust anything.
		TickProfile.Convert(m_assignedArcs[0], m_assignedProfiles[0], platformStepRate);

		// NOTE: We're pulling this out because the horizontal scales
		// should be closer to the platform's. The horizontal track
		// keeps track of its starting position.
		TickProfile.Convert(m_assignedArcs[1], m_assignedProfiles[1], platformStepRate);

		// Accelerate vertical movement when
		// we're syncronized. Note that vertical tracks should always
		// start from 0.
		//int verticalStepRate = withPrinter.vertTrack[0].stepRate;
		for (int i = 0; i < m_assignedArcs[2].Count; i++) {
			Text.Log("Vert arc: {0}", m_assignedArcs[2][i]);
		}
		TickProfile.Convert(m_assignedArcs[2], m_assignedProfiles[2], platformStepRate);
		TickProfile.Convert(m_assignedArcs[3], m_assignedProfiles[3], platformStepRate);

		// NOTE: This can be merged with the main for loop, below, after
		// we're done testing.
		for (int i = withPrinter.numberOfBaseMotors;
			i < withPrinter.numberOfMotors; i++)
		{
			SetAbsoluteStart(m_assignedArcs[i], logicalStartStepOffset,
				stepsPerPlatformRotation);
		}

		int shiftForPressure = 0;
		for (int i = withPrinter.numberOfBaseMotors;
			i < withPrinter.numberOfMotors; i++)
		{
			int availablePressure;
			int availableDepressure;
			m_assignedProfiles[i].Clear();
			PrinterExtruder theExtruder = (PrinterExtruder)withPrinter.GetMotor(i);

			GetPressureStepsAvailable(theExtruder, fromStep, lastPlatformStep,
				out availablePressure, out availableDepressure);

			// Don't depressurize more than our max, kPressureSteps.
			int depressureOverSteps = Mathf.Min(availableDepressure,
				Mathf.Max(TickProfile.kPressureSteps - theExtruder.pressureRequired, 0));
			Contract.Assert(depressureOverSteps <= TickProfile.kPressureSteps,
				@"Requested too much depressure: {0}", depressureOverSteps);

			// We should be depressurizing over the rotation arc if
			// we can. Note this will increase the pressure we'll need
			// to apply.
			TickProfile.CompleteDepressurization(theExtruder,
				depressureOverSteps, platformStepRate, m_assignedProfiles[i]);

			if (m_assignedArcs[i].Count < 1) {
				// If we don't have any arcs, keep trying to depressurize up to our
				// maximum available.
				m_depressureStepsRemaining[i] -= depressureOverSteps;
			}
			else {
				// If available pressure < pressure required, then we're going to need
				// to shift everything by the difference in ticks. For that, we'll need
				// to know how negative we go when we pressurize things…
				int shiftRequired;
				int pressurizeOverSteps = theExtruder.pressureRequired;

				TickProfile.ConvertExtruder(
					m_assignedArcs[i], m_assignedProfiles[i], platformStepRate,
					stepsPerPlatformRotation, pressurizeOverSteps, lastPlatformStep,
					out shiftRequired,
					out m_depressureStepsRemaining[i]);
				shiftForPressure = Mathf.Max(shiftForPressure, shiftRequired);
			}

			m_assignedArcs[i].Clear();
		}

		if (shiftForPressure > 0) {
			// Ok, we need some more time to pressurize everything.
			// The simplest thing is to just shift *everything* up
			// by the shift required.
			foreach (List<TickProfile> profiles in m_assignedProfiles) {
				foreach (TickProfile aProfile in profiles) {
					aProfile.Add(shiftForPressure);
					Contract.Assert(aProfile.startTick >= 0,
						@"Negative start tick for {0}", aProfile);
				}
			}
		}

		#if UNITY_EDITOR
		foreach (List<TickProfile> profiles in m_assignedProfiles) {
			for (int i = 1; i < profiles.Count; ++i) {
				TickProfile a = profiles[i - 1];
				TickProfile b = profiles[i];

				if (a.tickLength == 0) {
					Text.Warning(@"Zero tick length {0}", a);
					continue;
				}

				Contract.Assert(a.startTick < b.startTick,
					"Profiles overlap.\n{0}\n{1}", a, b);
			}
		}
		#endif
	}

	int m_lastNumberSent;
	/// <summary>
	/// Converts tick profiles to commands and enqueues them in forQueue.
	/// </summary>
	/// <param name='fromProfiles'>
	/// The profiles to process.
	/// </param>
	/// <param name='forQueue'>
	/// The queue to fill.
	/// </param>
	/// <param name='withPrinter'>
	/// The propeller's printer state; used to determine whether or not
	/// we need to change step size, direction, and rates.
	/// </param>
	void ConvertToCommands(List<TickProfile>[] fromProfiles, List<TxPacket> forList,
		Printer withPrinter)
	{
		// NOTE: We don't clear the TxPacket queue here because
		// we may not have sent the data yet. Clear the arcs
		// that are marked active.
		for (int i = 0; i < m_activeProfiles.Length; ++i) m_activeProfiles[i] = null;

		TickProfile nextProfile = NextProfile(fromProfiles);
		if (nextProfile == null) return;

		int currentTick = nextProfile.startTick;
		int lastStateNumber = int.MaxValue;

		while (AreProfilesRemaining(fromProfiles, m_activeProfiles)) {
			int nextTick = int.MaxValue;

			// Any profiles starting at currentTick?
			for (int i = 0; i < fromProfiles.Length; ++i) {
				if (fromProfiles[i].Count < 1) continue;

				TickProfile startingProfile = fromProfiles[i][0];
				if (startingProfile.startTick > currentTick) {
					// We can potentially begin on the next starting tick.
					nextTick = Mathf.Min(nextTick, startingProfile.startTick);
					continue;
				}
				Contract.Assert(fromProfiles[i][0].startTick == currentTick,
					@"Missed starting step profile {0} at tick {1}.",
					fromProfiles[i][0], currentTick);

				fromProfiles[i].RemoveAt(0);
				// We started nextProfile, so clear the cached value.
				if (nextProfile == startingProfile) nextProfile = null;
				lastStateNumber = m_propellerState.UpdateState(startingProfile, forList);

				// NOTE: Since we're just clobbering the last
				// item here, we avoid the problem of zeroing
				// the old step rate and transmitting a new
				// one for adjacent arcs for the same extruder.
				Contract.Assert(m_activeProfiles[i] == null
					|| m_activeProfiles[i].endTick == currentTick,
					@"Profile {0} clobbers profile {1} at tick {2}.",
					startingProfile, m_activeProfiles[i], currentTick);
				m_activeProfiles[i] = startingProfile;
				nextTick = Mathf.Min(nextTick, startingProfile.endTick);
			}

			// Any profiles ending at currentTick?
			for (int i = 0; i < m_activeProfiles.Length; ++i) {
				if (m_activeProfiles[i] == null) continue;

				Contract.Assert(m_activeProfiles[i] != nextProfile,
					@"Next profile {0} in active profiles.", nextProfile);

				TickProfile stoppingProfile = m_activeProfiles[i];
				Contract.Assert(stoppingProfile.endTick >= currentTick,
					@"Missed stopping profile {0} by tick {1}.",
					stoppingProfile, currentTick);
				if (stoppingProfile.endTick > currentTick) {
					// Not ready to stop yet.
					nextTick = Mathf.Min(nextTick, stoppingProfile.endTick);
				}
				else if (stoppingProfile.endTick == currentTick) {
					// We found something to stop!
					stoppingProfile.stepRate = 0;
					lastStateNumber = m_propellerState.UpdateState(stoppingProfile, forList);
					m_activeProfiles[i] = null;
				}
			}

			// What's the next step coming up?
			if (nextProfile == null) nextProfile = NextProfile(fromProfiles);
			if (nextProfile != null) {
				// We still have profiles to process.
				Contract.Assert(currentTick <= nextProfile.startTick,
					@"Missed opportunity to start {0} at {1} by {2}.", nextProfile,
					currentTick, currentTick - nextProfile.startTick);
				nextTick = Mathf.Min(nextProfile.startTick, nextTick);
			}

			// If we have nowhere to go, we're done.
			if (nextTick == int.MaxValue) {
				Contract.Assert(!AreProfilesRemaining(fromProfiles, m_activeProfiles),
					@"Next tick unassigned despite profiles remaining.");
				break;
			}

			// Go to the next point of interest. Note that we don't really
			// care about the tick counter wrapping since (1) we're resetting
			// it each layer, and (2) the firmware *should* handle wrapping
			// counts correctly. If it doesn't, we'll need to change things.
			int stepsToTake = nextTick - currentTick;
			Contract.Assert(stepsToTake > 0, @"Attempting to take {0} non-positive step{1}.",
				stepsToTake, Text.S(stepsToTake));
			if (nextTick > int.MaxValue) Text.Warning(@"Propeller tick wrapped.");

			m_lastNumberSent = (lastStateNumber == int.MaxValue) ? m_lastNumberSent : lastStateNumber;

			if (   m_lastNumberSent != stepsToTake 
			    || m_lastNumberSent == int.MaxValue) 
			{
				m_propellerState.UpdateLastSentNumber(stepsToTake);
				m_lastNumberSent = stepsToTake;
				forList.Add(new TxPacket(GanglionCommand.Value, stepsToTake));

				m_propellerState.lastEnqueuedPacket = new TxPacket(GanglionCommand.Step);
				forList.Add(m_propellerState.lastEnqueuedPacket);
			}
			else if (   m_propellerState.lastEnqueuedPacket != null 
			         && m_propellerState.lastEnqueuedPacket.aCmd == GanglionCommand.StepRate) 
			{
				TxPacket lastLastPacket = (forList.Count - 2 >= 0 ? forList[forList.Count - 2] : null);
				if (lastLastPacket != null && kMotorStepRateStep.ContainsKey(lastLastPacket.aCmd))
				{
					lastLastPacket.aCmd = kMotorStepRateStep[lastLastPacket.aCmd];
					forList.RemoveAt(forList.Count - 1);
					m_propellerState.lastEnqueuedPacket = lastLastPacket;
				}
				else {
					m_propellerState.lastEnqueuedPacket.aCmd = GanglionCommand.StepRateStep;
				}
			}
			else {
				forList.Add(new TxPacket(GanglionCommand.Step));
			}

			currentTick = nextTick;
		}

#if UNITY_EDITOR
		for (int i = 0; i < fromProfiles.Length; ++i) {
			List<TickProfile> profiles = fromProfiles[i];
			Contract.Assert(profiles.Count == 0, @"Missed processing {0} profile{1} for motor {2}.",
				profiles.Count, Text.S(profiles.Count), i);
		}
		for (int i = 0; i < m_activeProfiles.Length; ++i) {
			Contract.Assert(m_activeProfiles[i] == null,
				@"Missed processing active profile {0}.", m_activeProfiles[i]);
		}
#endif
	}

	static readonly Dictionary<GanglionCommand, GanglionCommand> kMotorStepRateStep 
		= new Dictionary<GanglionCommand, GanglionCommand> 
	{
		{ GanglionCommand.Platform,   GanglionCommand.PlatformStepRateStep   },
		{ GanglionCommand.HorizTrack, GanglionCommand.HorizTrackStepRateStep },
		{ GanglionCommand.LeftTrack,  GanglionCommand.LeftTrackStepRateStep  },
		{ GanglionCommand.RightTrack, GanglionCommand.RightTrackStepRateStep },
		{ GanglionCommand.Extruder0,  GanglionCommand.Extruder0StepRateStep  },
		{ GanglionCommand.Extruder1,  GanglionCommand.Extruder1StepRateStep  },
		{ GanglionCommand.Extruder2,  GanglionCommand.Extruder2StepRateStep  },
		{ GanglionCommand.Extruder3,  GanglionCommand.Extruder3StepRateStep  }
	};

	/// <summary>
	/// Returns the steps available for depressure and pressure. Note
	/// that the same value should be used for both operations.
	/// </summary>
	/// <returns>
	/// The pressure steps available.
	/// </returns>
	/// <param name='forExtruderId'>
	/// The extruder id number.
	/// </param>
	/// <param name='withInitialStep'>
	/// The starting step for printing.
	/// </param>
	void GetPressureStepsAvailable(PrinterExtruder forExtruder, int withInitialStep,
		int lastPlatformStep,
		out int pressureAvailable, out int depressureAvailable)
	{
		// How many steps are available?
		int availableSteps = 0;
		List<Arc> arcs = m_assignedArcs[forExtruder.id];
		int arcCount = arcs.Count;

		// If we have arcs, we can only use the steps up to the first arc.
		if (arcCount > 0) {
			Arc firstArc = arcs[0];
			Contract.Assert(firstArc.startStep >= withInitialStep,
				@"Arc {0} doesn't start after {1}.", firstArc, withInitialStep);

			availableSteps = firstArc.startStep;
		}
		else {
			// If we don't have any arcs, just use the space for depressure.
			// By default, we can use the whole rotation.
			pressureAvailable = 0;
			depressureAvailable = lastPlatformStep;
			return;
		}

		// We have arcs to print, which means we need to pressurize
		// as much as possible. Ideally, this means that we have
		// enough space for a full depressure-pressure cycle.
		pressureAvailable = Mathf.Min(TickProfile.kPressureSteps, availableSteps);
		depressureAvailable = availableSteps - pressureAvailable;
		Contract.Assert(pressureAvailable >= 0, @"Negative pressure {0} available.",
			pressureAvailable);
		Contract.Assert(depressureAvailable >= 0, @"Negative depressure {0} available.",
			depressureAvailable);

		if (pressureAvailable < TickProfile.kPressureSteps) {
			// This means we don't have enough time to do a full cycle.
			// So we'll steal what we can from depressure to make it
			// even.
			int depressureStepsTaken = TickProfile.kPressureSteps
				- m_depressureStepsRemaining[forExtruder.id];

			// Pressurize the steps we've already taken, if possible.
			pressureAvailable = Mathf.Min(availableSteps, depressureStepsTaken);

			// Next, we can divide the remaining space.
			int remainingSteps = Mathf.Max(availableSteps - pressureAvailable, 0);
			int splitSteps = remainingSteps / 2;

			depressureAvailable = splitSteps;
			pressureAvailable += splitSteps;
		}

		Contract.Assert(depressureAvailable >= 0,
			@"Depressure steps available {0} shouldn't be negative for motor {1}.",
			depressureAvailable, forExtruder.id);
		Contract.Assert(pressureAvailable >= 0,
			@"Pressure steps available {0} shouldn't be negative for motor {1}.",
			pressureAvailable, forExtruder.id);
	}

	/// <summary>
	/// Returns the next earliest tick profile.
	/// </summary>
	/// <returns>
	/// The next profile or null.
	/// </returns>
	/// <param name='fromProfiles'>
	/// The profiles to search.
	/// </param>
	TickProfile NextProfile(List<TickProfile>[] fromProfiles) {
		TickProfile result = null;
		int startTick = int.MaxValue;

		for (int i = 0; i < fromProfiles.Length; ++i) {
			List<TickProfile> profiles = fromProfiles[i];
			if (profiles.Count < 1) continue;

			TickProfile initialProfile = profiles[0];
			if (initialProfile.startTick < startTick) {
				result = initialProfile;
				startTick = result.startTick;
			}
		}

		return result;
	}

	/// <summary>
	/// Returns true if either profiles remain to be processed or if
	/// any profiles are active.
	/// </summary>
	/// <returns>
	/// True if profiles to process still exist.
	/// </returns>
	/// <param name='inProfiles'>
	/// The unprocessed profiles to check.
	/// </param>
	/// <param name='orActiveProfiles'>
	/// The processed profiles to check.
	/// </param>
	bool AreProfilesRemaining(List<TickProfile>[] inProfiles, TickProfile[] orActiveProfiles) {
		foreach (List<TickProfile> profiles in inProfiles) {
			if (profiles.Count > 0) return true;
		}
		for (int i = 0; i < orActiveProfiles.Length; ++i) {
			if (orActiveProfiles[i] != null) return true;
		}
		return false;
	}
	#endregion

	#region Transmission handlers
	/// <summary>
	/// Don't send anything until we've finished heating
	/// the extruders.
	/// </summary>
	/// <param name='packets'>
	/// Packets to send.
	/// </param>
	void IgnorePackets(List<TxPacket> packets) { }

	/// <summary>
	/// Actually sends the packets to all the consumers.
	/// </summary>
	/// <param name='packets'>
	/// Packets to send.
	/// </param>
	void TransmitPackets(List<TxPacket> packets) {
		foreach (ICommandConsumer aConsumer in m_consumers) {
			for (int i = 0; i < packets.Count; i++) {
				aConsumer.BeginSendingPackets();
				aConsumer.SendPacket(packets[i]);
				aConsumer.EndSendingPackets();
			}
		}

		// Since we've sent everything, we're done.
		m_packetsSent += packets.Count;
		packets.Clear();
	}
	#endregion
	#endregion
#endif
}
