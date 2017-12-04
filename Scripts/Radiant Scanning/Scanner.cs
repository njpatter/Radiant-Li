using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading; 

// Cameras support ~7 frames per second.
// 3 frames per image.
// 24 final images per camera.
// Thus 3 * 24 = 72 frames needed
// 	=> 10.3 seconds for all frames
// 2 * 24 rotations * ~0.5 sec/rotation = 24 seconds
// 	=> 34.3 seconds low baseline.
//
// Current: ~665 sec (~11 minutes), ~699

 
[RequireComponent(typeof(PrinterController))]
public class Scanner : MonoBehaviour {
	#if !UNITY_IOS && !UNITY_ANDROID
	#region Constants
	#region SavedCameraIntrinsics
	public const string kBottomCamera = "BottomCamera";
	public const string kTopCamera = "TopCamera";
	public const string kRotation = "_Rotation";
	public const string kTranslation = "_Translation"; 
	public const string kIntrinsics = "_Intrinsics";
	public const string kIsScannerCalibrated = "IsScannerCalibrated";
	#endregion SavedCameraIntrinsics
	
	public const int 	kNumberOfAttemptToDetectCircles = 4;
	public const string kOnScannerReadyToScan = "OnScannerReadyToScan"; 
	public const string kOnUserReadyToScan = "OnUserReadyToScan";
	public const string kOnScanningProgress = "OnScanProgress";

	const string kCameraIdentifier = "Live! Cam Sync HD VF0770";

	const float kVerticalCalibrationPosition = (ScanningCamera.kCalibrationVertMoveInitialCountMm 
			+ (ScanningCamera.kNumberOfPicturesPerCameraMm - 1)
			+ ScanningCamera.kCalibrationVertMoveCountMm);
	
	public const float kRayCastDistance = 10000.0f;//500.0f; //10000f; on live, but 500.f reaches the center.

	const string kClearPlatformString = "Remove everything from the platform and close the doors.";
	const string kPlaceObjectString = "Place your object on platform.";
	#endregion

	#region Public variables
	public PrinterController pc;

	public MeshManager manager;
	public Vector3 centerOfVoxelBlob;
	public GameObject scanningView;
	
	public int numberOfSilhouettePicsToTake;
	public List<ScanningCamera> calibratedCameras;
	
	public int3 scanVolumeStart = int3.zero;
	public int3 scanVolumeEnd = new int3(301, 301, 301);

	public int cutThreshold;
	
	public GameObject scanReadyButton;
	public GameObject scanMenuText;
	
	public Material projectionMat;
	#endregion

	#region Private variables
	Printer m_workingPrinter;
	float m_rotationPerPicture;
	bool m_readyToScan = false;
	List<Thread> m_scanningThreads;
	#endregion

	#region Initialization & deconstruction
	void Awake() {
		scanningView.SetActive(false);
		m_scanningThreads = new List<Thread>();
	}
	
	void Start() {
		// Initialize OpenCV for use in calibrating cameras
		OpenCV.Init();
		// Grab the Printer controller so that we can make it move
		pc = GetComponent<PrinterController>();
		m_workingPrinter = pc.originalPrinter.Clone();

		// Disable the scanner for now
		enabled = false;

		m_rotationPerPicture = 2.0f / (float)numberOfSilhouettePicsToTake * Mathf.PI;
	}

	void OnDestroy() {
		m_isScanning = false;
		foreach(ScanningCamera sc in calibratedCameras) {
			sc.Stop();
		}
		foreach (Thread t in m_scanningThreads) {
			if (t.IsAlive) {
				t.Abort();
			}
		}
	}
	#endregion

	/// <summary>
	/// Finds all possible scanning cameras
	/// </summary>
	/// <returns>A list of possible scanning cameras.</returns>
	List<ScanningCamera> GetScanningCameras() {
		List<ScanningCamera> uncalibratedCameras = new List<ScanningCamera>();
		foreach(WebCamDevice wcd in WebCamTexture.devices) { 
			if (wcd.name.Contains(kCameraIdentifier)) {
				uncalibratedCameras.Add(new ScanningCamera(wcd));
			}
		}
		Text.Log("Found {0} usable cameras.", uncalibratedCameras.Count);
		return uncalibratedCameras;
	}

