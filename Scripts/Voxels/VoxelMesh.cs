using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// The chunk mesh, containing a block of voxels.
/// </summary>
public class VoxelMesh : System.Object {
	static readonly Vector4[] kShaderAxes = {
		// Left, right, bot, top, back, front
		// Horizontal, Vertical
		new Vector4( 0, 0, -1, 0), new Vector4(0, 1, 0, 0),	// left
		new Vector4( 0, 0, -1, 0), new Vector4(0, 1, 0, 0),	// right
		new Vector4( 1, 0,  0, 0), new Vector4(0, 0, 1, 0),	// bot
		new Vector4( 1, 0,  0, 0), new Vector4(0, 0, 1, 0),	// top
		new Vector4( 1, 0,  0, 0), new Vector4(0, 1, 0, 0), // back
		new Vector4(-1, 0,  0, 0), new Vector4(0, 1, 0, 0)	// front
	};

	public static readonly float[] kFaceScale = {
		0.7f,	// left
		0.55f,	// right
		0.3f,	// bot
		1.0f,	// top
		0.4f,	// back
		0.85f	// front
	};

	public static MeshManager manager;
	public static float voxelScale = 1.0f;
	public static int layer;

	Mesh m_mesh;
	public int3 position = new int3();
	Vector3 m_worldPosition;
	
	const int kNumBuildMats = (int)MeshManager.kVoxelMatCount;
	public static Material blockMaterial;
	public static Color[] blockTints = new Color[] {
		new Color(254.0f/255.0f, 41.0f/255.0f, 43.0f/255.0f, 1.0f), 
		new Color(250.0f/255.0f, 252.0f/255.0f, 109.0f/255.0f, 1.0f),
		new Color(2.0f/255.0f, 117.0f/255.0f, 246.0f/255.0f, 1.0f),
		new Color(0.5f, 0.5f, 0.5f, 1.0f)
	};
	bool[][] m_hasBuild = new bool[kNumBuildMats][];
	
	const int kNumSpecialBlocks = 2;
	public static Color waterTint = new Color(224.0f/255.0f, 0.224f/255.0f, 0.224f/255.0f, 0.5f);
	public static Color patternTint = new Color(224.0f/255.0f, 0.224f/255.0f, 0.224f/255.0f, 0.5f);

	bool[] m_hasWater   = new bool[6];
	bool[] m_hasPattern = new bool[6];
	
	int[] m_buildOffsets     = new int[kNumBuildMats];
	const int kWaterOffset   = kNumBuildMats * 6;
	const int kPatternOffset = (kNumBuildMats + 1) * 6;
	const int kSubMeshCount  = (kNumBuildMats * kNumSpecialBlocks) * 6;
	
	public bool needsRegen = false;

	MaterialPropertyBlock[][] m_faceProperties;

	#region Initializers
	public VoxelMesh(Vector3 worldPosition) {
		m_worldPosition = worldPosition;
		for (int i = 0; i < 3; ++i) {
			position[i] = Mathf.FloorToInt(m_worldPosition[i] * voxelScale);
		}

		m_mesh = new Mesh();

		int horizAxisId = Shader.PropertyToID("_horizAxis");
		int vertAxisId = Shader.PropertyToID("_vertAxis");
		int tintId = Shader.PropertyToID("_tint");

		m_faceProperties = new MaterialPropertyBlock[6][];
		for (int face = 0; face < 6; ++face) {
			m_faceProperties[face] = new MaterialPropertyBlock[kNumBuildMats + 2];
			for (int blockMat = 0; blockMat < kNumBuildMats; ++blockMat) {
				m_faceProperties[face][blockMat] = new MaterialPropertyBlock();
				m_faceProperties[face][blockMat].AddVector(horizAxisId, kShaderAxes[face * 2]);
				m_faceProperties[face][blockMat].AddVector(vertAxisId,  kShaderAxes[face * 2 + 1]);
				m_faceProperties[face][blockMat].AddColor(tintId, blockTints[blockMat] * kFaceScale[face]);
			}

			m_faceProperties[face][kNumBuildMats] = new MaterialPropertyBlock();
			m_faceProperties[face][kNumBuildMats].AddVector(horizAxisId, kShaderAxes[face * 2]);
			m_faceProperties[face][kNumBuildMats].AddVector(vertAxisId,  kShaderAxes[face * 2 + 1]);
			m_faceProperties[face][kNumBuildMats].AddColor(tintId, waterTint * kFaceScale[face]);

			m_faceProperties[face][kNumBuildMats + 1] = new MaterialPropertyBlock();
			m_faceProperties[face][kNumBuildMats + 1].AddVector(horizAxisId, kShaderAxes[face * 2]);
			m_faceProperties[face][kNumBuildMats + 1].AddVector(vertAxisId,  kShaderAxes[face * 2 + 1]);
			m_faceProperties[face][kNumBuildMats + 1].AddColor(tintId, patternTint * kFaceScale[face]);
		}
		
		for (int i = 0; i < kNumBuildMats; ++i) {
			m_hasBuild[i] = new bool[6];
		}
		
		for (int i = 0; i < kNumBuildMats; ++i) {
			m_buildOffsets[i] = i * 6;
		}
	}

