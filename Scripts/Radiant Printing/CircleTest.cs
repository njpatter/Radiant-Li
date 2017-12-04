using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CircleTest : MonoBehaviour {
	
	public List<int2> currentPoints = new List<int2>();
	public List<int2> doublePoints = new List<int2>();
	public List<int2> tripleOrMore = new List<int2>();
	public List<int2> allPoints = new List<int2>();
	public HashSet<int2> squarePoints = new HashSet<int2>();
	
	public float currentRadius = 0f;
	private float delta = 1.0f;//0.501f;
	
	delegate int Converter(float a);
	Converter c;
	
	float nozzleSize = 0.7f;
	
	// Use this for initialization
	IEnumerator Start () {
		int largestRadius = 25;
		int2 center = new int2(75, 0);
		c = Mathf.FloorToInt;
		
		for(int x = 0; x < largestRadius; x++) {
			for (int y = 0; y < largestRadius; y++) {
				squarePoints.Add(new int2(center.x + x, center.y + y));
				squarePoints.Add(new int2(center.x - x, center.y + y));
				squarePoints.Add(new int2(center.x + x, center.y - y));
				squarePoints.Add(new int2(center.x - x, center.y - y));
					
			}
		}//*/
		
		List<int2> lastPoints = new List<int2>();
		for(int i = 0; i < largestRadius / nozzleSize; i++) {
			List<int2> newPoints = MultipleSamples((float)i*nozzleSize, center);
			foreach(int2 np in newPoints) {
				if (lastPoints.Contains(np)) {
					if (doublePoints.Contains(np)) {
						tripleOrMore.Add(np);
					} else doublePoints.Add(np);
				}
				squarePoints.Remove(np);
			}//*/
			lastPoints = newPoints;
			currentRadius = (float)i*nozzleSize;
			currentPoints = newPoints;
			allPoints.AddRange(currentPoints);
			yield return null; // new WaitForSeconds(1f);
		}//*/
		
		/*for (int i = 0; i < largestRadius; i++) {
			List<int2> newPoints = MultipleSamples(i, center);
			List<int2> newPoints2 = GetCircleNearly(i, center);
			
			foreach(int2 np in newPoints) {
				if (!newPoints2.Contains(np)) doublePoints.Add(np); 
			}
			currentRadius = (float)i;
			yield return new WaitForSeconds(1f);
		}//*/
		Text.Warning("DONE!!!!... finished with " + doublePoints.Count + 
			" doubled points and " + tripleOrMore.Count + " tripled points");
		yield return null;
	}
	
	
	List<int2> GetCircleNearly (float radius, int2 center) {
		List<int2> voxels = new List<int2>(); 
		for (int y = Mathf.FloorToInt(-radius - 1); y < Mathf.FloorToInt(radius + 1); y++) {
			for (int x = Mathf.FloorToInt(-radius - 1); x < Mathf.FloorToInt(radius + 1); x++) {  
				
				if ( Mathf.Abs( Mathf.Sqrt(x*x+y*y) - radius) <= delta) {  
					voxels.Add(new int2(center.x + x, center.y + y));
				}
			}
		}
		return voxels;
	}
	
	List<int2> GetCircleBresenhemFreq(int radius, int2 center) {
		HashSet<int2> voxels = new HashSet<int2>();
		int f = 1 - radius;
		int ddfx = 1;
		int ddfy = -2 * radius;
		float x = 0;
		float y = radius;
		
		voxels.Add(new int2(c(center.x + x), c(center.y + radius)));
		voxels.Add(new int2(c(center.x + x), c(center.y - radius)));
		voxels.Add(new int2(c(center.x + radius), c(center.y)));
		voxels.Add(new int2(c(center.x - radius), c(center.y)));
		
		while (x < y) {
			if (f >= 0) {
				y -= 0.5f;
				ddfy += 1;
				f += ddfy;
			}
			x += 0.5f;
			ddfx += 1;
			f += ddfx;
			
			foreach(int2 i in CreatePointsToAdd(x, y, center)) voxels.Add(i);
		}
		List<int2> rVal = new List<int2>();
		foreach(int2 i in voxels) rVal.Add(i);
		return rVal;
	}
	
	List<int2> GetCircleBresenhem (int radius, int2 center) {
		List<int2> voxels = new List<int2>();
		int f = 1 - radius;
		int ddfx = 1;
		int ddfy = -2 * radius;
		int x = 0;
		int y = radius;
		
		voxels.Add(new int2(center.x + x, center.y + radius));
		voxels.Add(new int2(center.x + x, center.y - radius));
		voxels.Add(new int2(center.x + radius, center.y));
		voxels.Add(new int2(center.x - radius, center.y));
		
		while (x < y) {
			if (f >= 0) {
				y--;
				ddfy += 2;
				f += ddfy;
			}
			x++;
			ddfx += 2;
			f += ddfx;
			voxels.AddRange(CreatePointsToAdd(x, y, center));
		}
		
		return voxels;
	}
	
	List<int2> MidpointCircle(int radius, int2 center) {
		List<int2> voxels = new List<int2>();
		int x = 0;
		int y = radius;
		int d = 1-radius;
		int deltaE = 3;
		int deltaSE = 5 - radius * 2;
		
		voxels.AddRange(CreatePointsToAdd(x,y,center));
		
		while (y > x) {
			if (d < 0) {
				d += deltaE;
				deltaE += 2;
				deltaSE += 2;
				x++;
			}
			else {
				d += deltaSE;
				deltaE += 2;
				deltaSE += 4;
				x++;
				y--;
				
			}
			voxels.AddRange(CreatePointsToAdd(x,y,center));
		}
		
		return voxels;
	}
	
	List<int2> MultipleSamples(float radius, int2 center) {
		HashSet<int2> voxels = new HashSet<int2>();
		HashSet<int2> axes = new HashSet<int2>();
		HashSet<Vector2> missedAxes = new HashSet<Vector2>();
		
		int sampleCount = Mathf.CeilToInt(((float)radius * Mathf.PI * 2f)*16f);
		for(int i = 0; i < sampleCount; i++) {
			float angle = (float)i * (Mathf.PI *2f / (float)sampleCount);
			float x = (float)radius * Mathf.Cos(angle);
			float y = (float)radius * Mathf.Sin(angle);
			
			int xi = Mathf.RoundToInt(x); // + (x < 0 ? 0.001f : -0.001f));
			int yi = Mathf.RoundToInt(y); // + (y < 0 ? 0.001f : -0.001f));
			//if (xi == 0 || yi == 0) Text.Log(x + "   " + y);
			float xd = x - (float)xi;
			float yd = y - (float)yi;
			//Text.Log("distance " + (xd*xd + yd*yd ) + "  allowable " + (delta* delta));
			if (xd*xd + yd*yd <= delta*delta) {
				voxels.Add(new int2(xi + center.x, yi + center.y));
				if (xi ==0 || yi == 0) axes.Add(new int2(xi + center.x, yi + center.y));
			}
			if (xi ==0 || yi == 0) missedAxes.Add(new Vector2(x , y));
			
		}
		//Text.Log("Added " + voxels.Count + " points");
		if (axes.Count != 4) {
			Text.Error("Missing " + (4 - axes.Count) + " points on the axes, points collected were : ");
			foreach(int2 i in axes) Text.Log(i);
			//foreach(Vector2 v in missedAxes) Text.Log("Choice " + v.x + ", " + v.y);
		}
		List<int2> returnVals = new List<int2>();
		foreach(int2 i in voxels) returnVals.Add(i);
		return returnVals;
	}
	
	List<int2> CreatePointsToAdd(int x, int y, int2 center) {
		List<int2> voxels = new List<int2>();
		voxels.Add(new int2(center.x + x, center.y + y));
		voxels.Add(new int2(center.x - x, center.y + y));
		voxels.Add(new int2(center.x + x, center.y - y));
		voxels.Add(new int2(center.x - x, center.y - y));
		voxels.Add(new int2(center.x + y, center.y + x));
		voxels.Add(new int2(center.x - y, center.y + x));
		voxels.Add(new int2(center.x + y, center.y - x));
		voxels.Add(new int2(center.x - y, center.y - x));
		return voxels;
	}
	
	List<int2> CreatePointsToAdd(float x, float y, int2 center) {
		List<int2> voxels = new List<int2>();
		voxels.Add(new int2(c(center.x + x), c(center.y + y)));
		voxels.Add(new int2(c(center.x - x), c(center.y + y)));
		voxels.Add(new int2(c(center.x + x), c(center.y - y)));
		voxels.Add(new int2(c(center.x - x), c(center.y - y)));
		voxels.Add(new int2(c(center.x + y), c(center.y + x)));
		voxels.Add(new int2(c(center.x - y), c(center.y + x)));
		voxels.Add(new int2(c(center.x + y), c(center.y - x)));
		voxels.Add(new int2(c(center.x - y), c(center.y - x)));
		return voxels;
	}
	
	
	void OnDrawGizmos() {
		//return;
		Gizmos.color = Color.yellow;
		foreach(int2 i in squarePoints) {
			Gizmos.DrawSphere(new Vector3(i.x, 0f, i.y), 0.5f);
		}//*/
		
		Gizmos.color = Color.red;
		foreach(int2 i in doublePoints) {
			Gizmos.DrawSphere(new Vector3(i.x, 0f, i.y), 0.5f);
		}//*/
		
		Gizmos.color = Color.cyan;
		foreach(int2 i in tripleOrMore) {
			Gizmos.DrawSphere(new Vector3(i.x, 0f, i.y), 0.5f);
		}
		
		Gizmos.color = Color.green;
		foreach(int2 i in currentPoints) {
			Gizmos.DrawSphere(new Vector3(i.x, 0f, i.y), 0.5f);
		}//*/
		
		
		
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(new Vector3(75,0,0), currentRadius);
		
	}
}
