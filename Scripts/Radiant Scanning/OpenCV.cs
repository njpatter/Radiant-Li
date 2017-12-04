using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class OpenCV {
	#if !UNITY_IOS && !UNITY_ANDROID
	static bool m_inited = false;
	
	public const int CV_RGB2GRAY    = 7;
	public const int CV_BLUR = 1;
	public const int CV_GAUSSIAN    = 2;
	public const int CV_MEDIAN = 3;
	public const int CV_BILATERAL = 4;

	public const int CV_HOUGH_GRADIENT = 3;
	
	public const int CV_TERMCRIT_ITER = 1;
	public const int CV_TERMCRIT_NUMBER = 1;
	public const int CV_TERMCRIT_EPS = 2;
	
	public const int CV_CALIB_USE_INTRINSIC_GUESS = 1;
	public const int CV_CALIB_FIX_ASPECT_RATIO    = 2;
	public const int CV_CALIB_FIX_PRINCIPAL_POINT = 4;
	public const int CV_CALIB_ZERO_TANGENT_DIST   = 8;
	public const int  CV_CALIB_FIX_FOCAL_LENGTH = 16;
	public const int  CV_CALIB_FIX_K1 = 32;
	public const int  CV_CALIB_FIX_K2 = 64;
	public const int  CV_CALIB_FIX_K3 = 128;
	public const int  CV_CALIB_FIX_K4 = 2048;
	public const int  CV_CALIB_FIX_K5 = 4096;
	public const int  CV_CALIB_FIX_K6 = 8192;
	public const int  CV_CALIB_RATIONAL_MODEL = 16384;
	public const int  CV_CALIB_THIN_PRISM_MODEL = 32768;
	public const int  CV_CALIB_FIX_S1_S2_S3_S4 = 65536;

	public const int kMinCircleRadiusInPixels 		= 9;
	public const int kMaxCircleRadiusInPixels 		= 14;
	public const double kCircleDetectionConstraint	= 17.5; 
	public const double kMinDistBetweenCircles		= 25;
	
#if UNITY_STANDALONE_OSX
	public const string Cv_CoreDll = "OpenCvBundle";
	public const string Cv_Calib3dDll = "OpenCvBundle";
	public const string Cv_ImageProcDll = "OpenCvBundle";
	public const string Cv_HighGuiDll = "OpenCvBundle";
	public const string Cv_ObjectDetectDll = "OpenCvBundle";
#endif
	
#if UNITY_STANDALONE_WIN
	public const string Cv_CoreDll = "opencv_core248";
	public const string Cv_Calib3dDll = "opencv_calib3d248";
	public const string Cv_ImageProcDll = "opencv_imgproc248";
	public const string Cv_HighGuiDll = "opencv_highgui248";
	public const string Cv_FlannDll = "opencv_flann248";
	public const string Cv_Features2dDll = "opencv_features2d248";
	public const string Cv_ObjectDetectDll = "opencv_objdetect248";

	[DllImport("kernel32.dll")]
	public static extern IntPtr LoadLibrary(string dllToLoad);

	[DllImport("kernel32.dll")]
	public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);	
