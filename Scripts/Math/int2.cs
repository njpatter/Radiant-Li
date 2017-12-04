using UnityEngine;

public struct int2 {
	public int x;
	public int y;
	
	public int this[int i] {
		get {
			switch (i) {
				case 0:
					return x;
				case 1:
					return y;
				default:
					throw new System.Exception("Index out of range: " + i);
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
				default:
					throw new System.Exception("Index out of range: " + i);
			}
		}
	}
	
	public int2(int x, int y) {
		this.x = x;
		this.y = y;
	}
	
	public int2(int2 other) {
		x = other.x;
		y = other.y;
	}
	
	public int2(Vector2 other) {
		x = (int)other.x;
		y = (int)other.y;
	}
	
	public static int2 operator+(int2 i, int2 j) {
		return new int2(i.x + j.x, i.y + j.y);
	}
	
	public static int2 operator-(int2 i, int2 j) {
		return new int2(i.x - j.x, i.y - j.y);
	}
	
	public static int2 operator*(int2 i, int j) {
		return new int2(i.x * j, i.y * j);
	}
	
	public static int2 operator/(int2 i, int j) {
		return new int2(i.x / j, i.y / j);
	}
	
	public static bool operator==(int2 i, int2 j) {
		return i.x == j.x && i.y == j.y;
	}
	
	public static bool operator!=(int2 i, int2 j) {
		return i.x != j.x || i.y != j.y;
	}
	
	public static int2 zero { get { return new int2(0, 0); } }
	public static int2 one { get { return new int2(1, 1); } }
	
	public override bool Equals(object obj) {
		if (obj.GetType() == typeof(int2)) {
			int2 other = (int2) obj;
			return (x == other.x && y == other.y);
		}
		return base.Equals (obj);
	}
	
	//this is here because otherwise unity complains about me not overriding it since I overrode Equals
	public override int GetHashCode() {
		return base.GetHashCode();
	}

	public override string ToString() {
		return "(" + x + ", " + y + ")";
	}
}

