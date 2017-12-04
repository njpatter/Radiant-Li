using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// A line-segment in Cartesian coordinates.
/// </summary>
[System.Serializable]
public class CartesianSegment : System.Object {
	public Vector2 p0;
	public Vector2 p1;
	public byte material;
	
	/// <summary>
	/// Initializes a new instance of the <see cref="CartesianSegment"/> class.
	/// </summary>
	/// <description>
	/// Note that though these are line segments, we're treating them as being
	/// directional vectors—and therefore not reordering them.
	/// </description>
	/// <param name='p0'>
	/// The initial point.
	/// </param>
	/// <param name='p1'>
	/// The terminal point
	/// </param>
	/// <param name='material'>
	/// The segment's material
	/// </param>
	public CartesianSegment(Vector2 p0, Vector2 p1, byte material) {
		this.p0 = p0;
		this.p1 = p1;
		this.material = material;
	}
	
	public int SharedPointCount(CartesianSegment otherSegment) {
		return Convert.ToInt32(Approximately(p0, otherSegment.p0)) + Convert.ToInt32(Approximately(p0, otherSegment.p1)) +
			Convert.ToInt32(Approximately(p1, otherSegment.p0)) + Convert.ToInt32(Approximately(p1, otherSegment.p1));
	}

	public float MinSqrMagnitudeFromEndpoint(Vector2 toPoint) {
		return Mathf.Min((p0 - toPoint).sqrMagnitude, (p1 - toPoint).sqrMagnitude);
	}
	
	public void FlipPoints() {
		Vector2 pTemp = p1;
		p1 = p0;
		p0 = pTemp;
	}
	
	public Vector2 UnitDirection() {
		return (this.p1 - this.p0).normalized;
	}
	
	public override string ToString() {
		return string.Format("CartesianSegment with p0 = {0} and p1 = {1}", p0, p1);
	}
	
	public static bool Approximately(Vector2 v0, Vector2 v1) {
		return Mathf.Approximately(v0.x, v1.x) && Mathf.Approximately(v0.y, v1.y);
	}
}
