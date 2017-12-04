using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[System.Serializable] 
public class ScanningCamera { 
	#if !UNITY_IOS && !UNITY_ANDROID
	#region Constants
	#region Calibration constants
	public const float 	kCalibrationVertMoveMm	 		   = 2.5f; 
	public const int 	kCalibrationVertMoveInitialCountMm = 50;
	public const int 	kNumberOfPicturesPerCameraMm 	   = 40;
	public const int 	kCalibrationVertMoveCountMm 	   = 100;
	public const int 	kMaxNumberOfScanningCameras 	   = 2;
	#endregion

	#region Image constants
	public const int kChessboardWidth = 10;
	public const int kChessboardHeight = 5;
	public const int kNumChessboardPoints = (kChessboardWidth - 1) * (kChessboardHeight - 1);
	public const float kGrayscaleBlueWeight = 0.114f;
	public const float kGrayscaleRedWeight = 0.299f;
	public const float kGrayscaleGreenWeight = 0.587f;
	public const int kSearchImageScaleFactor = 2;

	const float kPixelDiffThreshold = 0.1f; // Higher numbers result in more gaps.
	const float kSqrPixelThreshold = kPixelDiffThreshold * kPixelDiffThreshold;
	#endregion
	#endregion

	#region Public variables
	public WebCamDevice cameraDevice;
	public WebCamTexture webcamImage;

	public int imageWidth = 1280;
	public int imageHeight = 720;

	public Matrix intrinsics;
	public Matrix distortion;
	public Vector2 fieldOfViewAngles;
	public Matrix worldToCameraTranslation;
	public Matrix worldToCameraRotation;
	public Vector3 worldPosition {
		get {
			return -(worldToCameraRotation.t() * worldToCameraTranslation).ToVector3();
		}
	}

	public List<Vector2> m_calibrationCornerPixels;
	public List<Vector3> m_calibrationCornerWorldPoints;

	public BitArray mostRecentImage;
	#endregion

	#region Private variables
	int m_pictureNumber;
	Color32[] calibrationImage;
	Color32[][] m_pixels;
	#endregion

	#region Initialization and teardown
	/// <summary>
	/// Initializes a new instance of the <see cref="ScanningCamera"/> class.
	/// </summary>
	public ScanningCamera (WebCamDevice aWebCam) {
		cameraDevice = aWebCam;
		m_calibrationCornerPixels = new List<Vector2>();
		m_calibrationCornerWorldPoints = new List<Vector3>();

		distortion = Matrix.Zero(5,1);
		intrinsics = Matrix.Zero(3,3);
		intrinsics[0, 0] = 1140f;
		intrinsics[0, 2] = 640;
		intrinsics[1, 1] = 1140;
		intrinsics[1, 2] = 360;
		intrinsics[2, 2] = 1f;
		fieldOfViewAngles = new Vector2(2f * Mathf.Atan2(((float)imageWidth / 2f), (float)intrinsics[0,0]), 
				2f * Mathf.Atan2(((float)imageHeight / 2f), (float)intrinsics[1,1]));
	}
	#endregion

	#region Sequence methods
	/// <summary>
	/// Initializes memory for taking pictures.
	/// </summary>
	public void Ready() {
		// NOTE: The following assumes that we're not changing
		// the image width and height while running.
		// For median filtering, later.
		m_pixels = new Color32[3][];
		for (int i = 0; i < 3; ++i) {
			m_pixels[i] = new Color32[imageWidth * imageHeight];
		}

		Text.Log("Creating blank calibration and most recent image arrays");
		calibrationImage = new Color32[imageWidth * imageHeight];
		mostRecentImage = new BitArray(imageWidth * imageHeight);
	}

	/// <summary>
	/// Frees memory.
	/// </summary>
	public void Shutdown() {
		//Text.Warning("Clearing out calibration image");
		calibrationImage = null;
		m_pixels = null;
	}

	/// <summary>
	/// Begins capturing frames.
	/// </summary>
	/// <returns>The capturing.</returns>
	public IEnumerator StartCapturing() {
		Contract.Assert(m_pixels != null, "Ready() not called before capturing.");

		if (webcamImage != null && webcamImage.isPlaying) {
			Debug.LogWarning("Camera already playing.");
			webcamImage.Stop();
			yield return null;
		}

		webcamImage = new WebCamTexture(cameraDevice.name, imageWidth, imageHeight, kFramesPerSecond);
		webcamImage.Play();
		yield return new WaitSeconds(5f);
	}

