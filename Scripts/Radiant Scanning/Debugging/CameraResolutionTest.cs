using UnityEngine;
using System.Collections;
using System.IO;

public class CameraResolutionTest : MonoBehaviour { 
	
	int pictureNumber = 1;
	
	public WebCamTexture aTex;
	// Use this for initialization
	void Start () {
		foreach(WebCamDevice d in WebCamTexture.devices) {
			Text.Log(d.name);
		}
		aTex = new WebCamTexture("Live! Cam Sync HD VF0770 #6", 1280, 720) ; //WebCamTexture.devices[1].name);
		//aTex = new WebCamTexture(WebCamTexture.devices[1].name, 1200, 720, 30);
		renderer.material.mainTexture = aTex;
		aTex.Play();
		Debug.Log(aTex.width + "   " + aTex.height);
	}
	
	// Update is called once per frame
	void Update () {
		if (Input.GetKeyDown(KeyCode.Space)) {
			Texture2D t2 = new Texture2D(aTex.width, aTex.height);
			t2.SetPixels(aTex.GetPixels());
			Debug.Log("Taking picture " + pictureNumber);
			TakePictureAndSave(t2);
			
		}
	}
	
	void TakePictureAndSave(Texture2D aTex) {
		byte[] data = aTex.EncodeToPNG();
		string fileName;
		if (pictureNumber < 10) fileName = "0" + pictureNumber;
		else fileName = ""+pictureNumber;
		
		System.IO.File.WriteAllBytes(Application.dataPath + "/" + fileName + ".png", data);
		pictureNumber++;
	}
	
}
