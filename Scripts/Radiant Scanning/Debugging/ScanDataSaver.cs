using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class ScanDataSaver {
	
	public string worldCoordSave = "SpatialPointsSave.txt";
	public string imageCoordSave = "ImagePointsSave.txt";
	
	public void Save (List<Vector3> spatialPoints, List<Vector3> imagePoints) {
		List<Vector2> modList = new List<Vector2>();
		foreach(Vector3 v in imagePoints) {
			Vector2 v2 = new Vector2(v.x, v.y);
			modList.Add(v2);
		}
		Save(spatialPoints, modList);
	}
	
	public void Save (List<Vector3> spatialPoints, List<Vector2> imagePoints) {
		StreamWriter sw = new StreamWriter(worldCoordSave);
		foreach(Vector3 v in spatialPoints) {
			sw.WriteLine(v.x);
			sw.WriteLine(v.y);
			sw.WriteLine(v.z);
		}
		sw.Close();
		
		sw = new StreamWriter(imageCoordSave);
		foreach(Vector2 v in imagePoints) {
			sw.WriteLine(v.x);
			sw.WriteLine(v.y);
		}
		sw.Close();
	}
	
	public void Load (out List<Vector3> spatialPoints, out List<Vector2> imagePoints) {
		spatialPoints = new List<Vector3>();
		imagePoints = new List<Vector2>();
		StreamReader sr = new StreamReader(worldCoordSave);
		while(!sr.EndOfStream) {
			Vector3 aPoint = new Vector3(
				float.Parse(sr.ReadLine()),
				float.Parse(sr.ReadLine()),
				float.Parse(sr.ReadLine()));
			spatialPoints.Add(aPoint);
		}
		sr.Close();
		
		sr = new StreamReader(imageCoordSave);
		while(!sr.EndOfStream) {
			Vector2 aPoint = new Vector3(
				float.Parse(sr.ReadLine()),
				float.Parse(sr.ReadLine()));
			imagePoints.Add(aPoint);
		}
		sr.Close();
	}
	
}
