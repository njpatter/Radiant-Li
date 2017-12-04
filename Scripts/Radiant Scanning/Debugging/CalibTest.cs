using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections;
using System.Runtime.InteropServices;

public class CalibTest : MonoBehaviour {
	#if !UNITY_IOS && !UNITY_ANDROID
	private int[] numPicOptions = new int[1]{14}; //new int[7]{1, 7, 14, 28, 33, 66, 132};
	
	IEnumerator Start() {
		
		foreach(int i in numPicOptions) {
			Debug.Log("Number of pictures being used : " + i);
			float initialTime = Time.realtimeSinceStartup;
			CalibrateCamera(i);
			Debug.Log(i + " pictures took " + (Time.realtimeSinceStartup - initialTime) + " seconds");
			yield return null;
			
		}
		Debug.Log("Finished!");
	}
	
	// Use this for initialization
	void CalibrateCamera (int numPics) {
		ScanDataSaver sds = new ScanDataSaver();
		List<Vector3> wPoints;
		List<Vector2> imgPoints;
		sds.Load(out wPoints, out imgPoints);
		for(int i = 0; i < wPoints.Count; i++) {
			Vector3 temp = wPoints[i];
			//temp.z = 0;
			wPoints[i] = temp;
		} 
		OpenCV.Init();
		
		int numPointsPerPic = imgPoints.Count / numPics;
		Matrix pointCount = Matrix.InitToValue(numPics, 1, numPointsPerPic, MatrixType.cv32SC1); 
		
		CvSize imgSize = new CvSize();
		imgSize.height = 720;
		imgSize.width = 1280;
		
		Vector3[] testWorld;
		Vector2[] testImage;
		OpenCV.ReorderPoints(wPoints.ToArray(), imgPoints.ToArray(), numPics, out testWorld, out testImage);
		wPoints = new List<Vector3>(testWorld);
		imgPoints = new List<Vector2>(testImage);
		
		Matrix worldC3 = OpenCV.ConvertToMultiChannelMatrix(wPoints.ToArray());
		Matrix imageC2 = OpenCV.ConvertToMultiChannelMatrix(imgPoints.ToArray());
		
		Matrix cameraMatrixOne = Matrix.Identity(3,3,MatrixType.cv64FC1);
		cameraMatrixOne[0, 0] = 1140; //1141.84184f;
		cameraMatrixOne[0, 2] = 583.56836f; 
		cameraMatrixOne[1, 1] = 1140; //1139.50203f;
		cameraMatrixOne[1, 2] = 436.87757f;
		cameraMatrixOne[2, 2] = 1f;
		
		/*public static extern void cvInitIntrinsicParams2D(
		IntPtr object_points, 
		IntPtr image_points, 
		IntPtr npoints, 
		CvSize image_size,
		IntPtr camera_matrix,
		double aspect_ratio);*/
		OpenCV.cvInitIntrinsicParams2D(worldC3.matPtr, imageC2.matPtr, pointCount.matPtr, 
			imgSize, cameraMatrixOne.matPtr, 1);
		Debug.Log("Camera matrix from InitIntrinsicParams2d = \n" + cameraMatrixOne.ToString());
		
		
		
		Matrix cameraMatrixTwo = Matrix.Identity(3,3, MatrixType.cv32FC1); // Matrix.kCV_32FC1);
		cameraMatrixTwo[0, 0] = 1170; //1141.84184f;
		cameraMatrixTwo[0, 2] = 640; 
		cameraMatrixTwo[1, 1] = 1170; //1139.50203f;
		cameraMatrixTwo[1, 2] = 360;
		cameraMatrixTwo[2, 2] = 1f;
		//Matrix cameraMatrixTwo = cameraMatrixOne.Copy();
		
		Matrix imagePoints = new Matrix(imgPoints.Count, 2, MatrixType.cv32FC1); //Matrix.kCV_32FC1);
		Matrix worldPoints = new Matrix(imgPoints.Count, 3, MatrixType.cv32FC1); //Matrix.kCV_32FC1);
		for (int i = 0; i < imgPoints.Count; ++i) {
			imagePoints[i, 0] = imgPoints[i].x;
			imagePoints[i, 1] = imgPoints[i].y;
			
			worldPoints[i, 0] = wPoints[i].x;
			worldPoints[i, 1] = wPoints[i].y;
			worldPoints[i, 2] = wPoints[i].z;
		}
		
		Matrix worldToCamTranslation = Matrix.Zero(3,1, MatrixType.cv32FC1); //Matrix.kCV_32FC1);
		Matrix worldToCamRotation = Matrix.Zero(3,1, MatrixType.cv32FC1); //Matrix.kCV_32FC1); 
		
		
		
		
		Matrix distortion = Matrix.Zero(5,1, MatrixType.cv64FC1);
		Matrix rotationVectors = new Matrix(numPics, 3, MatrixType.cv64FC1); 
		Matrix translationVectors = new Matrix(numPics, 3, MatrixType.cv64FC1); 
		
		/*cvFindExtrinsicCameraParams2(const CvMat* object_points, const CvMat* image_points, 
		  const CvMat* camera_matrix, const CvMat* distortion_coeffs, CvMat* rotation_vector, 
		  CvMat* translation_vector, int use_extrinsic_guess=0 )*/
		
		
		//cameraMatrixTwo[0,0] = 1;
		//cameraMatrixTwo[1,1] = 1;
		double testError = OpenCV.cvCalibrateCamera2(worldPoints.matPtr, imagePoints.matPtr, pointCount.matPtr, 
			imgSize, cameraMatrixTwo.matPtr, distortion.matPtr, rotationVectors.matPtr, translationVectors.matPtr
			,
			OpenCV.CV_CALIB_FIX_ASPECT_RATIO 
			+ OpenCV.CV_CALIB_USE_INTRINSIC_GUESS  
		//	+ OpenCV.CV_CALIB_ZERO_TANGENT_DIST 
			+ OpenCV.CV_CALIB_FIX_FOCAL_LENGTH  
		//	+ OpenCV.CV_CALIB_FIX_K1 
		//	+ OpenCV.CV_CALIB_FIX_K2  
			+ OpenCV.CV_CALIB_FIX_K3  
		//	+ OpenCV.CV_CALIB_FIX_K4  
		//	+ OpenCV.CV_CALIB_FIX_K5  
		//	+ OpenCV.CV_CALIB_FIX_K6 
			); 
		Debug.Log(rotationVectors.ToString());
		Debug.Log(translationVectors.ToString());
		Debug.Log("Camera Matrix from CalibrateCamera2 with error " + testError + "\n" + cameraMatrixTwo.ToString() +
			"\n with distortion = \n" + distortion.ToString() );
		
		/*testError = OpenCV.cvCalibrateCamera2(worldPoints.matPtr, imagePoints.matPtr, pointCount.matPtr, 
			imgSize, cameraMatrixTwo.matPtr, distortion.matPtr, IntPtr.Zero, IntPtr.Zero// testTransVecs.matPtr
			,
			//OpenCV.CV_CALIB_FIX_ASPECT_RATIO 
			//+ 
			OpenCV.CV_CALIB_USE_INTRINSIC_GUESS  
		//	+ OpenCV.CV_CALIB_ZERO_TANGENT_DIST 
		//	+ OpenCV.CV_CALIB_FIX_FOCAL_LENGTH  
			+ OpenCV.CV_CALIB_FIX_PRINCIPAL_POINT
		//	+ OpenCV.CV_CALIB_FIX_K1 
		//	+ OpenCV.CV_CALIB_FIX_K2  
			+ OpenCV.CV_CALIB_FIX_K3  
		//	+ OpenCV.CV_CALIB_FIX_K4  
		//	+ OpenCV.CV_CALIB_FIX_K5  
		//	+ OpenCV.CV_CALIB_FIX_K6 
			); 
		Debug.Log("Iterated Camera Matrix from CalibrateCamera2 with error " + testError + "\n" + cameraMatrixTwo.ToString() +
			"\n with distortion = \n" + distortion.ToString() );
		*/
		/// Call the OpenCV function that calculates the Camera Extrinsics 
		/// matrix from 2-D + 3-D point correspondence  //Matrix.kCV_32FC1);
		OpenCV.cvFindExtrinsicCameraParams2(worldPoints.matPtr, imagePoints.matPtr, 
			cameraMatrixTwo.matPtr, distortion.matPtr, worldToCamRotation.matPtr, 
			worldToCamTranslation.matPtr, 0);
		
		Debug.Log("Resulting Translation : \n" + worldToCamTranslation.ToString());
		
		Matrix fullRot = Matrix.Zero(3,3,MatrixType.cv32FC1);
		OpenCV.cvRodrigues2(worldToCamRotation.matPtr, fullRot.matPtr, IntPtr.Zero);
		Vector3 newPos = -(fullRot.t() * worldToCamTranslation).ToVector3();
		Debug.Log("New camera position " + newPos);
	}
	
