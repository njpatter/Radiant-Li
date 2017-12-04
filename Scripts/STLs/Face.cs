using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class Face {
	public Vector3 normal;
	public Vector3[] vertices = new Vector3[3];
	public Vector3 lowerBound;
	public Vector3 upperBound;
	
	public Face(Vector3 p0, Vector3 p1, Vector3 p2) {
		vertices[0] = p0;
		vertices[1] = p1;
		vertices[2] = p2;
		FindBounds();
	}
	
	public void Init() {
		normal = CalculateFaceNormal();
		FindBounds();
	}
	
	void FindBounds() {
		lowerBound = new Vector3 (Mathf.Min(vertices[0].x,vertices[1].x,vertices[2].x),
		                          Mathf.Min(vertices[0].y,vertices[1].y,vertices[2].y),
		                          Mathf.Min(vertices[0].z,vertices[1].z,vertices[2].z));
		upperBound = new Vector3 (Mathf.Max(vertices[0].x,vertices[1].x,vertices[2].x),
		                          Mathf.Max(vertices[0].y,vertices[1].y,vertices[2].y),
		                          Mathf.Max(vertices[0].z,vertices[1].z,vertices[2].z));
	}
	
	const float kNormalScaling = 1024.0f;
	Vector3 CalculateFaceNormal() {
		Vector3 i = vertices[0] ;
		Vector3 j = vertices[1];
		Vector3 k = vertices[2];
		
		Vector3 u = j - i;
		Vector3 v = k - i;
		
		Vector3 tempNormal = new Vector3(0,0,0);
		
		tempNormal.x = kNormalScaling * u.y * v.z - kNormalScaling * u.z * v.y;
		tempNormal.y = kNormalScaling * u.z * v.x - kNormalScaling * u.x * v.z;
		tempNormal.z = kNormalScaling * u.x * v.y - kNormalScaling * u.y * v.x;
		
		// TODO: This could be broken! Verify that zeros aren't
		// being generated.
		if (tempNormal == Vector3.zero) {
			Text.Error(string.Format("Zero normal detected.\nx: {0}\ty: {1}\tz: {2}",
				vertices[0], vertices[1], vertices[2]));
		}
		tempNormal = Vector3.Normalize(tempNormal);
		return tempNormal;
	}
	
		
	public static Vector3 FindLowerBound(List<Face> someFaces) {
		Vector3 lowerBound = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
		foreach(Face f in someFaces) {
			lowerBound.x = Mathf.Min(f.lowerBound.x, lowerBound.x);
			lowerBound.y = Mathf.Min(f.lowerBound.y, lowerBound.y);
			lowerBound.z = Mathf.Min(f.lowerBound.z, lowerBound.z);
		}
		return lowerBound;
	}
	
	public static Vector3 FindUpperBound(List<Face> someFaces) {
		Vector3 upperBound = new Vector3(float.MinValue, float.MinValue, float.MinValue);
		foreach(Face f in someFaces) {
			upperBound.x = Mathf.Max(f.upperBound.x, upperBound.x);
			upperBound.y = Mathf.Max(f.upperBound.y, upperBound.y);
			upperBound.z = Mathf.Max(f.upperBound.z, upperBound.z);
		}
		return upperBound;
	}
}
