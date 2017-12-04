using UnityEngine;
using System.Collections;

public class CalibrationTest : MonoBehaviour {
	#if !UNITY_IOS && !UNITY_ANDROID
	public PrinterController pc;
	
	public bool testStarted = false;
	public int topSeeksCompleted = 0;
	public int bottomSeeksCompleted = 0;
	public int centersCompleted = 0;
	
	// Use this for initialization
	void OnGUI () {
		if (!testStarted) {
			if (GUI.Button(new Rect(0,0,200,200), "Start Test")) {
				Scheduler.StartCoroutine(RunCalibrationStressTest());
			}
		}
		else {
			GUI.Box(new Rect(0,0,300,40), "Top Seeks Completed : " + topSeeksCompleted);
			GUI.Box(new Rect(0,40,300,40), "Bottom Seeks Completed : " + bottomSeeksCompleted);
			GUI.Box(new Rect(0,80,300,40), "Centers Completed : " + centersCompleted);
		}
	}
	
	IEnumerator RunCalibrationStressTest() {
		while (true) {
			yield return Scheduler.StartCoroutine(pc.CalibrateTop(pc.originalPrinter));
			topSeeksCompleted++;
			centersCompleted++;
			yield return Scheduler.StartCoroutine(pc.CalibrateBottom(pc.originalPrinter));
			bottomSeeksCompleted++;
			centersCompleted++;
		}
	}
#endif
}
