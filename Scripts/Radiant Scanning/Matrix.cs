using UnityEngine;
using System;
using System.Collections;
using System.Runtime.InteropServices;

// Matrix Type Depth Example: 
// #define CV_16SC1 CV_MAKETYPE(CV_16S,1)
// #define CV_MAKETYPE(depth,cn) (CV_MAT_DEPTH(depth) + (((cn)-1) << CV_CN_SHIFT))
// #define CV_MAT_DEPTH_MASK       (CV_DEPTH_MAX - 1)
// #define CV_MAT_DEPTH(flags)     ((flags) & CV_MAT_DEPTH_MASK)
// #define CV_CN_MAX     512
// #define CV_CN_SHIFT   3
// #define CV_DEPTH_MAX  (1 << CV_CN_SHIFT)
// So the equation becomes: depth(numbers below) + (channels - 1) << 3
public enum MatrixType {  
	cv8UC1 = 0, 
	cv8SC1 = 1,
	cv16UC1 = 2,
	cv16SC1 = 3,
	cv32SC1 = 4,
	cv32FC1 = 5,
	cv64FC1 = 6,
	//#define CV_USRTYPE1 7
	cv8UC2 = 8, 
	cv8SC2 = 9,
	cv16UC2 = 10,
	cv16SC2 = 11,
	cv32SC2 = 12,
	cv32FC2 = 13,
	cv64FC2 = 14,
	// 15
	cv8UC3 = 16, 
	cv8SC3 = 17,
	cv16UC3 = 18,
	cv16SC3 = 19,
	cv32SC3 = 20,
	cv32FC3 = 21,
	cv64FC3 = 22
}

public class Matrix {	
	#if !UNITY_IOS && !UNITY_ANDROID
	public IntPtr matPtr;
	public int[] size;

	public int width {
		get {
			return OpenCV.cvGetDimSize(this.matPtr, 1);
		}
	}

	public int height {
		get {
			return OpenCV.cvGetDimSize(this.matPtr, 0);
		}
	}
	
	public Matrix(int rows, int cols, MatrixType type) {
		size = new int[2]{rows,cols};
		matPtr = OpenCV.cvCreateMat(rows, cols, (int)type);
	}
	
	public Matrix(int rows, int cols) {
		size = new int[2]{rows,cols};
		matPtr = OpenCV.cvCreateMat(rows, cols, (int)MatrixType.cv32FC1 );
	}
	
	public Matrix(Vector2 v) {
		size = new int[2]{2,1};
		matPtr = OpenCV.cvCreateMat(2, 1, (int)MatrixType.cv32FC1);
		this[0,0] = v.x;
		this[1,0] = v.y;
	}
	
	public Matrix(Vector3 v) {
		size = new int[2]{3,1};
		matPtr = OpenCV.cvCreateMat(3, 1, (int)MatrixType.cv32FC1);
		this[0,0] = v.x;
		this[1,0] = v.y;
		this[2,0] = v.z;
	}
	
	~Matrix() {
		OpenCV.cvReleaseMat(ref matPtr);
	}

	public int type {
		get {
			return OpenCV.cvGetElemType(this.matPtr);
		}
	}

	public void Destroy() {
		OpenCV.cvReleaseMat(ref this.matPtr);
	}
	
	public float this[int r, int c] {
		get {
			if (r >= size[0] || c >= size[1]) {
				Debug.LogError("Index out of bounds!");
				return float.MinValue;
			}
			return (float)OpenCV.cvGetReal2D(matPtr, r, c);
		}
		set {
			if (r >= size[0] || c >= size[1]) {
				Debug.LogError("Index out of bounds!");
				return;
			}
			OpenCV.cvSetReal2D(matPtr, r, c, value);
		}
	}
	
	public cvScalar this[int r, int c, bool isScalar] {
		get {
			return OpenCV.cvGet2D(matPtr, r, c);
		}
		set {
			if (r >= size[0] || c >= size[1]) {
				Debug.LogError("Index out of bounds!");
				return;
			}
			OpenCV.cvSet2D(matPtr, r, c, value);
		}
	}
	
	public override string ToString() {
		string info = "[";
		for(int r = 0; r < this.size[0]; r++) {
			for(int c = 0; c < this.size[1] - 1; c++) {
				info += this[r,c] + ", ";
			}
			if (r < this.size[0] - 1) info += this[r,this.size[1] - 1] + "\n";
			else info += this[r,this.size[1] - 1] + "]";
		}
		return info;
	}
	
	public Matrix Copy() {
		Matrix aCopy = new Matrix(this.size[0], this.size[1]);
		for(int r = 0; r < this.size[0]; r++) {
			for(int c = 0; c < this.size[1]; c++) {
				aCopy[r,c] = this[r,c];
			}
		}
		return aCopy;
	}
	