	/// <summary>
	/// Stops capturing frames.
	/// </summary>
	public void Stop() {
		if (webcamImage != null) webcamImage.Stop();
		webcamImage = null;
	}
	#endregion

	#region Public capturing methods
	public IEnumerator TakeCalibrationPicture() {
		yield return Scheduler.StartCoroutine(CaptureFrame());

		Color32[] pixels = m_pixels[0];
		for (int i = 0; i < imageWidth * imageHeight; ++i) {
			calibrationImage[i] = pixels[i];
		}

#if UNITY_EDITOR || DEBUG_SCANNING
		WritePixels("Diff base " + webcamImage.deviceName);
#endif
	}

	public IEnumerator TakePicture() {
		yield return Scheduler.StartCoroutine(CaptureFrame());
#if UNITY_EDITOR || DEBUG_SCANNING
		WritePixels("Prediff " + m_pictureNumber + " " + webcamImage.deviceName);
#endif

		CreateDiffImage();
#if UNITY_EDITOR || DEBUG_SCANNING
		WritePixels("Postdiff " + m_pictureNumber + " " +  webcamImage.deviceName);
		m_pictureNumber++;
#endif
	}

	public IEnumerator TakeRawPicture() {
		m_pictureNumber = 0;
		yield return Scheduler.StartCoroutine(CaptureFrame());
#if UNITY_EDITOR || DEBUG_SCANNING
		WritePixels("Precalibration " + webcamImage.deviceName);
#endif
	}
	#endregion

	#region Capture & support methods
	const int kFramesPerSecond = 5;
	const float kCaptureTimeoutSec = 2.5f;
	IEnumerator CaptureFrame() {
		float secPerFrame = 1.0f / (float)kFramesPerSecond;
		int i = 0;

nextLoop:
		while (i < 3) {
			float timeLeft = kCaptureTimeoutSec;
			
			do {
				yield return new WaitSeconds(secPerFrame);
				
				float captureWindow = 3f * secPerFrame; // + secPerFrame;
				timeLeft -= secPerFrame + captureWindow;// + secPerFrame;
				do {
					float tempDelta = Time.realtimeSinceStartup;
					if (webcamImage.didUpdateThisFrame) {
						webcamImage.GetPixels32(m_pixels[i]);
						++i;
						goto nextLoop;
					}
					else {
						yield return null;
						#if UNITY_STANDALONE_WIN
						//yield return null;
						int waitCount = GetDelayCount(); //7000000;//[825000, 900000;
						for (int j = 0; j < waitCount; j++) {
							j = j + 1 - 1;
						}
						//yield return null;
						#endif

					}
					captureWindow -= (Time.realtimeSinceStartup - tempDelta);
				} while (captureWindow > 0.0f);
				
				//yield return new WaitSeconds(secPerFrame);
			} while (timeLeft > 0);

			kDelayIndex = Mathf.Min(kDelayIndex + 1, kDelayCounts.Length - 1);
			//Debug.Log("Failed to capture frame. Restarting camera and changing delayCount to " + kDelayCounts[kDelayIndex]);
			Text.Log("Failed to capture frame. Restarting camera and changing delayCount to {0}.", kDelayCounts[kDelayIndex]);
			webcamImage.Stop();

			yield return new WaitSeconds(secPerFrame);
			webcamImage.Play();
		}
		MedianFilterImage();
		yield return null;
		yield return Scheduler.StartCoroutine(BlurImage());
	}

	public static int kDelayIndex = 0;
	public static int[] kDelayCounts = new int[7]{500000, 1000000, 2000000, 4000000, 8000000, 12000000, 16000000};

	int GetDelayCount() {
		return kDelayCounts[kDelayIndex];
	}

	/// <summary>
	/// Creates a diff image for a new image.
	/// </summary>
	void CreateDiffImage() {
		Color32[] pixels = m_pixels[0];
		int size = pixels.Length;

		for (int i = 0; i < size; ++i) {
			Color calibPixel = calibrationImage[i];
			Color imgPixel = pixels[i];
			Vector3 diff = new Vector3(imgPixel.r - calibPixel.r,
			                           imgPixel.g - calibPixel.g,
			                           imgPixel.b - calibPixel.b);
			mostRecentImage[i] = (diff.sqrMagnitude >= kSqrPixelThreshold);

#if UNITY_EDITOR || DEBUG_SCANNING
			if (!mostRecentImage[i]) {
				pixels[i] = Color.magenta;
			}
#endif
		}
	}

