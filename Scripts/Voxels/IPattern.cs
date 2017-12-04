using UnityEngine;
using System.Collections;

public interface IPattern {
	Vector3 Size {get;}
	bool LockToGrid {get;}
	string FileExtension {get;}
	IEnumerator CreateMeshObject(GameObject targetObj, Material mat, int layer, bool collide);
	IPatternConverter CreateConverter(MeshManager manager, Vector3 position, Quaternion rotation, Vector3 scale, byte blockMat, Material meshMat);
	float GetMinScaleIncrement(InterfaceController controller);
	float GetMinScale(InterfaceController controller);
	void WriteToFile(string filePath);
	bool IsLoaded();
}
