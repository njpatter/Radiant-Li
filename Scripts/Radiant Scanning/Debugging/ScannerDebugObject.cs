using UnityEngine;
using System.Collections;

public class ScannerDebugObject : MonoBehaviour {
	private Material decayMaterial;
	private float decayTime = 3f;

	IEnumerator Start () {
		decayMaterial = new Material(renderer.material);
		renderer.material= decayMaterial;
		float initialTime = Time.realtimeSinceStartup;
		while(decayMaterial.color.a > 0f) {
			Color tempColor = decayMaterial.color;
			tempColor.a = 1f - ((Time.realtimeSinceStartup - initialTime) / decayTime);
			decayMaterial.color = tempColor;
			yield return null;
		}
		Destroy(gameObject);
	}
	
	// Update is called once per frame
	void Update () {
	
	}
}