	void MedianFilterImage() {
		Color32[] p0 = m_pixels[0];
		Color32[] p1 = m_pixels[1];
		Color32[] p2 = m_pixels[2];
		int size = p0.Length;

		for (int i = 0; i < size; ++i) {
			p0[i].r = Median(p0[i].r, p1[i].r, p2[i].r);
			p0[i].g = Median(p0[i].g, p1[i].g, p2[i].g);
			p0[i].b = Median(p0[i].b, p1[i].b, p2[i].b);
		}
	}

	byte Median(int i, int j, int k) {
		return (byte)((i + j + k) / 3);
	}

	//byte Median(int i, int j, int k) {
	//	return (byte)Mathf.Min(Mathf.Max(i, j), Mathf.Max(j, k), Mathf.Max(i, k));
	//}

	IEnumerator BlurImage() {
		Color32[] pixels = m_pixels[0];
		Matrix src = new Matrix(imageHeight, imageWidth, MatrixType.cv32FC3);
		Matrix dest = new Matrix(imageHeight, imageWidth, MatrixType.cv32FC3);

		for (int row = 0, i = 0; row < imageHeight; ++row) {
			for (int col = 0; col < imageWidth; ++col, ++i) {
				Color32 c = pixels[i];
				src[row, col, true] = new cvScalar(c.b, c.g, c.r);
			}
			yield return null;
		}

		for (int i = 0; i < 2; ++i) {
			OpenCV.cvSmooth(src.matPtr, dest.matPtr, OpenCV.CV_BILATERAL, 5, 5, 250.0, 250.0);
			yield return null;
			OpenCV.cvSmooth(dest.matPtr, src.matPtr, OpenCV.CV_BILATERAL, 5, 5, 250.0, 250.0);
			yield return null;
		}

		for (int row = 0, i = 0; row < imageHeight; ++row) {
			for (int col = 0; col < imageWidth; ++col, ++i) {
				cvScalar c = src[row, col, true];
				pixels[i].r = (byte)c.d2;
				pixels[i].g = (byte)c.d1;
				pixels[i].b = (byte)c.d0;
			}
			yield return null;
		}
	}
	#endregion

	#region Public calibration
	public IEnumerator CalibratePosition() {
		if (m_calibrationCornerPixels.Count != m_calibrationCornerWorldPoints.Count) {
			Debug.LogError("Need matching array/list sizes");
			yield break;
		}
		
		Vector2[] cornerPixels = m_calibrationCornerPixels.ToArray(); 
		Vector3[] tempCornerLocations = m_calibrationCornerWorldPoints.ToArray(); 
		
		yield return Scheduler.StartCoroutine(GetCameraPositionAndRotation(cornerPixels, tempCornerLocations));
		
		worldToCameraTranslation[1,0] = -worldToCameraTranslation[1,0];
		Matrix tempRot = Matrix.Identity(3,3);
		tempRot[1,1] = -1;
		worldToCameraRotation = worldToCameraRotation * tempRot;
		worldToCameraTranslation = tempRot * worldToCameraTranslation;
		OpenCV.cvReleaseMat(ref tempRot.matPtr);
		
		Text.Log("Calibrated camera from " + m_calibrationCornerPixels.Count + 
		         " points,\nWorld Position = \n" + worldPosition + " \nand Rotation = \n" 
		         + worldToCameraRotation.t().ToString());
	}

	public bool AddPointsFromChessboardPicture(float verticalOffset) {
		List<Vector2> aChessBoardSet = FindChessboardsInImage();
		if (aChessBoardSet.Count != 2 * kNumChessboardPoints) {
			Debug.LogError("Did not find the correct number of chessboard points");
			return false;
		}
		m_calibrationCornerPixels.AddRange(aChessBoardSet);

		List<Vector3> pointsToAdd = new List<Vector3>();
		for (int numChessBoards = 0; numChessBoards < 2; ++numChessBoards) {
			for (int v = 0; v < kCalibrationCornerLocations.Length; v++) {
				Vector3 aPoint = new Vector3(kCalibrationCornerLocations[v].x, 
				                             kCalibrationCornerLocations[v].y - verticalOffset, 
				                             kCalibrationCornerLocations[v].z);
				if (numChessBoards == 1) aPoint.x -= kCalibrationLeftSideOffset;
				pointsToAdd.Add(aPoint);
			}
		}

		m_calibrationCornerWorldPoints.AddRange(pointsToAdd);
		return true;
	}