	void StopCameras() {
		foreach (ScanningCamera camera in calibratedCameras) {
			camera.Stop();
			camera.Shutdown();
		}
	}

	/// <summary>
	/// Moves for calibration.
	/// </summary>
	/// <returns>The for calibration.</returns>
	/// <param name="mm">The number of mm to move.</param>
	IEnumerator MoveForCalibration(int mm) {
		pc.BeginMotorChanges(m_workingPrinter);
		pc.MoveVerticallyBy(-mm, Change.Execute);
		pc.EndMotorChanges();

		yield return Scheduler.StartCoroutine(pc.WaitUntilDoneMoving());
		yield return new WaitSeconds(1f);
	}

	IEnumerator CalibrateCamera(int cameraNumber, string identifier, 
	                            List<ScanningCamera> uncalibratedCameras, 
	                            float verticalPosition) 
	{
		Text.Log("Starting to look for camera {0}", cameraNumber);

		int cameraIndex = 0;
		int circleSearchAttempts = 0;
		while (circleSearchAttempts < kNumberOfAttemptToDetectCircles 
		       && calibratedCameras.Count == cameraNumber) 
		{
			cameraIndex = 0;
			foreach (ScanningCamera sc in uncalibratedCameras) {
				// Start the camera and take a picture
				yield return Scheduler.StartCoroutine(sc.StartCapturing());
				scanningView.renderer.material.mainTexture = sc.webcamImage;
				yield return Scheduler.StartCoroutine(sc.TakeRawPicture());

				// Stop the camera and wait a second to make sure it stopped
				sc.Stop();
				yield return new WaitSeconds(1.0f);

				// Find the number of circles in the picture and add the camera to the list of it matches up
				List<Vector2> cornerList = sc.FindChessboardsInImage();

				if (cornerList.Count == ScanningCamera.kNumChessboardPoints * 2) {
					// Correct!
					calibratedCameras.Add(sc);
					break;
				} 
				else {
					Text.Log("Found {0} points with camera {1}, but need {2} to be correct.",
					         cornerList.Count, sc.cameraDevice.name, 
					         ScanningCamera.kNumChessboardPoints * 2);
				}
				// Stop the camera and wait a second to make sure it stopped
				sc.Stop();
				yield return new WaitSeconds(1.0f);
				++cameraIndex;
			}
			++circleSearchAttempts;
		}

		if (calibratedCameras.Count == cameraNumber) {
			Text.Error("Couldn't find camera {0}.", cameraNumber);
			yield break;
		}
		else {
			Text.Log("Found camera {0}.", cameraNumber);
		}
		uncalibratedCameras.RemoveAt(cameraIndex); 

		// Cameras are now identified. Calibrate or load calibration data as needed.
		if (!isScannerCalibrated) {
			yield return Scheduler.StartCoroutine(FindCalibrationPointsAndCalibrate(calibratedCameras[cameraNumber], 
			                                                                        verticalPosition));
			SaveIntrinsics(identifier, calibratedCameras[cameraNumber]);
		}
		else {
			pc.BeginMotorChanges(m_workingPrinter);
			pc.MoveVerticallyBy(-ScanningCamera.kNumberOfPicturesPerCameraMm, Change.Execute);
			pc.EndMotorChanges();
			LoadIntrinsics(identifier, calibratedCameras[cameraNumber]);
		}
	}

