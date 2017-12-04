using UnityEngine;
using System.Collections;

public class WebcamFpsTest : MonoBehaviour {

	public GameObject aCameraView;
	string webcamName = "";
	WebCamDevice aDevice;
	WebCamTexture aWebcamTexture;
	bool isInitialized = false;
	int frameCount = 0;
	float startingTime = 0f;
	public float fpsSampleTime = 10f;
	//int delayCounter = 0;
	public int aDelayCounterBetweenUpdates = 1000000;
	int[] delayCounters = new int[6]{0, 500000, 1000000, 2500000, 5000000, 10000000};
	int[] fpsRates = new int[3]{5, 15, 30};
	//string reportString = "";

	void Start() {
		Scheduler.StartCoroutine(StartRce());
	}

	IEnumerator StartRce () {
		yield return null;
		while (webcamName == "") yield return null;

		for (int i = 0; i < delayCounters.Length; i++) {
			int currentDelay = delayCounters[i];
			Debug.Log("Tests for delay = " + delayCounters[i]);

			string delayReport = "Delay, FPS Requested, Test Number, Frame Count, Actual FPS \n";

			for (int j = 0; j < fpsRates.Length; j++) {
				int currentFps = fpsRates[j];
				for(int testNumber = 0; testNumber < 2; testNumber++) {

					aWebcamTexture = new WebCamTexture(webcamName, 1280, 720);
					aWebcamTexture.Play();
					aCameraView.renderer.material.mainTexture = aWebcamTexture;
					yield return new WaitSeconds(3f);
					startingTime = Time.realtimeSinceStartup;

					frameCount = 0;
					isInitialized = true;
					timeSinceStartup = 0f;
					while (fpsSampleTime > timeSinceStartup) { //isInitialized) {
						for(int k = 0; k < currentDelay; k++) {
							k = k + 1 - 1;
						}
						if (aWebcamTexture.didUpdateThisFrame) {
							frameCount++;
						}
						yield return null;
						timeSinceStartup = Time.realtimeSinceStartup - startingTime;
					}

					delayReport += "" + currentDelay + ", " + currentFps + ", " + testNumber + ", " + frameCount + ", " + (frameCount / timeSinceStartup) + "\n";
					aWebcamTexture.Stop();
					yield return new WaitSeconds(2f);

				}
			}
			Debug.Log(delayReport);
		}


	}

	float timeSinceStartup = 0;
	void Update () {
		return;
/*		if (!isInitialized) return;
		if (aWebcamTexture.didUpdateThisFrame) {
			frameCount++;
		}

		timeSinceStartup = Time.realtimeSinceStartup - startingTime;
		if (timeSinceStartup > fpsSampleTime) {
			isInitialized = false;
			aWebcamTexture.Stop();
			isInitialized = false;
		}*/
	
	}

	void OnGUI() {
		if (webcamName != "") return;
		Rect main = new Rect(0, 0, 300, 25);
		foreach(WebCamDevice wcd in WebCamTexture.devices) {
			if (GUI.Button(main, wcd.name)) webcamName = wcd.name;
			main.y += 25;
		}

	}
}