	public List<Vector2> FindChessboardsInImage() {
		List<Vector2> cornerPixelPoints = new List<Vector2>();
		CvSize chessboardPatternSize = new CvSize();
		chessboardPatternSize.width = ScanningCamera.kChessboardWidth - 1;
		chessboardPatternSize.height = ScanningCamera.kChessboardHeight - 1;
		
		CvSize searchWindow = new CvSize();
		searchWindow.width = 15;
		searchWindow.height = 15;
		
		CvSize zeroZone = new CvSize();
		zeroZone.width = -1;
		zeroZone.height = -1;
		CvTermCriteria aCrit = new CvTermCriteria(3, 100, 0);
		
		CvPoint2D32f[] cornerArray = new CvPoint2D32f[(ScanningCamera.kChessboardHeight - 1) * (ScanningCamera.kChessboardWidth - 1)];
		GCHandle cornerArrayHandle = GCHandle.Alloc(cornerArray, GCHandleType.Pinned);
		System.IntPtr cornerPointer =  cornerArrayHandle.AddrOfPinnedObject();
		
		Matrix grayCvImage = new Matrix(imageHeight, imageWidth, MatrixType.cv8UC1);
		Matrix grayHalfScale = new Matrix(imageHeight / kSearchImageScaleFactor, 
		                                  imageWidth / kSearchImageScaleFactor, MatrixType.cv8UC1);
		
		FindCornerPixels(imageWidth / 2 + 1, imageWidth, 0, imageWidth / 2,
		                 grayCvImage, grayHalfScale, chessboardPatternSize,
		                 cornerPointer, cornerArray, searchWindow, zeroZone, aCrit, cornerPixelPoints, 0);

		FindCornerPixels(0, imageWidth / 2, imageWidth / 2 + 1, imageWidth,
		                 grayCvImage, grayHalfScale, chessboardPatternSize,
		                 cornerPointer, cornerArray, searchWindow, zeroZone, aCrit, cornerPixelPoints, 1);

		cornerArrayHandle.Free();

		return cornerPixelPoints;
	}
	#endregion

	#region Calibration support
	void FindCornerPixels(int clear0, int clear1, int gray0, int gray1, 
	                      Matrix grayCvImage, Matrix grayHalfScale, CvSize chessboardPatternSize,
	                      System.IntPtr cornerPointer, CvPoint2D32f[] cornerArray, CvSize searchWindow,
	                      CvSize zeroZone, CvTermCriteria aCrit, List<Vector2> cornerPixelPoints, int pass) 
	{
		// Clear pixels to white.
		for (int row = 0; row < imageHeight; ++row) {
			for (int col = clear0; col < clear1; ++col) {
				grayCvImage[row, col] = 255;
			}
		}

		// Convert pixels to grayscale.
		for (int row = 0; row < imageHeight; ++row) {
			for (int col = gray0; col < gray1; ++col) {
				Color c = m_pixels[0][(imageHeight - row - 1) * imageWidth + col];
				grayCvImage[row, col] = (int)((kGrayscaleRedWeight     * c.r
				                               + kGrayscaleGreenWeight * c.g
				                               + kGrayscaleBlueWeight  * c.b ) * 255.0f);
			}
		}

		// Create grayscale sample.
		for(int col = 0; col < imageWidth; col += kSearchImageScaleFactor) {
			for(int row = 0; row < imageHeight; row += kSearchImageScaleFactor) {
				grayHalfScale[row / kSearchImageScaleFactor, col / kSearchImageScaleFactor] = grayCvImage[row, col];
			}
		}
		
		
		int foundChessboard = OpenCV.cvFindChessboardCorners(grayHalfScale.matPtr, 
		                                                     chessboardPatternSize, 
		                                                     cornerPointer, 
		                                                     IntPtr.Zero, 1); 
		
		if (foundChessboard == 0) {
			return;
		}
		
		for (int i = 0; i < cornerArray.Length; i++) {  
			cornerArray[i].x *= kSearchImageScaleFactor;
			cornerArray[i].y *= kSearchImageScaleFactor;
		}
		OpenCV.cvFindCornerSubPix(grayCvImage.matPtr, cornerPointer, 
		                          (kChessboardWidth - 1) * (kChessboardHeight - 1),
		                          searchWindow,
		                          zeroZone,
		                          aCrit);
		Array.Sort(cornerArray, SortCvPointByYthenX);
		foreach (CvPoint2D32f cvp in cornerArray) {
			if (cvp.x < 2.0f || cvp.y < 2.0f) continue;
			cornerPixelPoints.Add(new Vector2(cvp.x, cvp.y));
		}

		if (cornerPixelPoints.Count != kNumChessboardPoints * (pass + 1)) {
			Text.Error("Found {0} point{1}; expected {2}.", cornerPixelPoints.Count,
			           Text.S(cornerPixelPoints.Count), kNumChessboardPoints);
		}
	}
	