	/// <summary>
	/// Calibrates the scanning cameras.
	/// </summary>
	IEnumerator CalibrateCameras() {
		calibratedCameras = new List<ScanningCamera>();
		List<ScanningCamera> uncalibratedCameras = GetScanningCameras();
		if (uncalibratedCameras.Count != 2) {
			Text.Error("Incorrect number of cameras found!");
			yield break;
		}

		foreach (ScanningCamera c in uncalibratedCameras) {
			c.Ready();
		}

		yield return Scheduler.StartCoroutine(MoveForCalibration(ScanningCamera.kCalibrationVertMoveInitialCountMm));
		scanningView.SetActive(true);

		// Get the bottom camera.
		yield return Scheduler.StartCoroutine(CalibrateCamera(0, kBottomCamera, uncalibratedCameras, 
		                                                      ScanningCamera.kCalibrationVertMoveInitialCountMm));
		
		// Get the top camera.
		yield return Scheduler.StartCoroutine(MoveForCalibration(ScanningCamera.kCalibrationVertMoveCountMm));
		yield return Scheduler.StartCoroutine(CalibrateCamera(1, kTopCamera, uncalibratedCameras,
		                                                      kVerticalCalibrationPosition));

		scanningView.SetActive(false);

		// Return to the calibrated position.
		yield return Scheduler.StartCoroutine(pc.Seek(GanglionEdge.Bottom));

		pc.TurnBacklightOff();
		pc.TurnFetOnOff(GanglionPowered.pScannerBackLight, true);
		pc.TurnFetOnOff(GanglionPowered.pScannerMidLight, true);
		// The following should be off!
		//printer.TurnFetOnOff(GanglionPowered.Fet4, true);
		                     
		if (calibratedCameras.Count != 2) {
			Text.Error("Expected 2 cameras, not {0}.", calibratedCameras.Count);
		}
		else {
			isScannerCalibrated = true;
		}
	}

	/// <summary>
	/// Moves the printer to multiple locations so that the chosen camera can find the calibration points
	/// and eventually calibrate itself
	/// </summary>
	/// <returns>The calibration points and calibrate.</returns>
	/// <param name="aCamera">A camera.</param>
	/// <param name="verticalPosition">Vertical position.</param>
	IEnumerator FindCalibrationPointsAndCalibrate(ScanningCamera aCamera, 
		float verticalPosition) 
	{
		yield return Scheduler.StartCoroutine(aCamera.StartCapturing());
		scanningView.renderer.material.mainTexture = aCamera.webcamImage;
		yield return Scheduler.StartCoroutine(aCamera.TakePicture());

		while (!aCamera.AddPointsFromChessboardPicture(verticalPosition)) {
			Text.Warning("Attempting to find chessboard in another picture...");
			yield return Scheduler.StartCoroutine(aCamera.TakePicture());
		}

		int picturesToTake = Mathf.FloorToInt(ScanningCamera.kNumberOfPicturesPerCameraMm / ScanningCamera.kCalibrationVertMoveMm) - 1;
		for (int i = 0; i < picturesToTake; ++i) {
			// Move to next position
			pc.BeginMotorChanges(m_workingPrinter);
			pc.MoveVerticallyBy(-ScanningCamera.kCalibrationVertMoveMm, Change.Execute);
			pc.EndMotorChanges();
			verticalPosition += ScanningCamera.kCalibrationVertMoveMm;

			yield return Scheduler.StartCoroutine(pc.WaitUntilDoneMoving());
			yield return new WaitSeconds(1f);
			yield return Scheduler.StartCoroutine(aCamera.TakePicture());

			while(!aCamera.AddPointsFromChessboardPicture(verticalPosition)) {
				yield return Scheduler.StartCoroutine(aCamera.TakePicture());
			}
			yield return null;
		}

		aCamera.Stop();
		yield return Scheduler.StartCoroutine(aCamera.CalibratePosition());
	}

