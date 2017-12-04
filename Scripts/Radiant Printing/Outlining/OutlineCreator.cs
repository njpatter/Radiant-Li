using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class OutlineCreator {
	public List<Outline> CreateOutlines (List<CartesianSegment> layerSegments, Printer forPrinter) {
		List<Outline> layerOutlines = new List<Outline>();
		List<List<CartesianSegment>> matLines = new List<List<CartesianSegment>>();
		
		for (int mat = 1; mat < 5; mat++) {
			/// Grab all of the line segments for this material type 
			matLines.Add(CollectMaterialSegments((byte)mat, layerSegments));
			if(matLines[mat - 1].Count == 0) continue;
			
			// Create a quadtree for all of the line segments of mat type
			QuadTree qt = new QuadTree(matLines[mat - 1]);

			CartesianSegment currentSegment = null;

			PrinterExtruder[] extrudersForMaterial = forPrinter.GetExtrudersWithMaterial((byte)mat);
			float bestDistance = float.MaxValue;
			foreach(PrinterExtruder pe in extrudersForMaterial) {
				Vector2 extruderPosition = (Vector2)forPrinter.GetExtruderCartesianPosition(pe);
				extruderPosition += Vector2.one * forPrinter.platformRadiusInMm;
				CartesianSegment aSegment = qt.FindSegmentClosestToPoint(extruderPosition);
				
				//Debug.Log("Starting search with extruder " + pe.id + " at position " + extruderPosition + " and found best Segment = " + aSegment);

				if (aSegment != null &&
					((extruderPosition - aSegment.p0).sqrMagnitude < bestDistance ||
				    (extruderPosition - aSegment.p1).sqrMagnitude < bestDistance)) 
				{
					bestDistance = Mathf.Min((extruderPosition - aSegment.p0).sqrMagnitude,
					                         (extruderPosition - aSegment.p1).sqrMagnitude);
					currentSegment = aSegment;
					//Debug.LogWarning("Started at ext pos = " + extruderPosition + " and selected " + currentSegment);
				}
			}
			
			// Make complete outlines by ordering connected segments
			layerOutlines.Add(new Outline((byte)mat));
			if (currentSegment == null) {
				Text.Warning("Search for closest line segment returned null, grabbing first point in outline, " + qt.segments[0].ToString());
				currentSegment = qt.segments[0];
			}
			layerOutlines[layerOutlines.Count - 1].AddSegment(currentSegment);
			qt.RemoveSegmentFromTree(currentSegment);
			//Debug.Log("Starting at position " + aStartingPoint + " and found the closest point to be " + currentSegment);

			
			while (qt.segments.Count > 0) {
				CartesianSegment nextSegment = null;
				if (currentSegment != null) {
					nextSegment = qt.FindSegmentWithSharedEndPoint(currentSegment);
				}
				if (nextSegment != null 
				   && (CartesianSegment.Approximately(nextSegment.p0, currentSegment.p1)
				    || CartesianSegment.Approximately(nextSegment.p1, currentSegment.p1)))
				{
					// If the quadtree returns a connected segment, add it to the current outline and then search again
					layerOutlines[layerOutlines.Count - 1].AddSegment(nextSegment);
					currentSegment = nextSegment;
					qt.RemoveSegmentFromTree(currentSegment);
				} 
				else {
					// Check to see if there are any segments that match the initial segment
					
					nextSegment = qt.FindSegmentWithSharedEndPoint(layerOutlines[layerOutlines.Count - 1].segments[0]);
					if (nextSegment != null) {
						layerOutlines[layerOutlines.Count - 1].InsertSegmentAtStart(nextSegment);
						currentSegment = null;
						qt.RemoveSegmentFromTree(nextSegment);
						
					} 
					else { // Start a new outline!
						currentSegment = qt.FindSegmentClosestToPoint(
							layerOutlines[layerOutlines.Count - 1].segments[layerOutlines[layerOutlines.Count - 1].segments.Count - 1].p1); 
						if (currentSegment == null) currentSegment = qt.segments[0];
						layerOutlines.Add(new Outline((byte)mat));
						layerOutlines[layerOutlines.Count - 1].AddSegment(currentSegment);
						qt.RemoveSegmentFromTree(currentSegment);
					}
				}
			}
			
		}
		layerOutlines = CollapseOutlines(layerOutlines);
		return layerOutlines;
	}
	
	private List<Outline> CollapseOutlines(List<Outline> outlines) {
		foreach(Outline o in outlines) {
			//int segmentCount = o.segments.Count;
			for(int i = 1; i < o.segments.Count; i++) {
				if (o.segments[i - 1].UnitDirection() == o.segments[i].UnitDirection()) {
					o.segments[i - 1].p1 = o.segments[i].p1;
					o.segments.RemoveAt(i);
					i--;
				}
			}
		}
		return outlines;
	}
	
	public List<CartesianSegment> CollectMaterialSegments(byte targetMaterial, 
		List<CartesianSegment> layerSegments) 
	{
		List<CartesianSegment> matSegments = new List<CartesianSegment>();
		for(int i = 0; i < layerSegments.Count; i++) {
			if (layerSegments[i].material == targetMaterial) {
				matSegments.Add(layerSegments[i]);
				layerSegments.RemoveAt(i);
				i--;
			}
		}
		return matSegments;
	}
}
