using UnityEngine;
using System.Collections;

public class Sphere3 {
	///This class uses the coordinates explained at http://mathworld.wolfram.com/SphericalCoordinates.html
	/// with Z and Y swapped, because we are using the Unity coordinate system in which Y is the vertical axis.
	
	/// <summary>
	/// Phi - The angle from the vertical axis (Y) in radians.
	/// </summary>
	public float phi; 
	/// <summary>
	/// Theta - The angle from the main horizontal axis (X) in radians.
	/// </summary>
	public float theta; 
	/// <summary>
	/// Radius - Distance from origin.
	/// </summary>
	public float r; 
	
	/// <summary>
	/// Initializes a new instance of Sphere3 and copies the 
	/// Sphere3 's' variable passed to it.
	/// </summary>
	/// <param name='s'>
	/// S.
	/// </param>
	public Sphere3(Sphere3 s) {
		this.phi = s.phi;
		this.theta = s.theta;
		this.r = s.r;
	}
	
	/// <summary>
	/// Initializes a new instance of the <see cref="Sphere3"/> class.
	/// </summary>
	public Sphere3() {
		this.phi = 0f;
		this.theta = 0f;
		this.r = 0f;
	}
	
	/// <summary>
	/// Initializes a new instance of the <see cref="Sphere3"/> class.
	/// </summary>
	/// <param name='r'>
	/// Radius.
	/// </param>
	/// <param name='theta'>
	/// Theta in radians.
	/// </param>
	/// <param name='phi'>
	/// Phi in radians.
	/// </param>
	public Sphere3(float r, float theta, float phi) {
		this.r = r;
		this.theta = theta;
		this.phi = phi;
	}
	
	public override string ToString() {
		return "r = " + this.r + ", theta = " + theta + ", phi = " + phi;
	}
	
	public Vector3 ToCartesian() {
		return Sphere3.SphericalToCartesian(this);
	}
	
	/// <summary>
	/// Cartesians to the cartPos vector3 to spherical coordinates.
	/// </summary>
	/// <returns>
	/// The to spherical.
	/// </returns>
	/// <param name='cartPos'>
	/// Cart position.
	/// </param>
	public static Sphere3 CartesianToSpherical(Vector3 cartPos) {
		Sphere3 spherePos = new Sphere3();
		spherePos.r = Mathf.Sqrt(cartPos.x * cartPos.x + cartPos.y * cartPos.y + cartPos.z * cartPos.z);
		spherePos.theta = Mathf.Atan2(cartPos.z, cartPos.x);
		spherePos.phi = Mathf.Acos(cartPos.y/spherePos.r) ; 
		return spherePos;
	}
	
	/// <summary>
	/// Converts a sphere3 to cartesian coordinates.
	/// </summary>
	/// <returns>
	/// The to cartesian.
	/// </returns>
	/// <param name='spherePos'>
	/// Sphere position.
	/// </param>
	public static Vector3 SphericalToCartesian(Sphere3 spherePos) {
		Vector3 cartPos = new Vector3();
		cartPos.x = spherePos.r * Mathf.Cos(spherePos.theta ) * Mathf.Sin(spherePos.phi );
		cartPos.y = spherePos.r * Mathf.Cos(spherePos.phi);
		cartPos.z = spherePos.r * Mathf.Sin(spherePos.theta ) * Mathf.Sin(spherePos.phi );
		return cartPos;
	}
	
}