	public Matrix t() {
		Matrix transpose = new Matrix(this.size[1], this.size[0]);
		for(int r = 0; r < this.size[0]; r++) {
			for(int c = 0; c < this.size[1]; c++) {
				transpose[c,r] = this[r,c];
			}
		}
		return transpose;
	}
	
	public static Matrix operator*(float a, Matrix b) {
		Matrix answer = Matrix.Zero(b.size[0], b.size[1]);
		for(int r = 0; r < b.size[0]; r++) {
			for(int c = 0; c < b.size[1]; c++) {
				answer[r,c] = a * b[r,c];
			}
		}
		return answer;
	}
	
	public static Matrix operator*(Matrix a, Matrix b) {
		if (a.size[1] != b.size[0]) {
			Debug.LogError("Matrix sizes do not allow multiplication!!!");
			return null;
		}
		
		Matrix answer = new Matrix(a.size[0], b.size[1]);
		for(int r = 0; r < a.size[0]; r++) {
			for(int c = 0; c < b.size[1]; c++) {
				float matValue = 0f;
				for(int m = 0; m < a.size[1]; m++) {
					matValue += a[r,m] * b[m,c];
				}
				answer[r,c] = matValue;
			}
		}
		return answer;
	}
	
	public static Vector3 operator*(Matrix a, Vector3 b) {
		if (a.size[1] != 3) {
			Debug.LogError("Matrix/Vector3 sizes do not allow multiplication!!!");
			return Vector3.one * float.MinValue;
		}
		
		Vector3 answer = Vector3.zero;
		for(int r = 0; r < 3; r++) {
			float matValue = 0f;
			for(int m = 0; m < a.size[1]; m++) {
				matValue += a[r,m] * b[m];
			}
			answer[r] = matValue;
		}
		return answer;
	}
	
	public static Matrix operator+(Matrix a, Matrix b) {
		if (a.size[0] != b.size[0] || a.size[1] != b.size[1]) {
			Debug.LogError("Matrix sizes do not allow addition!!!!");
			return null;
		}
		Matrix answer = new Matrix(a.size[0], a.size[1]);
		for(int r = 0; r < a.size[0]; r++) {
			for(int c = 0; c < a.size[1]; c++) {
				answer[r,c] = a[r,c] + b[r,c];
			}
		}
		return answer;
	}
	
	public static Matrix operator-(Matrix a, Matrix b) {
		if (a.size[0] != b.size[0] || a.size[1] != b.size[1]) {
			Debug.LogError("Matrix sizes do not allow subtraction!!!!");
			return new Matrix(0,0);
		}
		Matrix answer = new Matrix(a.size[0], a.size[1]);
		for(int r = 0; r < a.size[0]; r++) {
			for(int c = 0; c < a.size[1]; c++) {
				answer[r,c] = a[r,c] - b[r,c];
			}
		}
		return answer;
	}
	
	public static Matrix operator-(Matrix a) {
		Matrix val = new Matrix(a.size[0], a.size[1]);
		for(int i = 0; i < a.size[0]; i++) {
			for (int j = 0; j < a.size[0]; j++) {
				val[i,j] = -a[i,j];
			}
		}
		return val;
	}
	
	public static Matrix Zero(int r, int c, MatrixType type) {
		return InitToValue(r,c,0f, type);
	}
	
	public static Matrix Zero(int r, int c) {
		return InitToValue(r,c,0f, MatrixType.cv32FC1);
	}
	
	public static Matrix One(int r, int c) {
		return InitToValue(r,c,1f, MatrixType.cv32FC1);
	}
	
	public static Matrix Identity(int r, int c) {
		return Identity(r, c, MatrixType.cv32FC1);
	}
	
	public static Matrix Identity(int r, int c, MatrixType type) {
		if (r != c) {
			Debug.LogError("Identity needs to be square");
			return Matrix.Identity(r,r);
		}
		Matrix mat = Matrix.Zero(r,r);
		for(int i = 0; i < r; i++) {
			for (int j = 0; j < r; j++) {
				if (i==j) mat[i,j] = 1f;
			}
		}
		return mat;
	}
	
	public static Matrix InitToValue(int r, int c, float val, MatrixType type) {
		Matrix mat = new Matrix(r,c,type);
		for(int i = 0; i < r; i++) {
			for (int j = 0; j < c; j++) {
				mat[i,j] = val;
			}
		}
		return mat;
	}
	
	public Vector3 ToVector3() {
		if (this.size[0] != 3 || this.size[1] != 1) {
			Debug.LogError("Matrix not correct size/shape to convert to Vector3!!!");
			return Vector3.zero;
		}
		return new Vector3(this[0,0], this[1,0], this[2,0]);
	}

	public float[,] ToArray() {
		float[,] anIntArray = new float[size[0], size[1]];
		for(int r = 0; r < this.size[0]; r++) {
			for(int c = 0; c < this.size[1]; c++) {
				anIntArray[r,c] = (float)this[r,c];
			}
		}
		return anIntArray;
	}
#endif
}