#endif
	
	[DllImport(Cv_CoreDll)]
	public static extern IntPtr cvCreateMemStorage(int block_size);
	
	[DllImport(Cv_CoreDll)]
	public static extern IntPtr cvClearMemStorage(IntPtr storage);
	
	[DllImport(Cv_CoreDll)]
	public static extern IntPtr cvGetSeqElem(IntPtr seq, int index);
	
	[DllImport(Cv_CoreDll)]
	public static extern IntPtr cvCreateMat(int rows, int cols, int type);
	
	[DllImport(Cv_CoreDll)]
	public static extern void cvReleaseMat(ref IntPtr mat);

	[DllImport(Cv_CoreDll)]
	public static extern void cvSetReal2D(IntPtr mat, int y, int x, double val);
	
	[DllImport(Cv_CoreDll)]
	public static extern int cvGetElemType(IntPtr mat);
	
	[DllImport(Cv_CoreDll) ]
	public static extern CvSize cvGetSize(IntPtr mat);
	
	[DllImport(Cv_CoreDll) ]
	public static extern IntPtr cvGetMat(IntPtr arr, IntPtr header);
	
	[DllImport(Cv_CoreDll) ]
	public static extern int cvGetDimSize(IntPtr mat, int index);
	
	[DllImport(Cv_CoreDll)]
	public static extern void cvSet2D(IntPtr mat, int y, int x, cvScalar scalar);
	
	[DllImport(Cv_CoreDll)]
	public static extern cvScalar cvGet2D(IntPtr mat, int y, int x);
	
	[DllImport(Cv_CoreDll)]
	public static extern double cvGetReal2D(IntPtr mat, int y, int x);

	[DllImport(Cv_Calib3dDll)]
	public static extern void cvFindExtrinsicCameraParams2(IntPtr spatialPoints, 
		IntPtr imagePoints, IntPtr camMat, IntPtr distortion, IntPtr rotation, 
		IntPtr translation, int useExtrinsic);
	
	
	/*
	CVAPI(double) cvCalibrateCamera2( 
		const CvMat* object_points,
        const CvMat* image_points,
        const CvMat* point_counts,
        CvSize image_size,
        CvMat* camera_matrix,
        CvMat* distortion_coeffs,
        CvMat* rotation_vectors CV_DEFAULT(NULL),
        CvMat* translation_vectors CV_DEFAULT(NULL),
        int flags CV_DEFAULT(0),
        CvTermCriteria term_crit CV_DEFAULT(cvTermCriteria(
            CV_TERMCRIT_ITER+CV_TERMCRIT_EPS,30,DBL_EPSILON)) );
	//*/
	[DllImport(Cv_Calib3dDll)]
	public static extern double cvCalibrateCamera2(
		IntPtr object_points, 
		IntPtr image_points, 
		IntPtr point_counts, 
		CvSize image_size, 
		IntPtr camera_matrix, 
		IntPtr distortion_coeffs, 
		IntPtr rotation_vectors,
		IntPtr translation_vectors, 
		int flags);//, 
		//CvTermCriteria term_crit);
	
	/// void cvInitIntrinsicParams2D(const CvMat* object_points, 
	/// const CvMat* image_points, const CvMat* npoints, CvSize image_size, 
	/// CvMat* camera_matrix, double aspect_ratio=1. )
	[DllImport(Cv_Calib3dDll)]
	public static extern void cvInitIntrinsicParams2D(
		IntPtr object_points, 
		IntPtr image_points, 
		IntPtr npoints, 
		CvSize image_size,
		IntPtr camera_matrix,
		double aspect_ratio);
	
	[DllImport(Cv_Calib3dDll)]
	public static extern void cvRodrigues2(IntPtr src, IntPtr dest, IntPtr jacobian); 

	[DllImport(Cv_Calib3dDll)]
	public static extern int cvFindChessboardCorners(IntPtr image, CvSize pattern_size, IntPtr corners, IntPtr corner_count, 
	                                                  int flags); 

	[DllImport(Cv_ImageProcDll)]
	public static extern void cvFindCornerSubPix(IntPtr image, IntPtr corners, 
	                                             int count, CvSize win, CvSize zero_zone, CvTermCriteria criteria);

	[DllImport(Cv_ImageProcDll)]
	public static extern void cvCvtColor(IntPtr src, IntPtr dst, int code); 
	
	[DllImport(Cv_ImageProcDll)]
	public static extern void cvSmooth(IntPtr src, IntPtr dst, int smoothtype, int size1, int size2, double sigma1, double sigma2); 
	
	[DllImport(Cv_ImageProcDll)]
	public static extern IntPtr cvHoughCircles(IntPtr image, IntPtr circle_storage, int method,
		double dp, double min_dist, double param1, double param2, int min_radius, int max_radius);
	
	[DllImport(Cv_HighGuiDll)]
	public static extern IntPtr cvCreateCameraCapture(int device);

	[DllImport(Cv_HighGuiDll)]
	public static extern void cvReleaseCapture(ref IntPtr cvCapturePtr);

	[DllImport(Cv_HighGuiDll)]
	public static extern IntPtr cvQueryFrame(IntPtr cvCapture);

	[DllImport(Cv_HighGuiDll)]
	public static extern int cvSetCaptureProperty(IntPtr cvCapture, int property_id, double value);

	[DllImport(Cv_HighGuiDll)]
	public static extern double cvGetCaptureProperty(IntPtr cvCapture, int property_id);

	[DllImport(Cv_CoreDll)]
	public static extern IntPtr cvGetMat(IntPtr arr, IntPtr header, IntPtr coi, int allowND=0);

	[DllImport(Cv_CoreDll)]
	public static extern void cvSplit(IntPtr src, IntPtr dst0, IntPtr dst1, IntPtr dst2, IntPtr dst3);

	[DllImport(Cv_ObjectDetectDll)]
	public static extern IntPtr cvHaarDetectObjects(IntPtr image, IntPtr cascade, IntPtr storage,
	                                                double scale_factor, int min_neighbors, int flags, 
	                                                CvSize min_size, CvSize max_size);
	
	[DllImport(Cv_ObjectDetectDll)]
	public static extern IntPtr cvLoadHaarClassifierCascade(IntPtr directory, CvSize orig_window_size );

	public static int CV_FOURCC_MACRO(char c1, char c2, char c3, char c4) {
		return (((c1) & 255) + (((c2) & 255) << 8) + (((c3) & 255) << 16) + (((c4) & 255) << 24));
	}
	
	public static void Init() {
		if (m_inited)
			return;
#if UNITY_STANDALONE_WIN
		string path = UnityEngine.Application.dataPath + "/Plugins/";
#if UNITY_EDITOR
			path += "x86/";
#endif
		LoadLibrary(path + Cv_CoreDll + ".dll");
		LoadLibrary(path + Cv_ImageProcDll + ".dll");
		LoadLibrary(path + Cv_Calib3dDll + ".dll");
		LoadLibrary(path + Cv_FlannDll + ".dll");
		LoadLibrary(path + Cv_Features2dDll +".dll"); 
		LoadLibrary(path + Cv_HighGuiDll + ".dll");
#endif
		m_inited = true;
	}

	public static Matrix PixelToWorldCoordinates(Matrix pixelPoint, 
		Matrix cameraIntrinsics, Matrix Rotation, Matrix Translation, float zScale) {  
		
		Matrix camCoords = Matrix.Zero(3,1);
		camCoords[0,0] = (pixelPoint[0,0] - cameraIntrinsics[0,2]) / 
			cameraIntrinsics[0,0] * zScale;
		camCoords[1,0] = (pixelPoint[1,0] - cameraIntrinsics[1,2]) / 
			cameraIntrinsics[1,1] * zScale;
		camCoords[2,0] = zScale;
		camCoords = camCoords - Translation;
		camCoords = Rotation.t() * camCoords;
		return camCoords;
		
	}
	
	public static void ReorderPoints(Vector3[] worldPoints, Vector2[] imagePoints, int numberOfImages,
		out Vector3[] newWorldPoints, out Vector2[] newImagePoints) {
		if (worldPoints.Length % numberOfImages != 0 || worldPoints.Length != imagePoints.Length) {
			Debug.LogError("Something is wrong with the number of images chosen " + (numberOfImages) +
			               " and/or the length of the point arrays " + worldPoints.Length);
		}
		
		List<Vector3> tempWorldPoints = new List<Vector3>(worldPoints);
		List<Vector2> tempImagePoints = new List<Vector2>(imagePoints);
		List<List<Vector3>> distributedWorld = new List<List<Vector3>>();
		List<List<Vector2>> distributedImage = new List<List<Vector2>>();
		for(int i = 0; i < numberOfImages; i++) distributedWorld.Add(new List<Vector3>());
		for(int i = 0; i < numberOfImages; i++) distributedImage.Add(new List<Vector2>());
		for(int i = 0; i < worldPoints.Length / 8; i++) {
			for(int j = 0; j < 8; j++) {
				distributedWorld[i%numberOfImages].Add(tempWorldPoints[0]); 
				distributedImage[i%numberOfImages].Add(tempImagePoints[0]);
				tempWorldPoints.RemoveAt(0);
				tempImagePoints.RemoveAt(0);
			}
		}
		for(int i = 0; i < numberOfImages; i++) {
			tempWorldPoints.AddRange(distributedWorld[i]);
		 	tempImagePoints.AddRange(distributedImage[i]);
		}
		if (worldPoints.Length != tempWorldPoints.Count) Debug.LogError("Reordering went awry");
		newWorldPoints = tempWorldPoints.ToArray();
		newImagePoints = tempImagePoints.ToArray();
	}
	
	public static Matrix ConvertToMultiChannelMatrix(Vector2[] points2d) {
		Matrix mat = new Matrix(1, points2d.Length, MatrixType.cv32FC2);
		for(int i = 0; i < points2d.Length; i++) {
			mat[0,i, true] = new cvScalar(points2d[i].x, points2d[i].y);
		}
		return mat;
	}
	
	public static Matrix ConvertToMultiChannelMatrix(Vector3[] points3d) {
		Matrix mat = new Matrix(1, points3d.Length, MatrixType.cv32FC3);
		for(int i = 0; i < points3d.Length; i++) {
			mat[0,i, true] = new cvScalar(points3d[i].x, points3d[i].y, points3d[i].z);
		}
		return mat;
	}
	
}