	/// <summary>
	/// Scans an object.
	/// </summary>
	/// <returns>The object.</returns>
	public IEnumerator ScanObject() {
		Contract.Assert(!enabled, "Scanner already running.");

		enabled = true;
		m_workingPrinter = pc.originalPrinter.Clone();

		m_readyToScan = false;
		Dispatcher<string, PanelController.Handler, PanelController.Handler>.Broadcast(
			PanelController.kEventConfirm, kClearPlatformString, 
			delegate { 
				m_readyToScan = true; 
			},
			delegate { 
				StopCameras();
				enabled = false;
				Scheduler.StopCoroutines(this);
			});
		yield return new WaitUntil(() => m_readyToScan);

		Dispatcher<Panel>.Broadcast(PanelController.kEventOpenPanel, Panel.Message);
		Dispatcher<string>.Broadcast(PanelController.kEventShowMessage, "Calibrating...");
		
		//Prepare Printer
		pc.InitializeMotors(m_workingPrinter);
		// Turn on lights to best detect calibration pattern
		pc.TurnFetOnOff(GanglionPowered.pScannerBackLight, true);
		pc.TurnFetOnOff(GanglionPowered.pScannerMidLight, true);
		m_workingPrinter.extruders = new PrinterExtruder[0];

		// Go to the bottom for scanning
		yield return Scheduler.StartCoroutine(pc.CalibrateBottom(m_workingPrinter));

		// Calibrate the cameras if needed.
		int timeout = 5;
		if (calibratedCameras.Count != 2) {
			while (calibratedCameras.Count < 2 && timeout-- > 0) {
				Text.Log("Attempting to calibrate cameras...");
				yield return Scheduler.StartCoroutine(CalibrateCameras());
			}

			if (timeout == 0) {
				Text.Error("Failed to detect cameras; aborting.");
				Dispatcher<Panel>.Broadcast(PanelController.kEventClosePanel, Panel.Progress);
				enabled = false;
				yield break;
			}
		}
		else {
			foreach (ScanningCamera c in calibratedCameras) {
				c.Ready();
			}
		}

		yield return Scheduler.StartCoroutine(SilhouetteScan());
		
		Dispatcher<Panel>.Broadcast(PanelController.kEventClosePanel, Panel.Progress);
		enabled = false;
	}