	/*
	public Vector3[] wPoints = new Vector3[176]{
		new Vector3(-75f, 5f, 74.7f), //74.7f was previously 74.7f
		new Vector3(-60f, -7f, 74.7f), 
		new Vector3(-45f, 5f, 74.7f), 
		new Vector3(-30f, -7f, 74.7f), 
		new Vector3(0f, -1f, 74.7f), 
		new Vector3(45f, 5f, 74.7f), 
		new Vector3(60f, -7f, 74.7f), 
		new Vector3(75f, 5f, 74.7f), 
		new Vector3(-75f, 4f, 74.7f), 
		new Vector3(-60f, -8f, 74.7f), 
		new Vector3(-45f, 4f, 74.7f), 
		new Vector3(-30f, -8f, 74.7f), 
		new Vector3(0f, -2f, 74.7f), 
		new Vector3(45f, 4f, 74.7f), 
		new Vector3(60f, -8f, 74.7f), 
		new Vector3(75f, 4f, 74.7f), 
		new Vector3(-75f, 3f, 74.7f), 
		new Vector3(-60f, -9f, 74.7f), 
		new Vector3(-45f, 3f, 74.7f), 
		new Vector3(-30f, -9f, 74.7f), 
		new Vector3(0f, -3f, 74.7f), 
		new Vector3(45f, 3f, 74.7f), 
		new Vector3(60f, -9f, 74.7f), 
		new Vector3(75f, 3f, 74.7f), 
		new Vector3(-75f, 1f, 74.7f), 
		new Vector3(-60f, -11f, 74.7f), 
		new Vector3(-45f, 1f, 74.7f), 
		new Vector3(-30f, -11f, 74.7f), 
		new Vector3(0f, -5f, 74.7f), 
		new Vector3(45f, 1f, 74.7f), 
		new Vector3(60f, -11f, 74.7f), 
		new Vector3(75f, 1f, 74.7f), 
		new Vector3(-75f, -1f, 74.7f), 
		new Vector3(-60f, -13f, 74.7f), 
		new Vector3(-45f, -1f, 74.7f), 
		new Vector3(-30f, -13f, 74.7f), 
		new Vector3(0f, -7f, 74.7f), 
		new Vector3(45f, -1f, 74.7f), 
		new Vector3(60f, -13f, 74.7f), 
		new Vector3(75f, -1f, 74.7f), 
		new Vector3(-75f, -4f, 74.7f), 
		new Vector3(-60f, -16f, 74.7f), 
		new Vector3(-45f, -4f, 74.7f), 
		new Vector3(-30f, -16f, 74.7f), 
		new Vector3(0f, -10f, 74.7f), 
		new Vector3(45f, -4f, 74.7f), 
		new Vector3(60f, -16f, 74.7f), 
		new Vector3(75f, -4f, 74.7f), 
		new Vector3(-75f, -5f, 74.7f), 
		new Vector3(-60f, -17f, 74.7f), 
		new Vector3(-45f, -5f, 74.7f), 
		new Vector3(-30f, -17f, 74.7f), 
		new Vector3(0f, -11f, 74.7f), 
		new Vector3(45f, -5f, 74.7f), 
		new Vector3(60f, -17f, 74.7f), 
		new Vector3(75f, -5f, 74.7f), 
		new Vector3(-75f, -7f, 74.7f), 
		new Vector3(-60f, -19f, 74.7f), 
		new Vector3(-45f, -7f, 74.7f), 
		new Vector3(-30f, -19f, 74.7f), 
		new Vector3(0f, -13f, 74.7f), 
		new Vector3(45f, -7f, 74.7f), 
		new Vector3(60f, -19f, 74.7f), 
		new Vector3(75f, -7f, 74.7f), 
		new Vector3(-75f, -8f, 74.7f), 
		new Vector3(-60f, -20f, 74.7f), 
		new Vector3(-45f, -8f, 74.7f), 
		new Vector3(-30f, -20f, 74.7f), 
		new Vector3(0f, -14f, 74.7f), 
		new Vector3(45f, -8f, 74.7f), 
		new Vector3(60f, -20f, 74.7f), 
		new Vector3(75f, -8f, 74.7f), 
		new Vector3(-75f, -9f, 74.7f), 
		new Vector3(-60f, -21f, 74.7f), 
		new Vector3(-45f, -9f, 74.7f), 
		new Vector3(-30f, -21f, 74.7f), 
		new Vector3(0f, -15f, 74.7f), 
		new Vector3(45f, -9f, 74.7f), 
		new Vector3(60f, -21f, 74.7f), 
		new Vector3(75f, -9f, 74.7f), 
		new Vector3(-75f, -10f, 74.7f), 
		new Vector3(-60f, -22f, 74.7f), 
		new Vector3(-45f, -10f, 74.7f), 
		new Vector3(-30f, -22f, 74.7f), 
		new Vector3(0f, -16f, 74.7f), 
		new Vector3(45f, -10f, 74.7f), 
		new Vector3(60f, -22f, 74.7f), 
		new Vector3(75f, -10f, 74.7f), 
		new Vector3(-75f, -12f, 74.7f), 
		new Vector3(-60f, -24f, 74.7f), 
		new Vector3(-45f, -12f, 74.7f), 
		new Vector3(-30f, -24f, 74.7f), 
		new Vector3(0f, -18f, 74.7f), 
		new Vector3(45f, -12f, 74.7f), 
		new Vector3(60f, -24f, 74.7f), 
		new Vector3(75f, -12f, 74.7f), 
		new Vector3(-75f, -13f, 74.7f), 
		new Vector3(-60f, -25f, 74.7f), 
		new Vector3(-45f, -13f, 74.7f), 
		new Vector3(-30f, -25f, 74.7f), 
		new Vector3(0f, -19f, 74.7f), 
		new Vector3(45f, -13f, 74.7f), 
		new Vector3(60f, -25f, 74.7f), 
		new Vector3(75f, -13f, 74.7f), 
		new Vector3(-75f, -14f, 74.7f), 
		new Vector3(-60f, -26f, 74.7f), 
		new Vector3(-45f, -14f, 74.7f), 
		new Vector3(-30f, -26f, 74.7f), 
		new Vector3(0f, -20f, 74.7f), 
		new Vector3(45f, -14f, 74.7f), 
		new Vector3(60f, -26f, 74.7f), 
		new Vector3(75f, -14f, 74.7f), 
		new Vector3(-75f, -15f, 74.7f), 
		new Vector3(-60f, -27f, 74.7f), 
		new Vector3(-45f, -15f, 74.7f), 
		new Vector3(-30f, -27f, 74.7f), 
		new Vector3(0f, -21f, 74.7f), 
		new Vector3(45f, -15f, 74.7f), 
		new Vector3(60f, -27f, 74.7f), 
		new Vector3(75f, -15f, 74.7f), 
		new Vector3(-75f, -16f, 74.7f), 
		new Vector3(-60f, -28f, 74.7f), 
		new Vector3(-45f, -16f, 74.7f), 
		new Vector3(-30f, -28f, 74.7f), 
		new Vector3(0f, -22f, 74.7f), 
		new Vector3(45f, -16f, 74.7f), 
		new Vector3(60f, -28f, 74.7f), 
		new Vector3(75f, -16f, 74.7f), 
		new Vector3(-75f, -17f, 74.7f), 
		new Vector3(-60f, -29f, 74.7f), 
		new Vector3(-45f, -17f, 74.7f), 
		new Vector3(-30f, -29f, 74.7f), 
		new Vector3(0f, -23f, 74.7f), 
		new Vector3(45f, -17f, 74.7f), 
		new Vector3(60f, -29f, 74.7f), 
		new Vector3(75f, -17f, 74.7f), 
		new Vector3(-75f, -18f, 74.7f), 
		new Vector3(-60f, -30f, 74.7f), 
		new Vector3(-45f, -18f, 74.7f), 
		new Vector3(-30f, -30f, 74.7f), 
		new Vector3(0f, -24f, 74.7f), 
		new Vector3(45f, -18f, 74.7f), 
		new Vector3(60f, -30f, 74.7f), 
		new Vector3(75f, -18f, 74.7f), 
		new Vector3(-75f, -19f, 74.7f), 
		new Vector3(-60f, -31f, 74.7f), 
		new Vector3(-45f, -19f, 74.7f), 
		new Vector3(-30f, -31f, 74.7f), 
		new Vector3(0f, -25f, 74.7f), 
		new Vector3(45f, -19f, 74.7f), 
		new Vector3(60f, -31f, 74.7f), 
		new Vector3(75f, -19f, 74.7f), 
		new Vector3(-75f, -20f, 74.7f), 
		new Vector3(-60f, -32f, 74.7f), 
		new Vector3(-45f, -20f, 74.7f), 
		new Vector3(-30f, -32f, 74.7f), 
		new Vector3(0f, -26f, 74.7f), 
		new Vector3(45f, -20f, 74.7f), 
		new Vector3(60f, -32f, 74.7f), 
		new Vector3(75f, -20f, 74.7f), 
		new Vector3(-75f, -21f, 74.7f), 
		new Vector3(-60f, -33f, 74.7f), 
		new Vector3(-45f, -21f, 74.7f), 
		new Vector3(-30f, -33f, 74.7f), 
		new Vector3(0f, -27f, 74.7f), 
		new Vector3(45f, -21f, 74.7f), 
		new Vector3(60f, -33f, 74.7f), 
		new Vector3(75f, -21f, 74.7f), 
		new Vector3(-75f, -22f, 74.7f), 
		new Vector3(-60f, -34f, 74.7f), 
		new Vector3(-45f, -22f, 74.7f), 
		new Vector3(-30f, -34f, 74.7f), 
		new Vector3(0f, -28f, 74.7f), 
		new Vector3(45f, -22f, 74.7f), 
		new Vector3(60f, -34f, 74.7f), 
		new Vector3(75f, -22f, 74.7f)};
	
	public Vector2[] imgPoints = new Vector2[176]{
		new Vector2(73.5f, 91.5f), 
		new Vector2(183.5f, 173.5f), 
		new Vector2(278.5f, 88.5f), 
		new Vector2(388.5f, 171.5f), 
		new Vector2(591.5f, 129.5f), 
		new Vector2(905.5f, 81.5f), 
		new Vector2(1005.5f, 169.5f), 
		new Vector2(1110.5f, 86.5f), 
		new Vector2(74.5f, 98.5f), 
		new Vector2(184.5f, 180.5f), 
		new Vector2(279.5f, 95.5f), 
		new Vector2(387.5f, 178.5f), 
		new Vector2(592.5f, 134.5f), 
		new Vector2(905.5f, 90.5f), 
		new Vector2(1003.5f, 174.5f), 
		new Vector2(1113.5f, 91.5f), 
		new Vector2(73.5f, 106.5f), 
		new Vector2(183.5f, 186.5f), 
		new Vector2(280.5f, 102.5f), 
		new Vector2(386.5f, 185.5f), 
		new Vector2(592.5f, 141.5f), 
		new Vector2(907.5f, 98.5f), 
		new Vector2(1003.5f, 183.5f), 
		new Vector2(1113.5f, 96.5f), 
		new Vector2(76.5f, 119.5f), 
		new Vector2(184.5f, 200.5f), 
		new Vector2(281.5f, 114.5f), 
		new Vector2(389.5f, 199.5f), 
		new Vector2(592.5f, 156.5f), 
		new Vector2(906.5f, 113.5f), 
		new Vector2(1005.5f, 198.5f), 
		new Vector2(1108.5f, 114.5f), 
		new Vector2(77.5f, 132.5f), 
		new Vector2(186.5f, 213.5f), 
		new Vector2(282.5f, 128.5f), 
		new Vector2(388.5f, 212.5f), 
		new Vector2(593.5f, 167.5f), 
		new Vector2(906.5f, 125.5f), 
		new Vector2(1001.5f, 209.5f), 
		new Vector2(1107.5f, 125.5f), 
		new Vector2(80.5f, 155.5f), 
		new Vector2(187.5f, 234.5f), 
		new Vector2(284.5f, 148.5f), 
		new Vector2(388.5f, 232.5f), 
		new Vector2(592.5f, 189.5f), 
		new Vector2(904.5f, 144.5f), 
		new Vector2(999.5f, 228.5f), 
		new Vector2(1106.5f, 143.5f), 
		new Vector2(80.5f, 159.5f), 
		new Vector2(189.5f, 241.5f), 
		new Vector2(283.5f, 158.5f), 
		new Vector2(389.5f, 239.5f), 
		new Vector2(592.5f, 198.5f), 
		new Vector2(903.5f, 155.5f), 
		new Vector2(999.5f, 236.5f), 
		new Vector2(1108.5f, 154.5f), 
		new Vector2(82.5f, 175.5f), 
		new Vector2(188.5f, 255.5f), 
		new Vector2(284.5f, 172.5f), 
		new Vector2(391.5f, 253.5f), 
		new Vector2(594.5f, 210.5f), 
		new Vector2(900.5f, 167.5f), 
		new Vector2(1001.5f, 252.5f), 
		new Vector2(1104.5f, 167.5f), 
		new Vector2(84.5f, 181.5f), 
		new Vector2(189.5f, 260.5f), 
		new Vector2(286.5f, 179.5f), 
		new Vector2(390.5f, 261.5f), 
		new Vector2(593.5f, 220.5f), 
		new Vector2(901.5f, 174.5f), 
		new Vector2(999.5f, 258.5f), 
		new Vector2(1105.5f, 175.5f), 
		new Vector2(85.5f, 189.5f), 
		new Vector2(190.5f, 267.5f), 
		new Vector2(284.5f, 185.5f), 
		new Vector2(390.5f, 267.5f), 
		new Vector2(593.5f, 224.5f), 
		new Vector2(902.5f, 182.5f), 
		new Vector2(998.5f, 264.5f), 
		new Vector2(1104.5f, 182.5f), 
		new Vector2(83.5f, 197.5f), 
		new Vector2(191.5f, 274.5f), 
		new Vector2(287.5f, 192.5f), 
		new Vector2(390.5f, 275.5f), 
		new Vector2(593.5f, 232.5f), 
		new Vector2(902.5f, 189.5f), 
		new Vector2(997.5f, 270.5f), 
		new Vector2(1103.5f, 189.5f), 
		new Vector2(86.5f, 208.5f), 
		new Vector2(193.5f, 288.5f), 
		new Vector2(285.5f, 207.5f), 
		new Vector2(390.5f, 288.5f), 
		new Vector2(593.5f, 247.5f), 
		new Vector2(900.5f, 205.5f), 
		new Vector2(999.5f, 286.5f), 
		new Vector2(1104.5f, 204.5f), 
		new Vector2(85.5f, 217.5f), 
		new Vector2(191.5f, 296.5f), 
		new Vector2(288.5f, 212.5f), 
		new Vector2(391.5f, 293.5f), 
		new Vector2(593.5f, 253.5f), 
		new Vector2(899.5f, 211.5f), 
		new Vector2(996.5f, 291.5f), 
		new Vector2(1103.5f, 213.5f), 
		new Vector2(85.5f, 222.5f), 
		new Vector2(193.5f, 301.5f), 
		new Vector2(287.5f, 219.5f), 
		new Vector2(391.5f, 301.5f), 
		new Vector2(593.5f, 259.5f), 
		new Vector2(899.5f, 216.5f), 
		new Vector2(998.5f, 298.5f), 
		new Vector2(1101.5f, 217.5f), 
		new Vector2(90.5f, 228.5f), 
		new Vector2(193.5f, 308.5f), 
		new Vector2(288.5f, 227.5f), 
		new Vector2(393.5f, 307.5f), 
		new Vector2(591.5f, 269.5f), 
		new Vector2(901.5f, 224.5f), 
		new Vector2(993.5f, 304.5f), 
		new Vector2(1100.5f, 223.5f), 
		new Vector2(87.5f, 235.5f), 
		new Vector2(195.5f, 314.5f), 
		new Vector2(289.5f, 233.5f), 
		new Vector2(394.5f, 314.5f), 
		new Vector2(595.5f, 273.5f), 
		new Vector2(898.5f, 229.5f), 
		new Vector2(993.5f, 310.5f), 
		new Vector2(1100.5f, 230.5f), 
		new Vector2(89.5f, 242.5f), 
		new Vector2(194.5f, 320.5f), 
		new Vector2(289.5f, 240.5f), 
		new Vector2(394.5f, 320.5f), 
		new Vector2(594.5f, 279.5f), 
		new Vector2(899.5f, 237.5f), 
		new Vector2(994.5f, 319.5f), 
		new Vector2(1100.5f, 238.5f), 
		new Vector2(90.5f, 244.5f), 
		new Vector2(195.5f, 323.5f), 
		new Vector2(289.5f, 243.5f), 
		new Vector2(392.5f, 324.5f), 
		new Vector2(592.5f, 283.5f), 
		new Vector2(901.5f, 241.5f), 
		new Vector2(995.5f, 320.5f), 
		new Vector2(1102.5f, 241.5f), 
		new Vector2(92.5f, 256.5f), 
		new Vector2(197.5f, 335.5f), 
		new Vector2(290.5f, 254.5f), 
		new Vector2(393.5f, 334.5f), 
		new Vector2(592.5f, 292.5f), 
		new Vector2(896.5f, 250.5f), 
		new Vector2(992.5f, 331.5f), 
		new Vector2(1099.5f, 253.5f), 
		new Vector2(91.5f, 262.5f), 
		new Vector2(198.5f, 339.5f), 
		new Vector2(290.5f, 259.5f), 
		new Vector2(396.5f, 339.5f), 
		new Vector2(593.5f, 299.5f), 
		new Vector2(899.5f, 257.5f), 
		new Vector2(992.5f, 335.5f), 
		new Vector2(1096.5f, 255.5f), 
		new Vector2(92.5f, 272.5f), 
		new Vector2(195.5f, 347.5f), 
		new Vector2(291.5f, 267.5f), 
		new Vector2(393.5f, 347.5f), 
		new Vector2(593.5f, 308.5f), 
		new Vector2(898.5f, 264.5f), 
		new Vector2(993.5f, 346.5f), 
		new Vector2(1095.5f, 268.5f), 
		new Vector2(93.5f, 277.5f), 
		new Vector2(197.5f, 355.5f), 
		new Vector2(290.5f, 275.5f), 
		new Vector2(395.5f, 353.5f), 
		new Vector2(592.5f, 314.5f), 
		new Vector2(895.5f, 270.5f), 
		new Vector2(990.5f, 349.5f), 
		new Vector2(1097.5f, 274.5f)};
	*/
#endif
}
