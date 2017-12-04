using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class Outline : System.Object {
	
	public List<CartesianSegment> segments;
	public byte material;
	
	public Outline(byte materialOutlined) {
		segments = new List<CartesianSegment>();
		material = materialOutlined;
	}
	
	public Outline(List<CartesianSegment> outlineSegments, byte materialOutlined) {
		segments = outlineSegments;
		material = materialOutlined;
	}
	
	public void AddSegment(CartesianSegment aSegment) {
		if (segments.Count == 0 || CartesianSegment.Approximately(aSegment.p0, segments[segments.Count - 1].p1)) {
			segments.Add(aSegment);
			return;
		}
		aSegment.FlipPoints();
		Contract.Assert(CartesianSegment.Approximately(aSegment.p0, segments[segments.Count - 1].p1), 
			@"Added a segment to an outline that did not have matching end " +
				"to previous segment, Segments : {0}, {1}", aSegment.ToString(), segments[segments.Count - 1].ToString());
		segments.Add(aSegment);
	}
	
	public void InsertSegmentAtStart(CartesianSegment aSegment) {
		if(segments.Count == 0 || CartesianSegment.Approximately(aSegment.p1, segments[0].p0)) {
			segments.Insert(0, aSegment);
			return;
		}
		aSegment.FlipPoints();
		Contract.Assert(CartesianSegment.Approximately(aSegment.p1, segments[0].p0), 
		                @"Insertion failure. s[0].p1 = {0}, {1}; aSegment.p0 = {0}, {1}",
		                segments[0].p1.x, segments[0].p1.y,
		                aSegment.p0.x, aSegment.p0.x);
		segments.Insert(0, aSegment);
	}
}