	/// <summary>
	/// Uses a silhouette scanning setup
	/// </summary>
	/// <returns>The scan.</returns>
	IEnumerator SilhouetteScan() {
		Text.Log("SilhouetteScan.");
		bool isCancelled = false;

		manager.pauseUpdateCollision = true;
		m_blobSize = manager.m_blob.size;

		foreach(ScanningCamera sc in calibratedCameras) {
			yield return Scheduler.StartCoroutine(sc.StartCapturing());
			yield return Scheduler.StartCoroutine(sc.TakeCalibrationPicture());
			sc.Stop();
		}

		m_scanDataBlob = new byte[m_blobSize.x, m_blobSize.y, m_blobSize.z];
		m_readyToScan = false;
		bool shouldBreak = false;
		Dispatcher<Panel>.Broadcast(PanelController.kEventClosePanel, Panel.Message);
		Dispatcher<string, PanelController.Handler, PanelController.Handler>.Broadcast(
			PanelController.kEventConfirm, kPlaceObjectString, 
			delegate { m_readyToScan = true; },
			delegate { m_readyToScan = shouldBreak = true; StopCameras(); }
		);
		yield return new WaitUntil(() => m_readyToScan || shouldBreak);
		if (shouldBreak) {
			yield break;
		}

		m_readyToScan = false;
		Dispatcher<string, string, PanelController.Handler, PanelController.Handler>.Broadcast(
			PanelController.kEventShowProgress,
			Scanner.kOnScanningProgress, "Scanning {0:0%}", Nop, 
			delegate{ 
				isCancelled = true; 
				m_isScanning = false; 
				scanningView.SetActive(false);
				StopCameras();
			});

		float initialTime = Time.realtimeSinceStartup;

		scanningView.SetActive(true);
		m_isScanning = true;

		m_workQueue = new Queue<ScanWorkPacket>();
		int numThreads = Math.Max(1, System.Environment.ProcessorCount / 2);
		Text.Log(@"Starting {0} thread{1}.", numThreads, Text.S(numThreads));
		m_rowsProcessed = 0;
		foreach (Thread t in m_scanningThreads) {
			if (t.IsAlive) {
				t.Abort();
			}
		}
		m_scanningThreads.Clear();
		for (int i = 0; i < numThreads; ++i) {
			Thread worker = new Thread(ImageProcessor);
			worker.Start();
			m_scanningThreads.Add(worker);
		}

		int cameraIndex = 0;
		int rowsExpected = 0;
		foreach (ScanningCamera sc in calibratedCameras) {
			yield return Scheduler.StartCoroutine(sc.StartCapturing());
			scanningView.renderer.material.mainTexture = sc.webcamImage;

			for (int i = 0; i < numberOfSilhouettePicsToTake; i++) {
				yield return Scheduler.StartCoroutine(sc.TakePicture());

				float progressAddition = (cameraIndex == 1) ? 0.5f : 0f;
				Dispatcher<float>.Broadcast(kOnScanningProgress, ((float)i / (float)numberOfSilhouettePicsToTake) / 
				                            (float)calibratedCameras.Count + progressAddition);

				rowsExpected += sc.imageHeight;
				m_workQueue.Enqueue(new ScanWorkPacket(centerOfVoxelBlob, sc));

				if (Scheduler.ShouldYield()) {
					yield return null;
				}
				
				Matrix eulerRotate = Matrix.Zero(3,1);
				eulerRotate[1,0] = -m_rotationPerPicture;	// The original.
				//eulerRotate[1,0] = m_rotationPerPicture;
				
				Matrix rotateAroundY = Matrix.Zero(3,3); 
				OpenCV.cvRodrigues2(eulerRotate.matPtr, rotateAroundY.matPtr, IntPtr.Zero); 

				sc.worldToCameraRotation = sc.worldToCameraRotation * rotateAroundY;

				OpenCV.cvReleaseMat(ref rotateAroundY.matPtr);
				OpenCV.cvReleaseMat(ref eulerRotate.matPtr);

				pc.BeginMotorChanges(m_workingPrinter);
				pc.RotateBySteps(0, 
			                      m_workingPrinter.platform.stepsPerRotation / numberOfSilhouettePicsToTake, 
			                      m_workingPrinter.horizTrack.stepDirection,
			                      Change.Execute);
				pc.EndMotorChanges();
				yield return Scheduler.StartCoroutine(pc.WaitUntilDoneMoving());

				if (isCancelled) {
					pc.TurnBacklightOff();
					m_workQueue.Clear();
					//if (printer.serialController != null) {
					//	printer.serialController.ClearRxBuffer();
					//}
					scanningView.SetActive(false);
					StopCameras();
					yield break;
				}
			}
			sc.Stop();
			++cameraIndex;
		}
		scanningView.SetActive(false);

		Dispatcher<string, string, PanelController.Handler, PanelController.Handler>.Broadcast(
			PanelController.kEventShowProgress,
			Scanner.kOnScanningProgress, "Processing {0:0%}", Nop, 
			// TODO: Actually cancel things…to do this, we may need to make some changes
			// in the blob format and undo system.
			null);

		do {
			// NOTE: Changed 2.0 -> 5.0f
			yield return new WaitSeconds(5.0f);
			Dispatcher<float>.Broadcast(kOnScanningProgress, ((float)m_rowsProcessed / (float)rowsExpected) * 0.75f);
		} while (m_rowsProcessed < rowsExpected && !isCancelled);
		m_isScanning = false;

		VoxelBlob blob = new VoxelBlob(m_blobSize.x, m_blobSize.y, m_blobSize.z, false);
		yield return Scheduler.StartCoroutine(SetVoxelsInBlob((byte)cutThreshold, blob, m_scanDataBlob, null));
		m_scanDataBlob = null;

		Dispatcher<float>.Broadcast(kOnScanningProgress, 0.75f);
		yield return Scheduler.StartCoroutine(manager.AddVoxelBlob(blob, Vector3.zero));
		manager.pauseUpdateCollision = false;
		while (manager.isUpdating) {
			yield return new WaitSeconds(1.0f);
		}
		
		Dispatcher<float>.Broadcast(kOnScanningProgress, 1f);
		Text.Log("Done scanning and updating meshes after {0} sec.", Time.realtimeSinceStartup - initialTime);

		pc.TurnBacklightOff();
		Contract.Assert(m_workQueue.Count == 0, @"Didn't process {0} image{1}.",
		                m_workQueue.Count, Text.S(m_workQueue.Count));
	}