[StructLayout(LayoutKind.Sequential)]
public struct CvSize 
{
	public int width;
	public int height;
}

[StructLayout(LayoutKind.Sequential)]
public struct CvPoint2D32f
{
	public float x;
	public float y;
}

[StructLayout(LayoutKind.Sequential)]
public struct CvTermCriteria 
{
	public int type;
	public int max_iter;
	public double epsilon;
	
	public CvTermCriteria(int aType, int aMax_iter, double anEpsilon) {
		type = aType;
		max_iter = aMax_iter;
		epsilon = anEpsilon;
	}
}

[StructLayout(LayoutKind.Sequential)]
public struct CvRect
{
	public int x;
	public int y;
	public int width;
	public int height;
}

[StructLayout(LayoutKind.Sequential)]
public struct cvScalar 
{
	public double d0;
	public double d1;
	public double d2;
	public double d3;
	
	public cvScalar(double d0, double d1) 
	{
		//this.val = new double[4];
		this.d0 = d0;
		this.d1 = d1;
		this.d2 = 0;
		this.d3 = 0;
	}
	
	public cvScalar(double d0, double d1, double d2) 
	{
		//this.val = new double[4];
		this.d0 = d0;
		this.d1 = d1;
		this.d2 = d2;
		this.d3 = 0;
	}
	
