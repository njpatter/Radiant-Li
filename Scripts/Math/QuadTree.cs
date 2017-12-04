using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class QuadTree {
	public const int kMaxDepth = 5;
	public const int kMaxAllowableSegments = 10;
	
	public int depth;
	public Vector2 upperBound;
	public Vector2 lowerBound;
	public QuadTree[,] branches;
	
	public List<CartesianSegment> segments;
	
	/// <summary>
	/// Creates a Quadtree and creates branches to handle incoming line segments until kMaxDepth is hit.
	/// </summary>
	/// <param name='segmentList'>
	/// Segment list.
	/// </param>
	public QuadTree(List<CartesianSegment> segmentList) {
		segments = segmentList;
		depth = 0;
		SetBounds();
		if (segments.Count > kMaxAllowableSegments) SplitToBranches();
	}
	
	/// <summary>
	/// Initializes a new QuadTree Branch... should only call this as part of an expanding quadtree.
	/// </summary>
	/// <param name='segmentList'>
	/// Line Segment list.
	/// </param>
	/// <param name='aLowerBound'>
	/// A lower bound.
	/// </param>
	/// <param name='anUpperBound'>
	/// An upper bound.
	/// </param>
	/// <param name='aDepth'>
	/// A branch depth.
	/// </param>
	private QuadTree(List<CartesianSegment> segmentList, Vector2 aLowerBound, Vector2 anUpperBound, int aDepth) {
		segments = segmentList;
		depth = aDepth;
		lowerBound = aLowerBound;
		upperBound = anUpperBound;
		if (aDepth < kMaxDepth && segments.Count > kMaxAllowableSegments) SplitToBranches();
	}
	
	/// <summary>
	/// Splits the current QuadTree into branches and distributes the Cartesian Segments.
	/// </summary>
	private void SplitToBranches() {
		float width = (upperBound.x - lowerBound.x) / 2f;
		float height = (upperBound.y - lowerBound.y) / 2f;
		branches = new QuadTree[2, 2];
		
		for (int row = 0; row < 2; row++) {
			for (int col = 0; col < 2; col++) {
				List<CartesianSegment> branchSegments = new List<CartesianSegment>();
				Vector2 aBranchLowerBound = new Vector2(lowerBound.x + (float)col * width, 
					lowerBound.y + (float)row * height); 
				Vector2 aBranchUpperBound = new Vector2(lowerBound.x + ((float)col + 1) * width, 
					lowerBound.y + ((float)row + 1) * height); 
				
				foreach(CartesianSegment cs in segments) {
					if (IsLineInBounds(cs, aBranchLowerBound, aBranchUpperBound)) branchSegments.Add(cs);
				}
				branches[row, col] = new QuadTree(branchSegments, aBranchLowerBound, aBranchUpperBound, depth + 1);
			}
		}
	}
	
	/// <summary>
	/// Sets the bounds for the initial Quadtree Parent.
	/// </summary>
	private void SetBounds() {
		upperBound = Vector3.one * float.MinValue;
		lowerBound = Vector3.one * float.MaxValue;
		foreach(CartesianSegment cs in segments) {
			upperBound.x = Mathf.Max(new float[3]{upperBound.x, cs.p0.x, cs.p1.x});
			upperBound.y = Mathf.Max(new float[3]{upperBound.y, cs.p0.y, cs.p1.y});
			
			lowerBound.x = Mathf.Min(new float[3]{lowerBound.x, cs.p0.x, cs.p1.x});
			lowerBound.y = Mathf.Min(new float[3]{lowerBound.y, cs.p0.y, cs.p1.y});
		}
		upperBound += Vector2.one * 0.0001f;
		lowerBound -= Vector2.one * 0.0001f;
	}
	
	/// <summary>
	/// Determines whether this line segment is in the specified bounds.
	/// </summary>
	/// <returns>
	/// <c>true</c> if this instance is line in bounds the specified aSegment aLowerBound anUpperBound; otherwise, <c>false</c>.
	/// </returns>
	/// <param name='aSegment'>
	/// If set to <c>true</c> a segment.
	/// </param>
	/// <param name='aLowerBound'>
	/// If set to <c>true</c> a lower bound.
	/// </param>
	/// <param name='anUpperBound'>
	/// If set to <c>true</c> an upper bound.
	/// </param>
	private bool IsLineInBounds(CartesianSegment aSegment, Vector2 aLowerBound, Vector2 anUpperBound) {
		if (aSegment.p0.x >= aLowerBound.x && aSegment.p0.x <= anUpperBound.x) {
			if (aSegment.p0.y >= aLowerBound.y && aSegment.p0.y <= anUpperBound.y) {
				return true;
			}
		}
		if (aSegment.p1.x >= aLowerBound.x && aSegment.p1.x <= anUpperBound.x) {
			if (aSegment.p1.y >= aLowerBound.y && aSegment.p1.y <= anUpperBound.y) {
				return true;
			}
		}
		return false;
	}
	
	public bool BranchContains(CartesianSegment aSegment) {
		return IsLineInBounds(aSegment, lowerBound, upperBound);
	}

	public bool BranchContains(Vector2 aPoint) {
		CartesianSegment cs = new CartesianSegment(aPoint, aPoint, 0);
		return IsLineInBounds(cs, lowerBound, upperBound);
	}
	
	public List<QuadTree> FindAllBranches() {
		List<QuadTree> allBranches = new List<QuadTree>();
		if (branches != null) {
			foreach(QuadTree qt in branches) {
				allBranches.AddRange(qt.FindAllBranches());
			}
		}
		else {
			allBranches.Add(this);
		}
		return allBranches;
	}
	
	public CartesianSegment FindSegmentWithSharedEndPoint(CartesianSegment aSegment) {
		if(!BranchContains(aSegment)) return null;
		if(branches == null) {
			foreach(CartesianSegment sc in segments) {
				int pc = sc.SharedPointCount(aSegment);
				if(pc == 1) return sc;
			}
		}
		else {
			for (int row = 0; row < 2; row++) {
				for (int col = 0; col < 2; col++) {
					CartesianSegment cs = branches[row, col].FindSegmentWithSharedEndPoint(aSegment);
					if(cs != null) return cs;
				}
			}
		}
		return null;
	}

	public CartesianSegment FindSegmentClosestToPoint(Vector2 aPoint) {
		//if (!BranchContains(aPoint)) return null;
		//Debug.Log("Found that " + aPoint + " was in branch " + this.ToString());
		if(branches == null) {
			float minDistance = float.MaxValue;
			CartesianSegment closestCs = null;
			foreach(CartesianSegment cs in segments) {
				float dist = cs.MinSqrMagnitudeFromEndpoint(aPoint);
				if(dist < minDistance) {
					minDistance = dist;
					closestCs = cs;
					//Debug.Log("This point was closer " + cs);
				}
			}
			return closestCs;
		}
		else {
			CartesianSegment closestCs = null;
			float minDistance = float.MaxValue;
			CartesianSegment[] branchSegments = new CartesianSegment[4];
			for (int row = 0; row < 2; row++) {
				for (int col = 0; col < 2; col++) {
					branchSegments[2 * row + col] = branches[row, col].FindSegmentClosestToPoint(aPoint);
				}
			}
			foreach(CartesianSegment cs in branchSegments) {
				if (cs == null) continue;
				float dist = cs.MinSqrMagnitudeFromEndpoint(aPoint);
				if (dist < minDistance) {
					minDistance = dist;
					closestCs = cs;
					//Debug.Log("This point was closer " + cs);
				}
			}
			return closestCs;
		}
	}
	
	public void RemoveSegmentFromTree(CartesianSegment aSegment) {
		bool segmentInList = segments.Remove(aSegment);
		if(branches == null || !segmentInList) return;
		for (int row = 0; row < 2; row++) {
			for (int col = 0; col < 2; col++) {
				branches[row, col].RemoveSegmentFromTree(aSegment);
			}
		}
	}

	public override string ToString ()
	{
		return string.Format ("[QuadTree], Lower bound = {0}, Upper bound = {1}, branch count = {2}, segment count = {3}",
		                      lowerBound, upperBound, (branches == null ? "0" : "4"), segments.Count);
	}
}
