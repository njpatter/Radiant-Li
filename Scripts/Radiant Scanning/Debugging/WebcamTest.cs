using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class WebcamTest : MonoBehaviour {
	#if !UNITY_IOS && !UNITY_ANDROID
	public GameObject view1;
	public GameObject view2;
	// Use this for initialization
	IEnumerator Start () {
		OpenCV.Init();
		yield return null;

		List<int> allCams = new List<int>();
		for(int i = 0; i < WebCamTexture.devices.Length; i++) {
			if (WebCamTexture.devices[i].name.Contains("Live")) {
				allCams.Add(i);
				Debug.Log(WebCamTexture.devices[i].name);
			}
		}
		if (allCams.Count != 2) yield break;
		Debug.Log("Found " + allCams.Count + " cameras, first cam is index = ");// + allCams[0]);

		WebCamTexture cam1 = new WebCamTexture(WebCamTexture.devices[allCams[0]].name);
		WebCamTexture cam2 = new WebCamTexture(WebCamTexture.devices[allCams[1]].name);

		float startTime = Time.realtimeSinceStartup;
		cam1.Play();

		while(cam1.width != 1280) yield return null;
		Debug.Log("cam1 " + cam1.width + "   " + cam1.height);
		Color[] imageOne = cam1.GetPixels();
		cam1.Stop();
		while(cam1.isPlaying) yield return null;



		cam2.Play();
		while(cam2.width != 1280) yield return null;
		Debug.Log("cam2 " + cam2.width + "   " + cam2.height);
		float totalTime = Time.realtimeSinceStartup - startTime;
		Debug.Log("Total time " + totalTime);
		Texture2D newTex = new Texture2D(1280, 720);
		newTex.SetPixels(imageOne);
		newTex.Apply();
		view1.renderer.material.mainTexture = newTex;
		view2.renderer.material.mainTexture = cam2;
		cam2.Stop();
		yield break;

		/* UNREACHABLE
		while(!Input.GetKeyDown(KeyCode.Escape)) yield return null;
		
		IntPtr cvCapOne = OpenCV.cvCreateCameraCapture(allCams[0]); 
		//IntPtr cvCapTwo = OpenCV.cvCreateCameraCapture(allCams[1]); 
		OpenCV.cvSetCaptureProperty(cvCapOne, (int)CvCapture.CV_CAP_PROP_FRAME_WIDTH, 1280); 
		OpenCV.cvSetCaptureProperty(cvCapOne, (int)CvCapture.CV_CAP_PROP_FRAME_HEIGHT, 720); 

		
		while(!Input.GetKeyDown(KeyCode.Space)) yield return null;

		//while(!Input.GetKeyDown(KeyCode.N)) {
			yield return StartCoroutine(CreatePicture(cvCapOne, view1));
			//yield return StartCoroutine(CreatePicture(cvCapTwo, view2));
			yield return new WaitForSeconds(3);
		//}

		OpenCV.cvReleaseCapture(ref cvCapOne);
		//OpenCV.cvReleaseCapture(ref cvCapTwo);
		Debug.Log("Cams turned off");
		*/
	}
	
	// Update is called once per frame
	IEnumerator CreatePicture (IntPtr cvCap, GameObject aView) {
		Debug.Log("Cam turned on");
		IntPtr anImage = OpenCV.cvQueryFrame(cvCap);
		Matrix imageMatrix = new Matrix(720,1280);

		Debug.Log("About to convert image with pointer " + anImage + " to mat ptr " + imageMatrix.matPtr);
		OpenCV.cvGetMat(anImage, imageMatrix.matPtr, IntPtr.Zero, 0);
		Debug.Log("Done converting image");
		
		Matrix r = new Matrix(imageMatrix.height, imageMatrix.width, 0);
		Matrix g = new Matrix(imageMatrix.height, imageMatrix.width, 0);
		Matrix b = new Matrix(imageMatrix.height, imageMatrix.width, 0);
		
		Debug.Log("Image width, height =" + r.width + ", " + r.height); 
		
		OpenCV.cvSplit(anImage, r.matPtr, g.matPtr, b.matPtr, IntPtr.Zero);
		
		Texture2D aTex = new Texture2D(imageMatrix.width, imageMatrix.height);
		for(int i = 0; i < imageMatrix.width; i++) {
			for(int j = 0; j < imageMatrix.height; j++) {
				aTex.SetPixel(i,j, new Color((float)b[j,i] / 255f, (float)g[j,i] / 255f, (float)r[j,i] / 255f));
			}
		}
		aTex.Apply();
		aView.renderer.material.mainTexture = aTex;
		r.Destroy();
		g.Destroy();
		b.Destroy();
		yield return null;
	}
#endif
}