	public cvScalar(double d0, double d1, double d2, double d3) 
	{
		//this.val = new double[4];
		this.d0 = d0;
		this.d1 = d1;
		this.d2 = d2;
		this.d3 = d3;
	}
	
	public override string ToString() {
		return string.Format("cvScalar : {0}, {1}, {2}, {3}",
		                     //this.val[0], this.val[1], this.val[2], this.val[3]);
		                     this.d0, this.d1, this.d2, this.d3);
	}
}

[StructLayout(LayoutKind.Sequential)]
public struct cvSeq
{
	public int flags;
	public int header_size;
	public IntPtr h_prev;
	public IntPtr h_next;
	public IntPtr v_prev;
	public IntPtr v_next;
	
	public int total;
	public int elem_size;
	public IntPtr block_max;
	public IntPtr ptr;
	public int delta_elems;
	public IntPtr storage;
	public IntPtr free_blocks;
	public IntPtr first;
	
	public override string ToString() {
		return String.Format("Flags {0} \n header_size {1} \n h_prev {2} \n" +
		                     "h_next {3} \nv_prev {4} \nv_next {5} \ntotal {6} \nelem_size {7} \n" +
		                     "block_max {8} \nptr {9} \ndelta_elems {10} \nstorage {11} \nfree_blocks {12} \n" +
		                     "first {13}", this.flags, this.header_size, this.h_prev, this.h_next, this.v_prev,
		                     this.v_next, this.total, this.elem_size, this.block_max, this.ptr,
		                     this.delta_elems, this.storage, this.free_blocks, this.first);
	}
#endif
	
}