	#region Worker threads
	class ScanWorkPacket {
		public BitArray image;
		public int width;
		public int height;

		public Vector3 position;
		public float[,] cameraIntrinsics;
		public float[,] worldToCameraRotation;
		public float[,] worldToCameraTranslation;

		public ScanWorkPacket(Vector3 centerOfVoxelBlob, ScanningCamera c) {
			image = new BitArray(c.mostRecentImage);
			width = c.imageWidth;
			height = c.imageHeight;

			position = c.worldPosition + centerOfVoxelBlob;
			cameraIntrinsics = c.intrinsics.ToArray();
			worldToCameraRotation = c.worldToCameraRotation.ToArray();
			worldToCameraTranslation = c.worldToCameraTranslation.ToArray(); 
		}
	}
	Queue<ScanWorkPacket> m_workQueue;
	int m_rowsProcessed;
	byte[,,] m_scanDataBlob;
	bool m_isScanning;
	int3 m_blobSize;

	void ImageProcessor() {
		try {
			while (m_isScanning) {
				ScanWorkPacket packet = null;
				lock (m_workQueue) {
					if (m_workQueue.Count > 0) {
						packet = m_workQueue.Dequeue();
					}
				}
				if (packet == null) {
					Thread.Sleep(1000);
					continue;
				}

				// Coefficients for calculating the world position.
				float alpha = kRayCastDistance / packet.cameraIntrinsics[0, 0];
				float beta  = kRayCastDistance / packet.cameraIntrinsics[1, 1];

				// Invariants for calculating the world position.
				float x1 = -packet.cameraIntrinsics[0, 2] * alpha - packet.worldToCameraTranslation[0, 0];
				float y1 = -packet.cameraIntrinsics[1, 2] * beta  - packet.worldToCameraTranslation[1, 0];
				float z  = kRayCastDistance - packet.worldToCameraTranslation[2, 0];

				Vector3 v = new Vector3(0, 0, z);
				Vector3 worldPos = new Vector3();

				int i = 0;
				int rowFromTop = packet.height - 1;
				for (int row = 0; row < packet.height; ++row, --rowFromTop) {
					for (int col = 0; col < packet.width; ++col, ++i) {
						if (packet.image[i]) continue;
						// Calculates the world position.
						v.x = alpha * col + x1;
						v.y = beta  * rowFromTop + y1;
							
						worldPos.x = packet.worldToCameraRotation[0, 0] * v[0]
							+ packet.worldToCameraRotation[1, 0] * v[1] 
							+ packet.worldToCameraRotation[2, 0] * v[2]
							+ centerOfVoxelBlob.x;
						worldPos.y = packet.worldToCameraRotation[0, 1] * v[0] 
							+ packet.worldToCameraRotation[1, 1] * v[1] 
							+ packet.worldToCameraRotation[2, 1] * v[2]
							+ centerOfVoxelBlob.y;
						worldPos.z = packet.worldToCameraRotation[0, 2] * v[0] 
							+ packet.worldToCameraRotation[1, 2] * v[1] 
							+ packet.worldToCameraRotation[2, 2] * v[2]
							+ centerOfVoxelBlob.z;
						
						SetVoxelsAlongLine(packet.position, GetWorldPositionOfPixelCoordinate(rowFromTop,
						                                                                      col, packet.cameraIntrinsics,
						                                                                      packet.worldToCameraTranslation,
						                                                                      packet.worldToCameraRotation));
					}
				}
				lock (this) {
					m_rowsProcessed += packet.height;
				}
			}
		}
		catch (System.Threading.ThreadAbortException) {
			// No cleanup; just kill the thread.
		}
	}
	#endregion

