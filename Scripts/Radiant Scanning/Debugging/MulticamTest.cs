using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MulticamTest : MonoBehaviour {
	
	public WebCamDevice[] cams;
	public WebCamTexture[] textures; 
	public string viewInfo; 
	public List<GameObject> views;
	public GameObject dummyView;
	public Vector3[] viewPositions;
	
	// Use this for initialization
	IEnumerator Start () {
		views = new List<GameObject>();
		yield return null;
		cams = WebCamTexture.devices;
		textures = new WebCamTexture[cams.Length];
		for(int i = 0; i < cams.Length; i++) {
			if(!cams[i].name.Contains("Live")) continue;
			Debug.Log("Starting " + cams[i].name + " cams(" + i + ") at " + viewPositions[i]);
			views.Add(Instantiate(dummyView, viewPositions[i], dummyView.transform.rotation) as GameObject);
			WebCamTexture wct = new WebCamTexture(cams[i].name, 1280, 720, 10);
			wct.Play();
			textures[i] = wct;
			views[views.Count - 1].renderer.material.mainTexture = wct;
		}

	}

	void OnDestroy() {
		foreach(WebCamTexture tex in textures) {
			tex.Stop();
		}
	}
}