	public static void Cleanup() {
		manager      = null;
		sm_regenData = null;
	}
	#endregion
	
	byte VoxelAt(int3 pos, VoxelBlob blob) {
		return VoxelAt(pos.x, pos.y, pos.z, blob);
	}
	
	byte VoxelAt(int x, int y, int z, VoxelBlob blob) {
		return blob[x + position.x, y + position.y, z + position.z];
	}
	
	class RegenData {
		const int kIndexDim = 17;
		public List<int>[] tris = new List<int>[kSubMeshCount];
		public List<Vector3> verts = new List<Vector3>(800);
		public int[,,] vertIndices = new int[kIndexDim, kIndexDim, kIndexDim];
		public int2[][] negTriData = new int2[16][];
		public int2[][] posTriData = new int2[16][];
	

		public RegenData() {
			for (int i = 0; i < tris.Length; ++i) {
				tris[i] = new List<int>(1000);
			}
			
			negTriData[0]  = new int2[] {};
			negTriData[1]  = new int2[] {new int2(0, 0), new int2(1, 1), new int2(1, 0), 
										 new int2(0, 0), new int2(0, 1), new int2(1, 1)};
			negTriData[2]  = new int2[] {new int2(0, 1), new int2(1, 2), new int2(1, 1), 
										 new int2(0, 1), new int2(0, 2), new int2(1, 2)};
			negTriData[3]  = new int2[] {new int2(0, 0), new int2(1, 2), new int2(1, 0), 
										 new int2(0, 0), new int2(0, 2), new int2(1, 2)};
			negTriData[4]  = new int2[] {new int2(1, 0), new int2(2, 1), new int2(2, 0), 
										 new int2(1, 0), new int2(1, 1), new int2(2, 1)};
			negTriData[5]  = new int2[] {new int2(0, 0), new int2(2, 1), new int2(2, 0), 
										 new int2(0, 0), new int2(0, 1), new int2(2, 1)};
			negTriData[6]  = new int2[] {new int2(0, 1), new int2(1, 2), new int2(1, 1), 
										 new int2(0, 1), new int2(0, 2), new int2(1, 2),
										 new int2(1, 0), new int2(2, 1), new int2(2, 0), 
										 new int2(1, 0), new int2(1, 1), new int2(2, 1)};
			negTriData[7]  = new int2[] {new int2(0, 0), new int2(0, 2), new int2(2, 0),
										 new int2(0, 2), new int2(1, 2), new int2(1, 1),
										 new int2(2, 0), new int2(1, 1), new int2(2, 1)};
			negTriData[8]  = new int2[] {new int2(1, 1), new int2(2, 2), new int2(2, 1), 
										 new int2(1, 1), new int2(1, 2), new int2(2, 2)};
			negTriData[9]  = new int2[] {new int2(0, 0), new int2(1, 1), new int2(1, 0), 
										 new int2(0, 0), new int2(0, 1), new int2(1, 1),
										 new int2(1, 1), new int2(2, 2), new int2(2, 1), 
										 new int2(1, 1), new int2(1, 2), new int2(2, 2)};
			negTriData[10] = new int2[] {new int2(0, 1), new int2(2, 2), new int2(2, 1), 
										 new int2(0, 1), new int2(0, 2), new int2(2, 2)};
			negTriData[11] = new int2[] {new int2(0, 0), new int2(0, 2), new int2(2, 2),
										 new int2(1, 1), new int2(2, 2), new int2(2, 1),
										 new int2(1, 1), new int2(1, 0), new int2(0, 0)};
			negTriData[12] = new int2[] {new int2(1, 0), new int2(2, 2), new int2(2, 0), 
										 new int2(1, 0), new int2(1, 2), new int2(2, 2)};
			negTriData[13] = new int2[] {new int2(0, 0), new int2(2, 2), new int2(2, 0),
										 new int2(0, 0), new int2(0, 1), new int2(1, 1),
										 new int2(1, 1), new int2(1, 2), new int2(2, 2)};
			negTriData[14] = new int2[] {new int2(2, 0), new int2(0, 2), new int2(2, 2),
										 new int2(1, 0), new int2(1, 1), new int2(2, 0),
										 new int2(0, 1), new int2(0, 2), new int2(1, 1)};
			negTriData[15] = new int2[] {new int2(0, 0), new int2(2, 2), new int2(2, 0), 
										 new int2(0, 0), new int2(0, 2), new int2(2, 2)};
			
			for (int i = 0; i < 16; ++i) {
				int vertCount = negTriData[i].Length;
				posTriData[i] = new int2[vertCount];
				for (int face = 0; face < vertCount; face += 3) {
					posTriData[i][face] = negTriData[i][face];
					posTriData[i][face + 1] = negTriData[i][face + 2];
					posTriData[i][face + 2] = negTriData[i][face + 1];
				}
			}
		}
		