	#region Debugging
#if UNITY_EDITOR
	Color[] m_gizmoColors = { Color.magenta, Color.cyan };

	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	void OnDrawGizmos() {
		int i = 0;
		foreach (ScanningCamera c in calibratedCameras) {
			Gizmos.color = m_gizmoColors[i++];
			float[,] intr = c.intrinsics.ToArray();
			float[,] tran = c.worldToCameraTranslation.ToArray();
			float[,] rota = c.worldToCameraRotation.ToArray();
			Vector3 o = c.worldPosition + centerOfVoxelBlob;

			Vector3 p0 = GetWorldPositionOfPixelCoordinate(0, 0, intr, tran, rota);
			Vector3 p1 = GetWorldPositionOfPixelCoordinate(0, 1279, intr, tran, rota);
			Vector3 p2 = GetWorldPositionOfPixelCoordinate(719, 0, intr, tran, rota);
			Vector3 p3 = GetWorldPositionOfPixelCoordinate(719, 1279, intr, tran, rota);


			Gizmos.DrawSphere(p0, 0.5f);
			Gizmos.DrawLine(o, p0);

			Gizmos.color = Color.black;
			Gizmos.DrawSphere(p1, 0.5f);
			Gizmos.DrawLine(o, p1);
			Gizmos.DrawSphere(p2, 0.5f);
			Gizmos.DrawLine(o, p2);
			Gizmos.DrawSphere(p3, 0.5f);
			Gizmos.DrawLine(o, p3);
		}
	}
#endif
	#endregion

	Vector3 GetWorldPositionOfPixelCoordinate(int rowFromTop, int col, 
	                                          float[,] intrinsics, 
	                                          float[,] worldToCameraTranslation,
	                                          float[,] worldToCameraRotation) 
	{
		Vector3 camCoords = new Vector3(
			(col        - intrinsics[0, 2]) / intrinsics[0, 0] * kRayCastDistance,
			(rowFromTop - intrinsics[1, 2]) / intrinsics[1, 1] * kRayCastDistance,
			kRayCastDistance);
		
		for (int i = 0; i < 3; ++i) {
			camCoords[i] -= worldToCameraTranslation[i, 0];
		}
		
		Vector3 end = new Vector3();
		for(int i = 0; i < 3; i++) {
			end[i] = 
				worldToCameraRotation[0, i] * camCoords[0]
				+ worldToCameraRotation[1, i] * camCoords[1]
				+ worldToCameraRotation[2, i] * camCoords[2];
		}
		
		return end + centerOfVoxelBlob;
	}

	IEnumerator SetVoxelsInBlob(byte threshold, VoxelBlob blob, byte[,,] data, byte[,,] debugData) {
		for (int x = scanVolumeStart.x; x < scanVolumeEnd.x; ++x) {
			for (int y = scanVolumeStart.y; y < scanVolumeEnd.y; ++y) {
				for (int z = scanVolumeStart.z; z < scanVolumeEnd.z; ++z) {
					if (data[x, y, z] <= threshold) {
						blob[x, y, z] = 1;
					}
					else {
						blob[x, y, z] = 0;
					}
				}
			}
			if (Scheduler.ShouldYield()) yield return null;
		}
	}

