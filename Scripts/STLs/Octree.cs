using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class Octree {
	public const int kMaximumDepth = 6;
	public const int kMaximumFaces = 100;
	
	public List<Face> faces;
	public Octree[,,] childBranches;
	public Vector3 lowerBound;
	public Vector3 upperBound;
	
	private int m_depth;
	
	public Octree(int aDepth, List<Face> someFaces, Vector3 aLowerBound, Vector3 aUpperBound) {
		m_depth = aDepth;
		lowerBound = aLowerBound;
		upperBound = aUpperBound;
		//if (aDepth == 0) Text.Log("Starting to populate base Octree with face count : " + someFaces.Count);
		if (kMaximumDepth > m_depth && someFaces.Count > kMaximumFaces) {
			childBranches = new Octree[2,2,2];
			int faceCountPassedToChildren = 0;
			for (int x = 0; x < 2; x++) {
				for (int y = 0; y < 2; y++) {
					for (int z = 0; z < 2; z++) {
						Vector3 childLowerBound = new Vector3(lowerBound.x + 0.5f * (upperBound.x - lowerBound.x) * (float)x, 
															  lowerBound.y + 0.5f * (upperBound.y - lowerBound.y) * (float)y, 
															  lowerBound.z + 0.5f * (upperBound.z - lowerBound.z) * (float)z); 
						Vector3 childUpperBound = new Vector3(lowerBound.x + 0.5f * (upperBound.x - lowerBound.x) * ((float)x+1f), 
															  lowerBound.y + 0.5f * (upperBound.y - lowerBound.y) * ((float)y+1f), 
															  lowerBound.z + 0.5f * (upperBound.z - lowerBound.z) * ((float)z+1f)); 
						
						List<Face> facesToGiveChild = new List<Face>();
						for(int f = 0; f < someFaces.Count; f++) {
							//Text.Log(overlapingBounds(someFaces[f].lowerBound, someFaces[f].upperBound, childLowerBound, childUpperBound));
							if (OverlapingBounds(someFaces[f].lowerBound, someFaces[f].upperBound, childLowerBound, childUpperBound)) {
								facesToGiveChild.Add(someFaces[f]);
								faceCountPassedToChildren++;
								//someFaces.RemoveAt(f);
								//f--;
							}
						}
						//Text.Log("Setting up child " + x + ", " + y + ", " + z + " at depth " + (depth + 1) + " with : " + facesToGiveChild.Count + " faces");
						childBranches[x,y,z] = new Octree(m_depth + 1, facesToGiveChild, childLowerBound, childUpperBound);
					}
				}
			}
			if(someFaces.Count > faceCountPassedToChildren && childBranches != null) {
				Text.Error("Octree did not set up correctly!! It has a depth of " + m_depth + " and " + someFaces.Count + " faces and childbranches is not null");
				foreach(Octree o in childBranches) {
					Text.Warning("I am at depth : " + o.m_depth + " and I have : " + o.faces.Count + " faces");
				}
			}
			someFaces = null;
		} else {
			faces = someFaces;
		}
	}
	
	bool OverlapingBounds(Vector3 aLower, Vector3 aUpper, Vector3 branchLower, Vector3 branchUpper) {
		return !(aUpper.x < branchLower.x || aLower.x > branchUpper.x || 
		    aUpper.y < branchLower.y || aLower.y > branchUpper.y || 
		    aUpper.z < branchLower.z || aLower.z > branchUpper.z);
	}
	
	/// <summary>
	/// Determines if a point is in the bounds of the Octree branch.
	/// </summary>
	/// <returns>
	/// Is the point in bounds?.
	/// </returns>
	/// <param name='aPoint'>
	/// If set to <c>true</c> a point.
	/// </param>
	public bool IsPointInBounds(Vector3 aPoint) {
		return !(aPoint.x < lowerBound.x || aPoint.x > upperBound.x ||
			aPoint.y < lowerBound.y || aPoint.y > upperBound.y ||
			aPoint.z < lowerBound.z || aPoint.z > upperBound.z);
	}
	
	bool IsAASegmentIntersectingBranch(int segmentAxis, Vector3 lineLoc, float start, float end) {
		if (!isLineIntersectingBranch(lineLoc, segmentAxis)) return false;
		if (start >= lowerBound[segmentAxis] && start <= upperBound[segmentAxis]) return true;
		if (end >= lowerBound[segmentAxis] && end <= upperBound[segmentAxis]) return true;
		return (start < lowerBound[segmentAxis] && end > upperBound[segmentAxis]);
	}
	
	private bool isLineIntersectingBranch(Vector3 lineStart, int chosenCastingAxis) {
		///We are going to set up the Octree after orientation is chosen so that we can use rays cast along each axis 
		for(int i = 0; i < 3; i++) {
			if (i== chosenCastingAxis) continue;
			if (lineStart[i] < lowerBound[i] || lineStart[i] > upperBound[i]) return false;
		}
		return true;
	}
	
	public List<Face> FindIntersectingFacesOnAASegment(int segmentAxis, Vector3 lineLoc, float start, float end) {
		HashSet<Face> faceSet = new HashSet<Face>();
		InternallyFindIntersectingFacesOnAASegment(segmentAxis, lineLoc, start, end, faceSet);
		List<Face> result = new List<Face>(faceSet);
		return result;
	}
	
	public List<Face> FindAllPossibleIntersectingFaces(Vector3 start, int chosenCastingAxis) {
		HashSet<Face> faceSet = new HashSet<Face>();
		InternallyFindAllPossibleIntersectingFaces(start, chosenCastingAxis, faceSet);
		List<Face> result = new List<Face>(faceSet);
		return result;
	}
	
	void InternallyFindIntersectingFacesOnAASegment(int segmentAxis, Vector3 lineLoc, float start, float end, HashSet<Face> result) {
		if (!IsAASegmentIntersectingBranch(segmentAxis, lineLoc, start, end)) return;
		
		if (childBranches != null) {
			foreach (Octree child in childBranches) {
				child.InternallyFindIntersectingFacesOnAASegment(segmentAxis, lineLoc, start, end, result);
			}
		} 
		else foreach (Face aFace in faces) {
			// We found faces; only add non-duplicates.
			if (!result.Contains(aFace)) result.Add(aFace);
		}
	}
	
	void InternallyFindAllPossibleIntersectingFaces(Vector3 start, int chosenCastingAxis, HashSet<Face> result) {
		if (!isLineIntersectingBranch(start, chosenCastingAxis)) return;
			
		if (childBranches != null) {
			for (int x = 0; x < 2; x++) {
				for (int y = 0; y < 2; y++) {
					for (int z = 0; z < 2; z++) {
						childBranches[x,y,z].InternallyFindAllPossibleIntersectingFaces(start, chosenCastingAxis, result);
					}
				}
			}
		} 
		else foreach (Face aFace in faces) {
			// We found faces; only add non-duplicates.
			if (!result.Contains(aFace)) result.Add(aFace);
		}
	}
}