		public void Clear() {
			foreach (List<int> tri in tris) {
				tri.Clear();
			}
			verts.Clear();
			for (int x = 0; x < kIndexDim; ++x) {
				for (int y = 0; y < kIndexDim; ++y) {
					for (int z = 0; z < kIndexDim; ++z) {
						vertIndices[x, y, z] = -1;
					}
				}
			}
		}
		
		public int GetVertexIndex(int3 point) {
			return GetVertexIndex(point.x, point.y, point.z);
		}
		
		public int GetVertexIndex(int x, int y, int z) {
			try {
				if (vertIndices[x, y, z] == -1) {
					int index = verts.Count;
					verts.Add(new Vector3(x, y, z));
					vertIndices[x, y, z] = index;
					return index;
				}
				else {
					return vertIndices[x, y, z];
				}
			}
			catch (System.Exception e) {
				Text.Error(e.ToString());
				return 0;
			}
		}
	}
	
	static RegenData sm_regenData  = new RegenData();
	static bool      sm_regenInUse = false;

	void GenNegXFaces(int3 currVoxel, ref bool hasFaces, bool[] hasMatFace, int2[] indices, int triOffset) {
		int triIndex = 0;
		if (indices[triIndex] == int2.zero) {
			return;
		}
		hasFaces = true;
		hasMatFace[triIndex] = true;
		foreach(int2 pos in sm_regenData.negTriData[indices[triIndex].x]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x, currVoxel.y + pos.x, currVoxel.z + pos.y));
		}
		foreach(int2 pos in sm_regenData.negTriData[indices[triIndex].y]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + 1, currVoxel.y + pos.x, currVoxel.z + pos.y));
		}
	}
	
	void GenPosXFaces(int3 currVoxel, ref bool hasFaces, bool[] hasMatFace, int2[] indices, int triOffset) {
		int triIndex = 1;
		if (indices[triIndex] == int2.zero) {
			return;
		}
		hasFaces = true;
		hasMatFace[triIndex] = true;
		foreach(int2 pos in sm_regenData.posTriData[indices[triIndex].x]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + 1, currVoxel.y + pos.x, currVoxel.z + pos.y));
		}
		foreach(int2 pos in sm_regenData.posTriData[indices[triIndex].y]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + 2, currVoxel.y + pos.x, currVoxel.z + pos.y));
		}
	}

	void GenNegYFaces(int3 currVoxel, ref bool hasFaces, bool[] hasMatFace, int2[] indices, int triOffset) {
		int triIndex = 2;
		if (indices[triIndex] == int2.zero) {
			return;
		}
		hasFaces = true;
		hasMatFace[triIndex] = true;
		foreach(int2 pos in sm_regenData.negTriData[indices[triIndex].x]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + pos.y, currVoxel.y, currVoxel.z + pos.x));
		}
		foreach(int2 pos in sm_regenData.negTriData[indices[triIndex].y]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + pos.y, currVoxel.y + 1, currVoxel.z + pos.x));
		}
	}

	void GenPosYFaces(int3 currVoxel, ref bool hasFaces, bool[] hasMatFace, int2[] indices, int triOffset) {
		int triIndex = 3;
		if (indices[triIndex] == int2.zero) {
			return;
		}
		hasFaces = true;
		hasMatFace[triIndex] = true;
		foreach(int2 pos in sm_regenData.posTriData[indices[triIndex].x]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + pos.y, currVoxel.y + 1, currVoxel.z + pos.x));
		}
		foreach(int2 pos in sm_regenData.posTriData[indices[triIndex].y]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + pos.y, currVoxel.y + 2, currVoxel.z + pos.x));
		}
	}

	void GenNegZFaces(int3 currVoxel, ref bool hasFaces, bool[] hasMatFace, int2[] indices, int triOffset) {
		int triIndex = 4;
		if (indices[triIndex] == int2.zero) {
			return;
		}
		hasFaces = true;
		hasMatFace[triIndex] = true;
		foreach(int2 pos in sm_regenData.negTriData[indices[triIndex].x]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + pos.x, currVoxel.y + pos.y, currVoxel.z));
		}
		foreach(int2 pos in sm_regenData.negTriData[indices[triIndex].y]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + pos.x, currVoxel.y + pos.y, currVoxel.z + 1));
		}
	}
	
	void GenPosZFaces(int3 currVoxel, ref bool hasFaces, bool[] hasMatFace, int2[] indices, int triOffset) {
		int triIndex = 5;
		if (indices[triIndex] == int2.zero) {
			return;
		}
		hasFaces = true;
		hasMatFace[triIndex] = true;
		foreach(int2 pos in sm_regenData.posTriData[indices[triIndex].x]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + pos.x, currVoxel.y + pos.y, currVoxel.z + 1));
		}
		foreach(int2 pos in sm_regenData.posTriData[indices[triIndex].y]) {
			sm_regenData.tris[triIndex + triOffset].Add(sm_regenData.GetVertexIndex(
				currVoxel.x + pos.x, currVoxel.y + pos.y, currVoxel.z + 2));
		}
	}

	/// <summary>
	/// Builds the chunk's mesh.
	/// </summary>
	public void RegenerateVisualMesh(float aVoxelScale) {
		//float startTime = Time.realtimeSinceStartup;
		VoxelBlob blob = manager.m_blob;
		int3 blobSize = blob.size;
		if (sm_regenInUse) {
			Debug.Log("Somehow calling two regens at the same time");
			//throw new System.Exception("Cannot run two RegenerateVisualMesh at the same time. If needed, make more static RegenData");
			return;
		}
		sm_regenInUse = true;
		sm_regenData.Clear();

		needsRegen = false;

		int chunkSize = manager.chunkSize;
		
		for (int i = 0; i < 6; ++i) {
			m_hasBuild[0][i] = false;
			m_hasWater[i] = false;
			m_hasPattern[i] = false;
		}

		m_mesh.Clear();
		m_mesh.subMeshCount = kSubMeshCount;
		
		int3 currVoxel = new int3();
		
		bool hasFaces = false;
		
		int3 maxVals = blobSize - position;
		for (int i = 0; i < 3; ++i) {
			maxVals[i] = Mathf.Min(maxVals[i], chunkSize);
		}
		//Debug.Log("Starting main block");
		for (int z = 0; z < maxVals.z && z + position.z < blobSize.z; z += 2) {
			for (int y = 0; y < maxVals.y && y + position.y < blobSize.y; y += 2) {
				for (int x = 0; x < maxVals.x && x + position.x < blobSize.x; x += 2) {
					currVoxel.x = x;
					currVoxel.y = y;
					currVoxel.z = z;
	
					int2[][] buildIndex = new int2[kNumBuildMats][];
					for (int i = 0; i < kNumBuildMats; ++i) {
						buildIndex[i] = new int2[6];
					}
					int2[] waterIndex = new int2[6];
					int2[] patternIndex = new int2[6];
					
					byte voxel;
					
					for (int i = 0; i < 2 && x + i + position.x < blobSize.x; ++i) {
						for (int j = 0; j < 2 && y + j + position.y < blobSize.y; ++j) {
							for (int k = 0; k < 2 && z + k + position.z < blobSize.z; ++k) {
								int3 curr = new int3(currVoxel.x + i, currVoxel.y + j, currVoxel.z + k);
								voxel = VoxelAt(curr, blob);
								if (voxel != 0) {
									int2[] voxelIndex = null;
									switch (voxel) {
										case MeshManager.kVoxelWater:
											voxelIndex = waterIndex;
											break;
										case MeshManager.kVoxelObjectDef:
											voxelIndex = patternIndex;
											break;
										default:
											voxelIndex =  buildIndex[(int)(voxel - MeshManager.kVoxelFirstMat)];
											break;
									}
									if (curr.x + position.x == 0 || VoxelAt(curr.x - 1, curr.y, curr.z, blob) == 0)
										voxelIndex[0][i] += (1 << (j * 2 + k));
									if (curr.x + position.x == blobSize.x - 1 || VoxelAt(curr.x + 1, curr.y, curr.z, blob) == 0)
										voxelIndex[1][i] += (1 << (j * 2 + k));
									if (curr.y + position.y == 0 || VoxelAt(curr.x, curr.y - 1, curr.z, blob) == 0)
										voxelIndex[2][j] += (1 << (k * 2 + i));
									if (curr.y + position.y == blobSize.y - 1 || VoxelAt(curr.x, curr.y + 1, curr.z, blob) == 0)
										voxelIndex[3][j] += (1 << (k * 2 + i));
									if (curr.z + position.z == 0 || VoxelAt(curr.x, curr.y, curr.z - 1, blob) == 0)
										voxelIndex[4][k] += (1 << (i * 2 + j));
									if (curr.z + position.z == blobSize.z - 1 || VoxelAt(curr.x, curr.y, curr.z + 1, blob) == 0)
										voxelIndex[5][k] += (1 << (i * 2 + j));
								}
							}
						}
					}
					
					for (int i = 0; i < kNumBuildMats; ++i) {
						GenNegXFaces(currVoxel, ref hasFaces, m_hasBuild[i], buildIndex[i], m_buildOffsets[i]);
						GenPosXFaces(currVoxel, ref hasFaces, m_hasBuild[i], buildIndex[i], m_buildOffsets[i]);
						GenNegYFaces(currVoxel, ref hasFaces, m_hasBuild[i], buildIndex[i], m_buildOffsets[i]);
						GenPosYFaces(currVoxel, ref hasFaces, m_hasBuild[i], buildIndex[i], m_buildOffsets[i]);
						GenNegZFaces(currVoxel, ref hasFaces, m_hasBuild[i], buildIndex[i], m_buildOffsets[i]);
						GenPosZFaces(currVoxel, ref hasFaces, m_hasBuild[i], buildIndex[i], m_buildOffsets[i]);
					}
					
					GenNegXFaces(currVoxel, ref hasFaces, m_hasWater, waterIndex, kWaterOffset);
					GenNegXFaces(currVoxel, ref hasFaces, m_hasPattern, patternIndex, kPatternOffset);
					GenPosXFaces(currVoxel, ref hasFaces, m_hasWater, waterIndex, kWaterOffset);
					GenPosXFaces(currVoxel, ref hasFaces, m_hasPattern, patternIndex, kPatternOffset);
					
					GenNegYFaces(currVoxel, ref hasFaces, m_hasWater, waterIndex, kWaterOffset);
					GenNegYFaces(currVoxel, ref hasFaces, m_hasPattern, patternIndex, kPatternOffset);
					GenPosYFaces(currVoxel, ref hasFaces, m_hasWater, waterIndex, kWaterOffset);
					GenPosYFaces(currVoxel, ref hasFaces, m_hasPattern, patternIndex, kPatternOffset);
					
					GenNegZFaces(currVoxel, ref hasFaces, m_hasWater, waterIndex, kWaterOffset);
					GenNegZFaces(currVoxel, ref hasFaces, m_hasPattern, patternIndex, kPatternOffset);
					GenPosZFaces(currVoxel, ref hasFaces, m_hasWater, waterIndex, kWaterOffset);
					GenPosZFaces(currVoxel, ref hasFaces, m_hasPattern, patternIndex, kPatternOffset);
				}
			}
		}

		//Debug.Log("Finished main block");

		if(!hasFaces) {
			sm_regenInUse = false;
			return;
		}
		m_mesh.name 	 = "Visual Mesh";
		m_mesh.vertices  = sm_regenData.verts.ToArray();
		for (int i = 0; i < kSubMeshCount; ++i) {
			if (sm_regenData.tris[i].Count > 0)
				m_mesh.SetTriangles(sm_regenData.tris[i].ToArray(), i);
		}
		if (m_mesh.vertexCount > 0) {
			manager.EnableMesh(this);
		}
		else {
			manager.DisableMesh(this);
		}

		m_mesh.Optimize();
		sm_regenInUse = false;
	}
	
	public void Update() {
		for (int face = 0; face < 6; ++face) {
			for (int blockMat = 0; blockMat < kNumBuildMats; ++blockMat) {
				if (m_hasBuild[blockMat][face]) {
					Graphics.DrawMesh(m_mesh, m_worldPosition, Quaternion.identity,
					                  blockMaterial, layer, null, face + m_buildOffsets[blockMat], 
					                  m_faceProperties[face][blockMat]);
				}
			}
			if (m_hasWater[face]) {
				Graphics.DrawMesh(m_mesh, m_worldPosition, Quaternion.identity, 
				                  blockMaterial, layer, null, face + kWaterOffset,
				                  m_faceProperties[face][kNumBuildMats]);
			}
			if (m_hasPattern[face]) {
				Graphics.DrawMesh(m_mesh, m_worldPosition, Quaternion.identity, 
				                  blockMaterial, layer, null, face + kPatternOffset, 
				                  m_faceProperties[face][kNumBuildMats + 1]);
			}
		}
	}
}