	void SetVoxelsAlongLine(Vector3 start, Vector3 end) {
		Vector3 stepDelta = end - start; 
		float maxStep = Mathf.Max(Mathf.Abs(stepDelta.x), 
		                          Mathf.Abs(stepDelta.y), 
		                          Mathf.Abs(stepDelta.z));
		stepDelta /= maxStep;

		int firstX = Mathf.Min(Mathf.RoundToInt((m_blobSize.x - 1 - start.x) / stepDelta.x), 
		                   Mathf.RoundToInt(-start.x / stepDelta.x));
		int lastX = Mathf.Max(Mathf.RoundToInt((m_blobSize.x - 1 - start.x) / stepDelta.x), 
		                  Mathf.RoundToInt(-start.x / stepDelta.x));
		
		int firstY = Mathf.Min(Mathf.RoundToInt((m_blobSize.y - 1 - start.y) / stepDelta.y), 
		                   Mathf.RoundToInt(-start.y / stepDelta.y));
		int lastY = Mathf.Max(Mathf.RoundToInt((m_blobSize.y - 1 - start.y) / stepDelta.y), 
		                  Mathf.RoundToInt(-start.y / stepDelta.y));
		
		int firstZ = Mathf.Min(Mathf.RoundToInt((m_blobSize.z - 1 - start.z) / stepDelta.z), 
		                   Mathf.RoundToInt(-start.z / stepDelta.z));
		int lastZ =  Mathf.Max(Mathf.RoundToInt((m_blobSize.z - 1 - start.z) / stepDelta.z), 
		                   Mathf.RoundToInt(-start.z / stepDelta.z));
		
		int firstStepInBounds = Mathf.Max(0, firstX, firstY, firstZ);
		int lastStepInBounds  = Mathf.Min(lastX, lastY, lastZ);
		
		Vector3 currentScanlinePos = start + firstStepInBounds * stepDelta;
		
		int xPos, yPos, zPos;
		int xDim = m_blobSize.x;
		int yDim = m_blobSize.y;
		int zDim = m_blobSize.z;
		for (int i = firstStepInBounds; i < lastStepInBounds ; i++) {
			xPos = Mathf.RoundToInt(currentScanlinePos.x);
			yPos = Mathf.RoundToInt(currentScanlinePos.y);
			zPos = Mathf.RoundToInt(currentScanlinePos.z);
			if (xPos > -1 && yPos > -1 && zPos > -1 
			    && xPos < xDim && yPos < yDim && zPos < zDim 
			    && m_scanDataBlob[xPos, yPos, zPos] < byte.MaxValue)
			{
				m_scanDataBlob[xPos, yPos, zPos]++;
			}
			currentScanlinePos += stepDelta;
		}
	}

	/// <summary>
	/// No operation; used for progress bar.
	/// </summary>
	void Nop() {}

	#region Camera Instrinsics Saving, Loading, and Bool Checks
	void SaveIntrinsics(string cameraPosition, ScanningCamera aCam) {
		for(int i = 0; i < 3; i++) {
			PlayerPrefs.SetFloat(cameraPosition + kTranslation + i, 
				aCam.worldToCameraTranslation[i,0]);

			for(int j = 0; j < 3; j++) {
				PlayerPrefs.SetFloat(cameraPosition + kRotation + i + j, 
					aCam.worldToCameraRotation[i,j]);
				PlayerPrefs.SetFloat(cameraPosition + kIntrinsics + i + j,
				                     aCam.intrinsics[i,j]);
			}
		}
	}

	void LoadIntrinsics(string cameraPosition, ScanningCamera aCam) {
		aCam.worldToCameraTranslation = new Matrix(3,1);
		aCam.worldToCameraRotation = new Matrix(3,3);
		aCam.intrinsics = new Matrix(3,3);
		for(int i = 0; i < 3; i++) {
			aCam.worldToCameraTranslation[i,0] = PlayerPrefs.GetFloat(
				cameraPosition + kTranslation + i);

			for(int j = 0; j < 3; j++) {
				aCam.worldToCameraRotation[i,j] = PlayerPrefs.GetFloat(
					cameraPosition + kRotation + i + j); 
				aCam.intrinsics[i,j] = PlayerPrefs.GetFloat(
					cameraPosition + kIntrinsics + i + j); 
			}
		}
	}
	
	public bool isScannerCalibrated {
		get {
			bool calibrated = 1 == PlayerPrefs.GetInt(kIsScannerCalibrated);
			return calibrated;
		}
		set {
			PlayerPrefs.SetInt(kIsScannerCalibrated, value ? 1 : 0);
		}
	}
	#endregion Camera Instrinsics Saving, Loading, and Bool Checks
#endif
}
