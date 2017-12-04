using UnityEngine;
using System.Collections;

/// <summary>
/// Converts a texture of WI into a voxel layer.
/// </summary>
public class WiscToVoxels : MonoBehaviour { 
	
	public MeshManager myManager;
	public bool addBlob = false;
	
	public Texture2D image;
	public int targetVoxelLength;
	public Vector3 offset;
	public float redCutoff = 1.0f;
	public float greenCutoff = 1.0f;
	public float blueCutoff = 1.0f;
	public float alphaCutoff = 1.0f;
	
	public void Update() {
		if (addBlob) {
			addBlob = false;
			VoxelBlob tempBlob = new VoxelBlob(targetVoxelLength, 1, targetVoxelLength, false);
			myManager.AddVoxelBlob(Convert(tempBlob));
			
			Text.Log("added picture");
		}
	}
	
	public VoxelBlob Convert (VoxelBlob aPart) {
		float minLength = Mathf.Min(image.width, image.height);
		int conversionFactor = Mathf.FloorToInt((float)minLength / (float)targetVoxelLength);
		int voxelCount = 0;
		for (int aRow = 0; aRow < targetVoxelLength; ++aRow) {
			for (int aCol = 0; aCol < targetVoxelLength; ++aCol) {
				Color sourceColor = image.GetPixel(aCol * conversionFactor, aRow * conversionFactor);
				if (sourceColor.r > redCutoff && sourceColor.g > greenCutoff &&
					sourceColor.b > blueCutoff && sourceColor.a > alphaCutoff) {
					//Text.Log("A row = " + aRow + "   a col = " + aCol);
					//Text.Log(aPart[aRow,0,0]);
					voxelCount++;
					aPart[aRow, 0, aCol] = 1;
				}
			}
		}
		Text.Log("Created this many voxels from the picture " + voxelCount);
		return aPart;
	}
}
