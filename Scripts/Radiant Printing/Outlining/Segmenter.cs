using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Segmenter : System.Object {
	// Each row is { # points, x0, y0, …, xn, yn }
	static readonly float[][] kContourTable = new float[][] {
		// 0: Nothing
		new float[] { 0 },
		// 1: \ bottom
		new float[] { 1.0f, -1.0f, -0.5f, -0.5f, -1.0f },
		// 2: / bottom
		new float[] { 1.0f, -0.5f, -1.0f, 0.0f, -0.5f },
		// 3: -
		new float[] { 1.0f, -1.0f, -0.5f, 0.0f, -0.5f },
		// 4: \ top
		new float[] { 1.0f, -0.5f, 0.0f, 0.0f, -0.5f},
		// 5: //
		new float[] { 2.0f, -1.0f, -0.5f, -0.5f, 0.0f, -0.5f, -1.0f, 0, -0.5f },
		// 6: |
		new float[] { 1.0f, -0.5f, -1.0f, -0.5f, 0 },
		// 7: / top
		new float[] { 1.0f, -1.0f, -0.5f, -0.5f, 0 },
		// 8: / top
		new float[] { 1.0f, -1.0f, -0.5f, -0.5f, 0 },
		// 9: |
		new float[] { 1.0f, -0.5f, -1.0f, -0.5f, 0 },
		// 10: \\
		new float[] { 2.0f, -1.0f, -0.5f, -0.5f, -1.0f, -0.5f, 0, 0, -0.5f },
		// 11: \ top
		new float[] { 1.0f, -0.5f, 0.0f, 0.0f, -0.5f},
		// 12: -
		new float[] { 1.0f, -1.0f, -0.5f, 0.0f, -0.5f },
		// 13: / bottom
		new float[] { 1.0f, -0.5f, -1.0f, 0.0f, -0.5f },
		// 14: \ bottom
		new float[] { 1.0f, -1.0f, -0.5f, -0.5f, -1.0f },
		// 15: Nothing
		new float[] { 0 }
	};
	
	public List<CartesianSegment> GetSegments(VoxelRegion region) {
		int flag;
		List<CartesianSegment> result = new List<CartesianSegment>();
		List<int2> voxelsToRemove = new List<int2>();
		
		// NOTE: 0,0 is in the bottom-left.
		for (int x = 0; x < region.width; ++x) {
			for (int z = 0; z < region.depth; ++z) {
				flag =     ((x < 1 || z < 1)  ? 0 : (region[x - 1, z - 1] != 0 ? 0x1 : 0x0))
					|  ((z < 1)               ? 0 : (region[x    , z - 1] != 0 ? 0x2 : 0x0))
					|                               (region[x    , z    ] != 0 ? 0x4 : 0x0)
					|  ((x < 1)               ? 0 : (region[x - 1, z    ] != 0 ? 0x8 : 0x0));
				
				int x0 = Mathf.Clamp(x - 1, 0, region.width - 1);
				int z0 = Mathf.Clamp(z - 1, 0, region.depth - 1);
				byte usedMaterial = MathUtil.ModeIgnore(0, 
              		region[x0, z0],
					region[x , z0],
					region[x , z ],
					region[x0, z ]);
				if (usedMaterial == 0) continue;
				
				float[] tableRow = kContourTable[flag];
				int numSegments = (int)tableRow[0];

				if (numSegments != 0) {
					voxelsToRemove.Add(new int2(x, z));
					voxelsToRemove.Add(new int2(x0, z));
					voxelsToRemove.Add(new int2(x, z0));
					voxelsToRemove.Add(new int2(x0, z0));
				}
				
				for (int aSegment = 0; aSegment < numSegments; ++aSegment) {
					Vector2 p0 = VoxelBlob.kVoxelSizeInMm * new Vector2(
							x + tableRow[aSegment * 4 + 1] , z + tableRow[aSegment * 4 + 2]);
					Vector2 p1 = VoxelBlob.kVoxelSizeInMm * new Vector2(
							x + tableRow[aSegment * 4 + 3], z + tableRow[aSegment * 4 + 4]);
					CartesianSegment product = new CartesianSegment(p0, p1, usedMaterial);
					result.Add(product);
				}
			}
		}

		foreach(int2 vox in voxelsToRemove) {
			region[vox.x, vox.y] = 0;
		}
		
		//Text.Log(@"{0} segment{1} found in outlines and {2} voxels removed.", result.Count, Text.S(result.Count), voxelsToRemove.Count);
		return result;
	}
}
