using UnityEngine;

public struct int3 {
	public int x;
	public int y;
	public int z;
	
	public int this[int i] {
		get {
			switch (i) {
				case 0: return x;
				case 1: return y;
				case 2: return z;
				default: throw new System.Exception("Index out of range");
			}
		}
		set {
			switch (i) {
				case 0:
					x = value;
					break;
				case 1:
					y = value;
					break;
				case 2:
					z = value;
					break;
				default:
					throw new System.Exception("Index out of range");
			}
		}
	}
	
	public static int2 GetOtherAxes(int dim) {
		switch (dim) {
			case 0:
				return new int2(1, 2);
			case 1:
				return new int2(2, 0);
			case 2:
				return new int2(0, 1);
			default:
				return new int2(-1, -1);
		}
	}
	
	public int3(int x, int y, int z) {
		this.x = x;
		this.y = y;
		this.z = z;
	}
	
	public int3(int3 other) {
		x = other.x;
		y = other.y;
		z = other.z;
	}
	
	public int3(Vector3 other) {
		x = (int)other.x;
		y = (int)other.y;
		z = (int)other.z;
	}
	
	public static int3 operator+(int3 i, int3 j) {
		return new int3(i.x + j.x, i.y + j.y, i.z + j.z);
	}
	
	public static int3 operator-(int3 i, int3 j) {
		return new int3(i.x - j.x, i.y - j.y, i.z - j.z);
	}
	
	public static int3 operator*(int3 i, int j) {
		return new int3(i.x * j, i.y * j,  i.z * j);
	}
	
	public static int3 operator/(int3 i, int j) {
		return new int3(i.x / j, i.y / j,  i.z / j);
	}
	
	public static bool operator==(int3 i, int3 j) {
		return i.x == j.x && i.y == j.y && i.z == j.z;
	}
	
	public static bool operator!=(int3 i, int3 j) {
		return i.x != j.x || i.y != j.y || i.z != j.z;
	}
	
	public static int3 zero { get { return new int3(0, 0, 0); } }
	public static int3 one  { get { return new int3(1, 1, 1); } }
	
	public override bool Equals(object obj) {
		if (obj.GetType() == typeof(int3)) {
			int3 other = (int3) obj;
			return (x == other.x && y == other.y && z == other.z);
		}
		return base.Equals (obj);
	}
	
	//this is here because otherwise unity complains about me not overriding it since I overrode Equals
	public override int GetHashCode() {
		return base.GetHashCode ();
	}
	
	public override string ToString() {
		return "(" + x + ", " + y + ", " + z + ")";
	}
	
	public Vector3 ToVector3() {
		return new Vector3(x, y, z);
	}
}