	const int kPlusMinusY = 5;
	int SortCvPointByYthenX(CvPoint2D32f a, CvPoint2D32f b) {
		float deltaY = Mathf.Abs(a.y - b.y);
		if (deltaY < kPlusMinusY) {
			if (a.x < b.x) return -1;
			if (a.x > b.x) return 1;
		}
		else if (a.y < b.y) return -1;
		else if (a.y > b.y) return 1;
		return 0;
	}

	IEnumerator GetCameraPositionAndRotation(Vector2[] imgPoints, Vector3[] spatialPoints) {
		if (imgPoints.Length != spatialPoints.Length || 
		    imgPoints.Length < 2 * kNumChessboardPoints) {
			throw new Exception("imgPoints must be the same size as spatialPoints and must be at least " +
			                    (2 * kNumChessboardPoints) + " long"); 
		}

		int numberOfPictures = Mathf.RoundToInt((float)kNumberOfPicturesPerCameraMm / kCalibrationVertMoveMm); 

		if (Scheduler.ShouldYield()) yield return null;
		int numPointsPerPic = imgPoints.Length / numberOfPictures;
		Matrix pointCount = Matrix.InitToValue(numberOfPictures, 1, numPointsPerPic, MatrixType.cv32SC1); 
		OpenCV.ReorderPoints(spatialPoints, imgPoints, numberOfPictures, out spatialPoints, out imgPoints);
		// Need to specify the image size
		CvSize imgSize = new CvSize();
		imgSize.height = imageHeight;
		imgSize.width = imageWidth;
		
		// Convert point arrays to CvMat's so that they can be passed to the calibration function
		Matrix imagePoints = new Matrix(imgPoints.Length, 2, MatrixType.cv32FC1);
		Matrix worldPoints = new Matrix(imgPoints.Length, 3, MatrixType.cv32FC1);
		for (int i = 0; i < imgPoints.Length; ++i) {
			imagePoints[i, 0] = imgPoints[i].x;
			imagePoints[i, 1] = imgPoints[i].y;
			
			worldPoints[i, 0] = spatialPoints[i].x;
			worldPoints[i, 1] = spatialPoints[i].y;
			worldPoints[i, 2] = spatialPoints[i].z;
		}
		if (Scheduler.ShouldYield()) yield return null;
		
		// Setup rotation and translation storage... not currently used but might be useful in the future
		Matrix rotationVectors = new Matrix(numberOfPictures, 3, MatrixType.cv64FC1); 
		Matrix translationVectors = new Matrix(numberOfPictures, 3, MatrixType.cv64FC1); 
		
		// Find actual camera intrinsics and distortion
		// First pass, fix focal length
		double calibrationError = OpenCV.cvCalibrateCamera2(worldPoints.matPtr, imagePoints.matPtr, 
		                                                    pointCount.matPtr, imgSize, intrinsics.matPtr, distortion.matPtr, 
		                                                    rotationVectors.matPtr, translationVectors.matPtr,
		                                                    OpenCV.CV_CALIB_FIX_ASPECT_RATIO 
		                                                    + OpenCV.CV_CALIB_USE_INTRINSIC_GUESS  
		                                                    // + OpenCV.CV_CALIB_FIX_PRINCIPAL_POINT
		                                                    //	+ OpenCV.CV_CALIB_ZERO_TANGENT_DIST 
		                                                    + OpenCV.CV_CALIB_FIX_FOCAL_LENGTH  
		                                                    //	+ OpenCV.CV_CALIB_FIX_K1 
		                                                    //	+ OpenCV.CV_CALIB_FIX_K2  
		                                                    + OpenCV.CV_CALIB_FIX_K3  
		                                                    //	+ OpenCV.CV_CALIB_FIX_K4  
		                                                    //	+ OpenCV.CV_CALIB_FIX_K5  
		                                                    //	+ OpenCV.CV_CALIB_FIX_K6 
		                                                    );
		Text.Log("Calibrated camera in first pass : {0} \n Intrinsics : \n{1} \nWith calibration error : \n{2}", 
		         cameraDevice.name, intrinsics.ToString(), calibrationError);
		if (Scheduler.ShouldYield()) yield return null;
		// Second pass, fix principal point
		calibrationError = OpenCV.cvCalibrateCamera2(worldPoints.matPtr, imagePoints.matPtr, 
                                                    pointCount.matPtr, imgSize, intrinsics.matPtr, distortion.matPtr, 
		                                             rotationVectors.matPtr, translationVectors.matPtr,
		                                             OpenCV.CV_CALIB_FIX_ASPECT_RATIO 
		                                             + OpenCV.CV_CALIB_USE_INTRINSIC_GUESS  
		                                             + OpenCV.CV_CALIB_FIX_PRINCIPAL_POINT
		                                             //	+ OpenCV.CV_CALIB_ZERO_TANGENT_DIST 
		                                             // + OpenCV.CV_CALIB_FIX_FOCAL_LENGTH  
		                                             //	+ OpenCV.CV_CALIB_FIX_K1 
		                                             //	+ OpenCV.CV_CALIB_FIX_K2  
		                                             + OpenCV.CV_CALIB_FIX_K3  
		                                             //	+ OpenCV.CV_CALIB_FIX_K4  
		                                             //	+ OpenCV.CV_CALIB_FIX_K5  
		                                             //	+ OpenCV.CV_CALIB_FIX_K6 
		                                             );
		Text.Log("Calibrated camera in second pass : {0} \n Intrinsics : \n{1} \nWith calibration error : \n{2}", 
		         cameraDevice.name, intrinsics.ToString(), calibrationError);
		if (Scheduler.ShouldYield()) yield return null;
		// Third pass, fix principal point & focal distance, but not Aspect Ratio
		calibrationError = OpenCV.cvCalibrateCamera2(worldPoints.matPtr, imagePoints.matPtr, 
		                                             pointCount.matPtr, imgSize, intrinsics.matPtr, distortion.matPtr, 
		                                             rotationVectors.matPtr, translationVectors.matPtr,
		                                             //OpenCV.CV_CALIB_FIX_ASPECT_RATIO 
		                                             OpenCV.CV_CALIB_USE_INTRINSIC_GUESS  
		                                             + OpenCV.CV_CALIB_FIX_PRINCIPAL_POINT
		                                             //	+ OpenCV.CV_CALIB_ZERO_TANGENT_DIST 
		                                             + OpenCV.CV_CALIB_FIX_FOCAL_LENGTH  
		                                             //	+ OpenCV.CV_CALIB_FIX_K1 
		                                             //	+ OpenCV.CV_CALIB_FIX_K2  
		                                             + OpenCV.CV_CALIB_FIX_K3  
		                                             //	+ OpenCV.CV_CALIB_FIX_K4  
		                                             //	+ OpenCV.CV_CALIB_FIX_K5  
		                                             //	+ OpenCV.CV_CALIB_FIX_K6 
		                                             );
		Text.Log("Calibrated camera in third pass : {0} \n Intrinsics : \n{1} \nWith calibration error : \n{2}", 
		         cameraDevice.name, intrinsics.ToString(), calibrationError);
		if (Scheduler.ShouldYield()) yield return null;
		// Fourth pass, redo first pass
		calibrationError = OpenCV.cvCalibrateCamera2(worldPoints.matPtr, imagePoints.matPtr, 
		                                                    pointCount.matPtr, imgSize, intrinsics.matPtr, distortion.matPtr, 
		                                                    rotationVectors.matPtr, translationVectors.matPtr,
		                                                    OpenCV.CV_CALIB_FIX_ASPECT_RATIO 
		                                                    + OpenCV.CV_CALIB_USE_INTRINSIC_GUESS  
		                                                    // + OpenCV.CV_CALIB_FIX_PRINCIPAL_POINT
		                                                    //	+ OpenCV.CV_CALIB_ZERO_TANGENT_DIST 
		                                                    + OpenCV.CV_CALIB_FIX_FOCAL_LENGTH  
		                                                    //	+ OpenCV.CV_CALIB_FIX_K1 
		                                                    //	+ OpenCV.CV_CALIB_FIX_K2  
		                                                    + OpenCV.CV_CALIB_FIX_K3  
		                                                    //	+ OpenCV.CV_CALIB_FIX_K4  
		                                                    //	+ OpenCV.CV_CALIB_FIX_K5  
		                                                    //	+ OpenCV.CV_CALIB_FIX_K6 
		                                                    );
		Text.Log("Calibrated camera in fourth pass : {0} \n Intrinsics : \n{1} \nWith calibration error : \n{2}", 
		         cameraDevice.name, intrinsics.ToString(), calibrationError);
		if (Scheduler.ShouldYield()) yield return null;
		/// Initialize translation/rotation vectors to be populated by OpenCV function
		Matrix worldToCamTranslation = Matrix.Zero(3,1);
		Matrix worldToCamRotation = Matrix.Zero(3,1); 
		
		/// Call the OpenCV function that calculates the Camera Extrinsics 
		/// matrix from 2-D + 3-D point correspondence 
		OpenCV.cvFindExtrinsicCameraParams2(worldPoints.matPtr, imagePoints.matPtr, 
		                             intrinsics.matPtr, IntPtr.Zero, worldToCamRotation.matPtr, 
		                             worldToCamTranslation.matPtr, 0);
		
		if (Scheduler.ShouldYield()) yield return null;
		
		/// This converts from mm to voxel coordinates...
		Text.Log("Translation before mm-to-voxel conversion: {0}", 
		         worldToCamTranslation.ToString());
		for (int i = 0; i < 3; ++i)  { 
			worldToCamTranslation[i,0] = worldToCamTranslation[i,0] / VoxelBlob.kVoxelSizeInMm;
		}
		Text.Log("Translation after mm-to-voxel conversion: {0}", 
		         worldToCamTranslation.ToString());
		/// End conversion
		
		/// Initialize and populate the 3x3 version of the world-to-camera rotation matrix
		Matrix worldToCamFullMatrix = new Matrix(3, 3);
		OpenCV.cvRodrigues2(worldToCamRotation.matPtr, worldToCamFullMatrix.matPtr, IntPtr.Zero);
		
		worldToCameraTranslation = worldToCamTranslation;
		worldToCameraRotation = worldToCamFullMatrix;
	}
	#endregion

	#region Debugging
	[System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEBUG_SCANNING")]
	void WritePixels(string id) {
		string path = string.Format("{0}/{1}.png", Application.temporaryCachePath, id);
		Texture2D tex = new Texture2D(imageWidth, imageHeight);
		tex.SetPixels32(m_pixels[0]);
		byte[] bytes = tex.EncodeToPNG();

		Text.Log("Saving image (size w, h, bytes = " + imageWidth + ", " + 
		         imageHeight+ ", " + bytes.Length + ") : {0}", path);

		try
		{
			System.IO.File.WriteAllBytes(path, bytes);
		}
		catch (System.IO.IOException e)
		{
			Text.Error("File.WriteAllBytes error : " + e.ToString());
		}
	}

	public override string ToString() {
		return string.Format("[ScanningCamera: intrinsics={0}, translation={1}, rotation={2}]", 
		                     intrinsics.ToString(),
		                     worldToCameraTranslation.ToString(),
		                     worldToCameraRotation.ToString());
	}
	#endregion

	#region Checkerboard values
	public const float kCalibrationLeftSideOffset = 100f;
	public Vector3[] kCalibrationCornerLocations = new Vector3[36] //81] 
	{ // mm coords
		new Vector3(70,			20.2f,		74.700000f),
		new Vector3(65,			20.2f,		74.700000f),  
		new Vector3(60,			20.2f,		74.700000f), 
		new Vector3(55,			20.2f,		74.700000f), 
		new Vector3(50,			20.2f,		74.700000f), 
		new Vector3(45,			20.2f,		74.700000f),
		new Vector3(40,			20.2f,		74.700000f), 
		new Vector3(35,			20.2f,		74.700000f), 
		new Vector3(30,			20.2f,		74.700000f), 

		new Vector3(70,			25.2f,		74.700000f),
		new Vector3(65,			25.2f,		74.700000f),  
		new Vector3(60,			25.2f,		74.700000f), 
		new Vector3(55,			25.2f,		74.700000f), 
		new Vector3(50,			25.2f,		74.700000f), 
		new Vector3(45,			25.2f,		74.700000f),
		new Vector3(40,			25.2f,		74.700000f), 
		new Vector3(35,			25.2f,		74.700000f), 
		new Vector3(30,			25.2f,		74.700000f), 

		new Vector3(70,			30.2f,		74.700000f),
		new Vector3(65,			30.2f,		74.700000f),  
		new Vector3(60,			30.2f,		74.700000f), 
		new Vector3(55,			30.2f,		74.700000f), 
		new Vector3(50,			30.2f,		74.700000f), 
		new Vector3(45,			30.2f,		74.700000f),
		new Vector3(40,			30.2f,		74.700000f), 
		new Vector3(35,			30.2f,		74.700000f), 
		new Vector3(30,			30.2f,		74.700000f), 

		new Vector3(70,			35.2f,		74.700000f),
		new Vector3(65,			35.2f,		74.700000f),  
		new Vector3(60,			35.2f,		74.700000f), 
		new Vector3(55,			35.2f,		74.700000f), 
		new Vector3(50,			35.2f,		74.700000f), 
		new Vector3(45,			35.2f,		74.700000f),
		new Vector3(40,			35.2f,		74.700000f), 
		new Vector3(35,			35.2f,		74.700000f), 
		new Vector3(30,			35.2f,		74.700000f) /*, 

		new Vector3(70,			40.2f,		74.700000f),
		new Vector3(65,			40.2f,		74.700000f),  
		new Vector3(60,			40.2f,		74.700000f), 
		new Vector3(55,			40.2f,		74.700000f), 
		new Vector3(50,			40.2f,		74.700000f), 
		new Vector3(45,			40.2f,		74.700000f),
		new Vector3(40,			40.2f,		74.700000f), 
		new Vector3(35,			40.2f,		74.700000f), 
		new Vector3(30,			40.2f,		74.700000f), 

		new Vector3(70,			45.2f,		74.700000f),
		new Vector3(65,			45.2f,		74.700000f),  
		new Vector3(60,			45.2f,		74.700000f), 
		new Vector3(55,			45.2f,		74.700000f), 
		new Vector3(50,			45.2f,		74.700000f), 
		new Vector3(45,			45.2f,		74.700000f),
		new Vector3(40,			45.2f,		74.700000f), 
		new Vector3(35,			45.2f,		74.700000f), 
		new Vector3(30,			45.2f,		74.700000f), 

		new Vector3(70,			50.2f,		74.700000f),
		new Vector3(65,			50.2f,		74.700000f),  
		new Vector3(60,			50.2f,		74.700000f), 
		new Vector3(55,			50.2f,		74.700000f), 
		new Vector3(50,			50.2f,		74.700000f), 
		new Vector3(45,			50.2f,		74.700000f),
		new Vector3(40,			50.2f,		74.700000f), 
		new Vector3(35,			50.2f,		74.700000f), 
		new Vector3(30,			50.2f,		74.700000f), 

		new Vector3(70,			55.2f,		74.700000f),
		new Vector3(65,			55.2f,		74.700000f),  
		new Vector3(60,			55.2f,		74.700000f), 
		new Vector3(55,			55.2f,		74.700000f), 
		new Vector3(50,			55.2f,		74.700000f), 
		new Vector3(45,			55.2f,		74.700000f),
		new Vector3(40,			55.2f,		74.700000f), 
		new Vector3(35,			55.2f,		74.700000f), 
		new Vector3(30,			55.2f,		74.700000f), 

		new Vector3(70,			60.2f,		74.700000f),
		new Vector3(65,			60.2f,		74.700000f),  
		new Vector3(60,			60.2f,		74.700000f), 
		new Vector3(55,			60.2f,		74.700000f), 
		new Vector3(50,			60.2f,		74.700000f), 
		new Vector3(45,			60.2f,		74.700000f),
		new Vector3(40,			60.2f,		74.700000f), 
		new Vector3(35,			60.2f,		74.700000f), 
		new Vector3(30,			60.2f,		74.700000f), */
	};
	#endregion
#endif
}
