using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Dynamically creates and destroys terrain meshes.
/// </summary>
public class MeshManager : MonoBehaviour {
	public const int   kTileNumber  = 16;
	public const float kImageSize   = 256.0f;
	public const float kTileSize    = (float)kTileNumber / kImageSize;
	
	public const string kOnForceRefresh = "OnForceRefresh";
	public const string kOnSetVoxel		= "OnSetVoxel";
	public const string kOnLoadUpdate   = "OnLoadUpdate";
	
	public const byte kVoxelEmpty			= 0;
	public const byte kVoxelFirstMat 		= 1;
	/// <summary>
	/// The number of materials we support.
	/// </summary>
	public const byte kVoxelMatCount		= 4;
	public const byte kVoxelFirstSpecial	= 128;
	public const byte kVoxelWater			= kVoxelFirstSpecial;
	public const byte kVoxelObjectDef		= kVoxelFirstSpecial + 1;
	public const byte kVoxelSubtract		= 255; //the voxel used in blob addition to set a block back to 0
	
	public bool useTestBlob = true;
	
	public bool useFpsTestBlob = false;
	
	public int3 minVoxelBounds { get { return m_blob.minVoxelBounds; } }
	public int3 maxVoxelBounds { get { return m_blob.maxVoxelBounds; } }
	
	/// <summary>
	/// What scale are we working at? 1.0f is standard (0.5mm); 2.0 is twice that (1.0mm).
	/// </summary>
	public float voxelScale = 1.0f;

	/// <summary>
	/// The layer for all voxel chunks. Should be one value.
	/// </summary>
	public LayerMask voxelLayer;

	/// <summary>
	/// The material used on voxel chunks.
	/// </summary>
	public Material voxelMaterial;
	
	/// <summary>
	/// The length of one chunk axis. Final chunk size is
	/// chunkSize x chunkSize x chunkSize.
	/// </summary>
	public int   chunkSize    = 16;
	
	/// <summary>
	/// Determines how many chunks we need to load
	/// (see Awake(), where the chunks required is 
	/// calculated).
	/// </summary>
	public float viewDistance = 50.0f;
	
	/// <summary>
	/// The voxel data we're editing.
	/// </summary>
	public VoxelBlob m_blob;
	
	public Vector3 blobLowerBound {
		get {
			return m_trans.position;
		}
	}
	
	public Vector3 blobUpperBound {
		get {
			return new Vector3(m_blob.width, m_blob.height, m_blob.depth) * voxelScale;
		}
	}
	
	//BoxCollider[,,] m_colliderCache;
	BoxCollider[] m_colliderCache;
	VoxelMesh[,,] m_visualCache;
	int3 m_visualCacheSize = new int3();
	int m_needsRegenCount = 0;
	const int kColliderDistance = 2;
	const int kColliderCacheDim = kColliderDistance * 2 + 1;
	
	Transform m_trans;
	Transform m_cameraTrans;
	int3   m_chunkPositionCurrent;

	List<VoxelMesh> m_enabledMeshes;
	
	/// <summary>
	/// The redoable sequence of changes to the voxel blob.
	/// </summary>
	Stack<IDelta> m_deltaHistory;
	
	/// <summary>
	/// The undone sequence of changes to the voxel blob.
	/// </summary>
	Stack<IDelta> m_deltaFuture;
	
	void Awake() {
		Dispatcher.AddListener(MenuController.kEventNewBlob, ClearMeshes);

		m_trans          = transform;
		m_cameraTrans    = Camera.main.transform;
		m_deltaFuture = new Stack<IDelta>();
		m_deltaHistory = new Stack<IDelta>();
		m_enabledMeshes = new List<VoxelMesh>();
		
		if (useTestBlob) m_blob = VoxelBlob.NewTestDisc();
		else if(useFpsTestBlob) m_blob = VoxelBlob.NewFpsTestBlob();
		else m_blob = new VoxelBlob(VoxelBlob.kTestSize, VoxelBlob.kTestSize, VoxelBlob.kTestSize, true);

		string[] launchArguments = System.Environment.GetCommandLineArgs();
		if (launchArguments != null) {
			for(int i = launchArguments.Length - 1; i > -1; i--) {
				if (launchArguments[i].Contains(".radiant") || launchArguments[i].Contains(".Radiant")) {
					if (System.IO.File.Exists(launchArguments[i])) {
						Dispatcher<string>.Broadcast(MenuController.kEventLoadScene, launchArguments[i]);
						i = -1;
					}
				}
			}
		}

		m_colliderCache = new BoxCollider[kColliderCacheDim * kColliderCacheDim * kColliderCacheDim];
		m_chunkPositionCurrent = WorldToChunk(m_cameraTrans.position);
		
		// NOTE: When we load data in the future, we'll need to find a suitable
		// location for the player.
		CreateCollisionCache();
		Scheduler.StartCoroutine(LoadVisualChunks());
		Scheduler.StartCoroutine(UpdateVisualChunks());
		Dispatcher.Broadcast(kOnForceRefresh);
	}

	void OnDestroy() {
		Dispatcher.RemoveListener(MenuController.kEventNewBlob, ClearMeshes);
		VoxelMesh.Cleanup();
	}

	#region Updates
	public void ForceVisualRefresh() {
		foreach(VoxelMesh vm in m_visualCache) {
			MarkChunkForRegen(vm);
		}
		Dispatcher.Broadcast(kOnForceRefresh);
	}

	void Update() {
		for (int i = 0; i < m_enabledMeshes.Count; i++) {
			m_enabledMeshes[i].Update();
		}
	}

	void FixedUpdate() {
		if (!pauseUpdateCollision) {
			Profiler.BeginSample("UpdateCollisionBlocks");
			UpdateCollisionBlocks();
			Profiler.EndSample();
		}
		Profiler.BeginSample("UpdateMovement");
		UpdateMovement();
		Profiler.EndSample();
	}
	#endregion
	
	#region Chunk Management
	public const float kMaxLoadTime = 1.0f / 50.0f;

	public void EnableMesh(VoxelMesh m) {
		if (!m_enabledMeshes.Contains(m)) {
			m_enabledMeshes.Add(m);
		}
	}

	/// <summary>
	/// Clears the drawn meshes.
	/// </summary>
	/// <description>
	/// Just stop drawing them—don't need to clear the objects 
	/// because we'll regenerate them later as needed.
	/// </description>
	void ClearMeshes() {
		m_enabledMeshes.Clear();
	}

	public void DisableMesh(VoxelMesh m) {
		m_enabledMeshes.Remove(m);
	}

	/// <summary>
	/// Loads the visual chunks as soon as it can fit them in without ruining framerates.
	/// </summary>
	/// <returns>
	/// The visual chunks.
	/// </returns>
	public IEnumerator LoadVisualChunks() {
		int colEnd = Mathf.CeilToInt(((float)m_blob.width / (float)chunkSize) * voxelScale);
		int layerEnd = Mathf.CeilToInt(((float)m_blob.height / (float)chunkSize) * voxelScale);
		int rowEnd = Mathf.CeilToInt(((float)m_blob.depth / (float)chunkSize) * voxelScale);

		Text.Log("Making the visual cache size: {0}x{1}x{2}", colEnd, layerEnd, rowEnd);
		m_visualCacheSize.x = colEnd;
		m_visualCacheSize.y = layerEnd;
		m_visualCacheSize.z = rowEnd;

		m_visualCache = new VoxelMesh[colEnd, layerEnd, rowEnd];

		VoxelMesh.manager = this;
		VoxelMesh.voxelScale = voxelScale;
		VoxelMesh.layer = (int)Mathf.Log(voxelLayer.value, 2);
		VoxelMesh.blockMaterial = voxelMaterial;
		
		for (int aLayer = 0; aLayer < layerEnd; ++aLayer) {
			for (int aRow = 0; aRow < rowEnd; ++aRow) {
				for (int aCol = 0; aCol < colEnd; ++aCol) {
					Vector3 meshPosition = new Vector3(aCol, aLayer, aRow)
						* (float)chunkSize / voxelScale;

					VoxelMesh chunkMesh = new VoxelMesh(meshPosition);
					m_visualCache[aCol, aLayer, aRow] = chunkMesh;
					chunkMesh.RegenerateVisualMesh(voxelScale);
					
					if (Scheduler.ShouldYield()) {
						yield return null;
					}
				}
			}
		}
	}
	
	void CreateCollisionCache() {
		GameObject parent = new GameObject("Collider parent");

		for (int i = 0; i < kColliderCacheDim * kColliderCacheDim * kColliderCacheDim; ++i) {
			GameObject go = new GameObject("Collider cache");
			go.transform.parent = parent.transform;
			go.AddComponent<Rigidbody>().isKinematic = true;
			BoxCollider co = go.AddComponent<BoxCollider>();
			m_colliderCache[i] = co;
			co.size = Vector3.one * voxelScale;
			co.enabled = false;
			co.center = co.size * 0.5f;
		}

		/*
		GameObject parent = new GameObject("Collider parent");
		
		for (int x = 0; x < 6; ++x) {
			for (int y = 0; y < kColliderCacheDim; ++y) {
				for (int z = 0; z < kColliderCacheDim; ++z) {
					GameObject go = new GameObject("Collider cache");
					go.transform.parent = parent.transform;
					go.AddComponent<Rigidbody>().isKinematic = true;
					BoxCollider co = go.AddComponent<BoxCollider>();
					m_colliderCache[x, y, z] = co;
					co.size = Vector3.one * voxelScale;
					co.enabled = false;
					co.center = co.size * 0.5f;
				}
			}
		}
		*/
		
		UpdateCollisionBlocks();
	}
	
	public bool pauseUpdateCollision = false;

	int3 m_oldVoxelPos = new int3(-1, -1, -1);
	void UpdateCollisionBlocks() {
		//int3 blobSize = m_blob.size;
		Vector3 pos = m_cameraTrans.position;
		int3 voxelPos = new int3((int)pos.x, (int)pos.y, (int)pos.z);

		if (voxelPos.x == m_oldVoxelPos.x
		    && voxelPos.y == m_oldVoxelPos.y
		    && voxelPos.z == m_oldVoxelPos.z)
		{
			return;
		}
		m_oldVoxelPos = voxelPos;

		int ci = 0; // Collider index!
		for (int x = voxelPos.x - kColliderDistance; x <= voxelPos.x + kColliderDistance; ++x) {
			for (int y = voxelPos.y - kColliderDistance; y <= voxelPos.y + kColliderDistance; ++y) {
				for (int z = voxelPos.z - kColliderDistance; z <= voxelPos.z + kColliderDistance; ++z) {
					if (m_blob.VoxelAt(x, y, z) != 0) {
						m_colliderCache[ci].transform.position = new Vector3(x, y, z);
						m_colliderCache[ci].enabled = true;
						++ci;
					}
				}
			}
		}
		while (ci < m_colliderCache.Length) {
			m_colliderCache[ci].enabled = false;
			++ci;
		}

		/*
		for (int dim = 0; dim < 3; ++dim) {
			int2 planeAxes = int3.GetOtherAxes(dim);
			for (int i = 0; i < kColliderCacheDim; ++i) {
				int3 testVox = new int3();
				testVox[planeAxes.x] = voxelPos[planeAxes.x] + i - kColliderDistance;
				for (int j = 0; j < kColliderCacheDim; ++j) {
					testVox[planeAxes.y] = voxelPos[planeAxes.y] + j - kColliderDistance;
					if (testVox[planeAxes.x] < 0 
					    || testVox[planeAxes.y] < 0 
					    || testVox[planeAxes.x] >= blobSize[planeAxes.x]
					    || testVox[planeAxes.y] >= blobSize[planeAxes.y]) 
					{
						m_colliderCache[dim * 2, i, j].enabled = false;
						m_colliderCache[dim * 2 + 1, i, j].enabled = false;
						continue;
					}
					
					BoxCollider co = m_colliderCache[dim * 2, i, j];
					co.enabled = false;
					
					for (int k = voxelPos[dim] - 1; k >= 0; --k) {
						testVox[dim] = k;
						if (m_blob.VoxelAt(testVox.x, testVox.y, testVox.z) != 0) {
							co.transform.position = testVox.ToVector3();
							co.enabled = true;
							break;
						}
					}
					
					co = m_colliderCache[dim * 2 + 1, i, j];
					co.enabled = false;
					for (int k = voxelPos[dim] + 1; k < m_blob.size[dim]; ++k) {
						testVox[dim] = k;
						if (m_blob.VoxelAt(testVox.x, testVox.y, testVox.z) != 0) {
							co.transform.position = testVox.ToVector3();
							co.enabled = true;
							break;
						}
					}
				}
			}
		}
		*/

		/*
		int3 blobSize = m_blob.size;
		Vector3 pos = m_cameraTrans.position;
		int3 voxelPos = new int3((int)pos.x, (int)pos.y, (int)pos.z);
		if (voxelPos.x == m_oldVoxelPos.x
		    && voxelPos.y == m_oldVoxelPos.y
		    && voxelPos.z == m_oldVoxelPos.z)
		{
			return;
		}
		m_oldVoxelPos = voxelPos;

		for (int dim = 0; dim < 3; ++dim) {
			int2 planeAxes = int3.GetOtherAxes(dim);
			for (int i = 0; i < kColliderCacheDim; ++i) {
				int3 testVox = new int3();
				testVox[planeAxes.x] = voxelPos[planeAxes.x] + i - kColliderDistance;
				for (int j = 0; j < kColliderCacheDim; ++j) {
					testVox[planeAxes.y] = voxelPos[planeAxes.y] + j - kColliderDistance;
					if (testVox[planeAxes.x] < 0 
					    || testVox[planeAxes.y] < 0 
					    || testVox[planeAxes.x] >= blobSize[planeAxes.x]
					    || testVox[planeAxes.y] >= blobSize[planeAxes.y]) 
					{
						m_colliderCache[dim * 2, i, j].enabled = false;
						m_colliderCache[dim * 2 + 1, i, j].enabled = false;
						continue;
					}
					
					BoxCollider co = m_colliderCache[dim * 2, i, j];
					co.enabled = false;
					
					for (int k = voxelPos[dim] - 1; k >= 0; --k) {
						testVox[dim] = k;
						if (m_blob.VoxelAt(testVox.x, testVox.y, testVox.z) != 0) {
							co.transform.position = testVox.ToVector3();
							co.enabled = true;
							break;
						}
					}
					
					co = m_colliderCache[dim * 2 + 1, i, j];
					co.enabled = false;
					for (int k = voxelPos[dim] + 1; k < m_blob.size[dim]; ++k) {
						testVox[dim] = k;
						if (m_blob.VoxelAt(testVox.x, testVox.y, testVox.z) != 0) {
							co.transform.position = testVox.ToVector3();
							co.enabled = true;
							break;
						}
					}
				}
			}
		}
		*/
	}
	
	void UpdateVisualChunk(int x, int y, int z) {
		if (x < 0 || y < 0 || z < 0 ||
			x >= m_visualCacheSize.x || y >= m_visualCacheSize.y || z >= m_visualCacheSize.z)
			return;
		
		UpdateVisualChunk(m_visualCache[x, y, z]);
	}
	
	void UpdateVisualChunk(VoxelMesh vm) {
		if (vm != null && vm.needsRegen) {
			vm.RegenerateVisualMesh(voxelScale);
			--m_needsRegenCount;
		}
	}
	
	void UpdateNearbyVisualChunks(Vector3 currPos, Vector3 lastPos) {
		int currX = (int)m_chunkPositionCurrent.x;
		int currY = (int)m_chunkPositionCurrent.y;
		int currZ = (int)m_chunkPositionCurrent.z;
		
		UpdateVisualChunk(currX, currY, currZ);
		
		if (lastPos.x != currPos.x)
			UpdateVisualChunk(currX + (lastPos.x < currPos.x ? 1 : -1), currY, currZ);
		if (lastPos.y != currPos.y)
			UpdateVisualChunk(currX, currY + (lastPos.y < currPos.y ? 1 : -1), currZ);
		if (lastPos.z != currPos.z)
			UpdateVisualChunk(currX, currY, currZ + (lastPos.z < currPos.z ? 1 : -1));
	}

	public bool isUpdating {
		get {
			return m_needsRegenCount > 0;
		}
		set {

		}
	}
	
	public IEnumerator UpdateVisualChunks() {
		Vector3 lastPos = m_cameraTrans.position;
		Vector3 currPos = m_cameraTrans.position;
		while (true) {
			foreach(VoxelMesh vm in m_visualCache) {
				while (m_needsRegenCount == 0) {
					lastPos = currPos;
					yield return null;
					if (m_needsRegenCount > 0) {
						currPos = m_cameraTrans.position;
						UpdateNearbyVisualChunks(currPos, lastPos);
					}
				}
				
				UpdateVisualChunk(vm);

				if (Scheduler.ShouldYield()) {
					lastPos = currPos;
					yield return null;
					if (m_needsRegenCount > 0) {
						currPos = m_cameraTrans.position;
						UpdateNearbyVisualChunks(currPos, lastPos);
					}
				}
			}
		}
	}
	
	void MarkChunkForRegen(VoxelMesh vm) {
		if (vm != null && !vm.needsRegen) {
			vm.needsRegen = true;
			++m_needsRegenCount;
		}
	}
	
	public void MarkChunkForRegen(int x, int y, int z) {
		if (x >= 0 && y >= 0 && z >= 0 &&
			x < m_visualCacheSize.x && y < m_visualCacheSize.y && z < m_visualCacheSize.z)
			MarkChunkForRegen(m_visualCache[x, y, z]);
	}
	
	public void MarkChunkForRegen(int3 chunk) {
		MarkChunkForRegen(chunk.x, chunk.y, chunk.z);
	}
	
	public bool ValidChunk(int3 chunk) {
		for (int dim = 0; dim < 3; ++dim) {
			if (chunk[dim] < 0 || chunk[dim] >= m_visualCacheSize[dim])
				return false;
		}
		
		return true;
	}
	
	public bool ValidVisualCache(int3 chunk) {
		if (!ValidChunk(chunk))
			return false;
		return m_visualCache[chunk.x, chunk.y, chunk.z] != null;
	}
	
	/// <summary>
	/// Enqueues chunks to create if needed based on 
	/// our new position.
	/// </summary>
	void UpdateMovement() {
		m_chunkPositionCurrent = WorldToChunk(m_cameraTrans.position);
	}
	
	/// <summary>
	/// Converts a world position to a chunk position.
	/// </summary>
	/// <returns>
	/// The to position in chunk coordinates.
	/// </returns>
	/// <param name='aPosition'>
	/// A world position.
	/// </param>
	int3 WorldToChunk(Vector3 aPosition) {
		Vector3 result = aPosition / chunkSize;
		int3 ret = new int3(Mathf.FloorToInt(result.x), Mathf.FloorToInt(result.y), Mathf.FloorToInt(result.z));
		
		return ret;
	}
	
	/// <summary>
	/// Convert a world position to chunk coordinates.
	/// </summary>
	/// <param name='zIn'>
	/// Z in.
	/// </param>
	/// <param name='yIn'>
	/// Y in.
	/// </param>
	/// <param name='xIn'>
	/// X in.
	/// </param>
	/// <param name='zOut'>
	/// Z out.
	/// </param>
	/// <param name='yOut'>
	/// Y out.
	/// </param>
	/// <param name='xOut'>
	/// X out.
	/// </param>
	void WorldToChunk(int xIn, int yIn, int zIn, out int xOut, out int yOut, out int zOut) {
		zOut = zIn / chunkSize;
		yOut = yIn / chunkSize;
		xOut = xIn / chunkSize;
	}
	#endregion
	
	#region Voxel Support
	/// <summary>
	/// Texture sprites used for materials (top, side, bottom; check this).
	/// </summary>
	int[] m_materialTextures = {
		0, 
		256 - 16, 
		256 - 32,
	};

	int[] m_materialTexturesSpecial = {
		256 - 48,
		256 - 64
	};
	
	public int GetTextureForVoxelMaterial(int voxelMaterial) {
		if (voxelMaterial >= kVoxelFirstSpecial) return m_materialTexturesSpecial[voxelMaterial - kVoxelFirstSpecial];
		else return m_materialTextures[voxelMaterial];
	}
	
	/// <summary>
	/// Converts the voxel material to print material. Returns -1 if not a printable material
	/// (empty, water, etc)
	/// </summary>
	/// <returns>
	/// The voxel mat to print mat.
	/// </returns>
	/// <param name='voxel'>
	/// Voxel.
	/// </param>
	public static int ConvertVoxelMatToPrintMat(int voxel) {
		if (voxel >= kVoxelFirstMat && voxel < kVoxelFirstMat - kVoxelMatCount) {
			return voxel - kVoxelFirstMat;
		}
		else {
			return -1;
		}
	}
	
	
	/// <summary>
	/// Returns the voxel using Unity's (z, y, x) coordinates.
	/// </summary>
	/// <returns>
	/// The voxel byte, indicating material number.
	/// </returns>
	/// <param name='unityZ'>
	/// Z.
	/// </param>
	/// <param name='unityY'>
	/// Y.
	/// </param>
	/// <param name='unityX'>
	/// X.
	/// </param>
	public byte VoxelAt(int unityX, int unityY, int unityZ) {
		if (!m_blob.IsValidPoint(unityX, unityY, unityZ))
			return 0;
		
		return m_blob[unityX, unityY, unityZ];
	}
	
	public byte VoxelAt(int3 index) {
		if (!m_blob.IsValidPoint(index))
			return 0;
		
		return m_blob[index];
	}
	
	public bool IsValidChunk(int3 index) {
		for (int dim = 0; dim < 3; ++dim) {
			if (index[dim] < 0 || index[dim] >= m_visualCache.GetLength(dim))
				return false;
		}
		
		return m_visualCache[index.x, index.y, index.z] != null;
	}
	
	/// <summary>
	/// Sets the voxel at (z, y, x) to the specified
	/// material value.
	/// </summary>
	/// <param name='unityZ'>
	/// Z.
	/// </param>
	/// <param name='unityY'>
	/// Y.
	/// </param>
	/// <param name='unityX'>
	/// X.
	/// </param>
	/// <param name='toValue'>
	/// To value.
	/// </param>
	/// <returns>
	/// True if the voxel was placed.
	/// </returns>
	public bool SetVoxel(int unityX, int unityY, int unityZ, byte toValue) {
		if (!m_blob.IsValidPoint(unityX, unityY, unityZ)) {
			return false;
		}
		int3 unityPoint = new int3(unityX, unityY, unityZ);

		// Regenerate colliders.
		m_oldVoxelPos = new int3(-1, -1, -1);
		m_blob[unityX, unityY, unityZ] = toValue;


		// Find and regenerate the changed chunk
		int3 chunkLoc = WorldToChunk(new Vector3(unityX, unityY, unityZ));

		//Debug.Log("Regen chunk call");
		if (IsValidChunk(chunkLoc)) {
			m_visualCache[chunkLoc.x, chunkLoc.y, chunkLoc.z].RegenerateVisualMesh(voxelScale);
		}
		
		int3 neighbor;
		// Regenerate adjacent chunks if needed.
		for (int dim = 0; dim < 3; ++dim) {
			int mod = unityPoint[dim] % chunkSize;
			if (mod == 0) {
				neighbor = chunkLoc;
				neighbor[dim]--;
				if (IsValidChunk(neighbor))
					m_visualCache[neighbor.x, neighbor.y, neighbor.z].RegenerateVisualMesh(voxelScale);
			}
			else if (mod == chunkSize - 1) {
				neighbor = chunkLoc;
				neighbor[dim]++;
				if (IsValidChunk(neighbor))
					m_visualCache[neighbor.x, neighbor.y, neighbor.z].RegenerateVisualMesh(voxelScale);
			}
		}
		
		Dispatcher<int, int>.Broadcast(kOnSetVoxel, unityX, unityZ);
		return true;
	}
	
	/// <summary>
	/// Adds the passed voxel blob, vb, to the existing voxel blob.
	/// </summary>
	/// <param name='vb'>
	/// Vb.
	/// </param>
	public IEnumerator AddVoxelBlob(VoxelBlob vb, Vector3 offset) {
		int minX = (int)offset.x < 0 ? Mathf.Abs((int)offset.x) : 0;
		int minY = (int)offset.y < 0 ? Mathf.Abs((int)offset.y) : 0;
		int minZ = (int)offset.z < 0 ? Mathf.Abs((int)offset.z) : 0;
		int maxX = m_blob.width < vb.width + (int)offset.x ? m_blob.width - (int)offset.x : vb.width;
		int maxY = m_blob.height < vb.height + (int)offset.y ? m_blob.height - (int)offset.y : vb.height;
		int maxZ = m_blob.depth < vb.depth + (int)offset.z ? m_blob.depth - (int)offset.z : vb.depth;
		//Text.Warning("Adding a voxel blob at offset : " + offset);
		//Text.Log("Starting to add at " + Time.realtimeSinceStartup);
		HashSet<int3> chunksModified = new HashSet<int3>();
		
		BlobDelta delta = new BlobDelta(chunkSize);
		PushNewDelta(delta);
		
		for(int x = minX; x < maxX; x++) {
			for(int y = minY; y < maxY; y++) {
				for(int z = minZ; z < maxZ; z++) {
					if (vb[x,y,z] != 0) {
						int targetX = x + (int)offset.x;
						int targetY = y + (int)offset.y;
						int targetZ = z + (int)offset.z;
						byte newMat = vb[x, y, z];
						newMat = (newMat == kVoxelSubtract ? (byte)0 : newMat);
						byte oldMat = m_blob[targetX, targetY, targetZ];
						delta[targetX, targetY, targetZ] = (oldMat == 0 ? kVoxelSubtract : oldMat);
						m_blob[targetX, targetY, targetZ] = newMat;
						
						Vector3 targetPoint = new Vector3(targetX, targetY, targetZ);
						int3 changedChunk = ChunkForPoint(targetX, targetY, targetZ);
						chunksModified.Add(changedChunk);
						
						//flag all the surrounding chunks
						for (int dim = 0; dim < 3; ++dim) {
							for (int dir = -1; dir < 2; dir += 2) {
								Vector3 neighbor = targetPoint;
								neighbor[dim] += dir;
								int3 neighborChunk = ChunkForPoint(Mathf.FloorToInt(neighbor.x),
									Mathf.FloorToInt(neighbor.y), Mathf.FloorToInt(neighbor.z));
								if (neighborChunk != changedChunk)
									chunksModified.Add(neighborChunk);
							}
						}
					}
					if (Scheduler.ShouldYield()) {
						//Text.Log("Waited at " +Time.realtimeSinceStartup);
						yield return null;
					}
				}
			}
		}
		//float endTime = Time.realtimeSinceStartup;
		//Text.Log("Starting to Queue at " + Time.realtimeSinceStartup);
		foreach(int3 v in chunksModified) {
			MarkChunkForRegen(v);
		}
		yield return null;
	}
	
	public IEnumerator ReadBytes(byte[] data, int width, int height, int depth, int3 start, int3 end) {
		int position = 0;
		
		for (int y = start.y; y <= end.y; ++y) {
			for (int z = start.z; z <= end.z; ++z) {
				for (int x = start.x; x <= end.x; ++x) {
					if (Scheduler.ShouldYield()) yield return null;
					byte mat = data[position++];
					if (mat != m_blob[x, y, z]) {
						m_blob[x, y, z] = mat;
						int3 chunk = ChunkForPoint(x, y, z);
						MarkChunkForRegen(chunk.x, chunk.y, chunk.z);
					}
				}
				Dispatcher<float>.Broadcast(kOnLoadUpdate, (float)position / (float)data.Length);
			}
		}
		Dispatcher<float>.Broadcast(kOnLoadUpdate, 1.0f);
	}
	
	public IEnumerator ClearMinimalBlob() {
		if (m_blob.minVoxelBounds.x < 0 || m_blob.maxVoxelBounds.x < 0) yield break;
		for (int y = m_blob.minVoxelBounds.y; y <= m_blob.maxVoxelBounds.y; ++y) {
			for (int z = m_blob.minVoxelBounds.z; z <= m_blob.maxVoxelBounds.z; ++z) {
				for (int x = m_blob.minVoxelBounds.x; x <= m_blob.maxVoxelBounds.x; ++x) {
					if (Scheduler.ShouldYield()) yield return null;
					if (0 != m_blob[x, y, z]) {
						m_blob[x, y, z] = 0;
						int3 chunk = ChunkForPoint(x, y, z);
						MarkChunkForRegen(chunk.x, chunk.y, chunk.z);
					}
				}
			}
		}
	}

	public IEnumerator EraseBlob() {
		m_blob = new VoxelBlob(VoxelBlob.kTestSize, VoxelBlob.kTestSize, VoxelBlob.kTestSize, true);
		m_deltaHistory.Clear();
		yield return null;
	}
	
	/// <summary>
	/// Adds the passed voxel blob, vb, to the existing voxel blob.
	/// </summary>
	/// <param name='vb'>
	/// Vb.
	/// </param>
	public void AddVoxelBlob(VoxelBlob vb) {
		Scheduler.StartCoroutine(AddVoxelBlob(vb, Vector3.zero));
	}
	
	/// <summary>
	/// Returns the chunk for the provided point.
	/// </summary>
	/// <returns>
	/// The for point.
	/// </returns>
	/// <param name='width'>
	/// Width.
	/// </param>
	/// <param name='layer'>
	/// Layer.
	/// </param>
	/// <param name='depth'>
	/// Depth.
	/// </param>
	public int3 ChunkForPoint(int width, int layer, int depth) {
		return new int3(width / chunkSize, layer / chunkSize, depth / chunkSize);
	}
	
	public int3 ChunkForPoint(Vector3 point) {
		return ChunkForPoint((int)point.x, (int)point.y, (int) point.z);
	}
	
	public int3 ChunkForPoint(int3 point) {
		return new int3(point.x / chunkSize, point.y / chunkSize, point.z / chunkSize);
	}
	
	public void MarkChunksForRegenForPoint(int3 point) {
		MarkChunkForRegen(ChunkForPoint(point));
		for (int dim = 0; dim < 3; ++dim) {
			for (int dir = -1; dir < 2; dir += 2) {
				int3 neighbor = point;
				neighbor[dim] += dir;
				if (m_blob.IsValidPoint(neighbor)) {
					MarkChunkForRegen(ChunkForPoint(neighbor));
				}
			}
		}
	}
	
	public void MarkChunksForRegenForPoint(int x, int y, int z) {
		MarkChunksForRegenForPoint(new int3(x, y, z));
	}
	
	public void GetChunkVoxelBounds(Vector3 chunk, out Vector3 lowerBound, out Vector3 upperBound) {
		lowerBound = chunk * chunkSize + blobLowerBound;
		upperBound = (chunk + Vector3.one) * chunkSize + blobLowerBound;
		upperBound.x = Mathf.Min(upperBound.x, m_blob.width);
		upperBound.y = Mathf.Min(upperBound.y, m_blob.height);
		upperBound.z = Mathf.Min(upperBound.z, m_blob.depth);
	}
	
	public void GetChunkVoxelBounds(int3 chunk, out int3 lowerBound, out int3 upperBound) {
		lowerBound = chunk * chunkSize + new int3(blobLowerBound);
		upperBound = (chunk + int3.one) * chunkSize + new int3(blobLowerBound);
		upperBound.x = Mathf.Min(upperBound.x, m_blob.width);
		upperBound.y = Mathf.Min(upperBound.y, m_blob.height);
		upperBound.z = Mathf.Min(upperBound.z, m_blob.depth);
	}
	
	
	#endregion
	
	#region Undo/redo support
	
	/// <summary>
	/// Start a new blob delta (large scale undo action)
	/// </summary>
	/// <returns>
	/// The BLOB delta.
	/// </returns>
	public BlobDelta StartBlobDelta() {
		BlobDelta delta = new BlobDelta(chunkSize);
		PushNewDelta(delta);
		return delta;
	}
	
	public void PushNewDelta(IDelta delta) {
		m_deltaFuture.Clear();
		m_deltaHistory.Push(delta);
	}
	
	IDelta m_currentDelta = null;
	bool m_currentIsUndo;
	
	void OnDeltaDone() {
		m_currentDelta = null;
	}

	public void StopConversionsAndClearFuture() {
		if (m_currentDelta != null) m_currentDelta.Stop();

		while (m_deltaFuture.Count > 0) {
			m_currentDelta = m_deltaFuture.Pop();
			m_currentDelta.Stop();
		}

		while (m_deltaHistory.Count > 0) {
			m_currentDelta = m_deltaHistory.Pop();
			m_currentDelta.Stop();
		}
	}
	
	/// <summary>
	/// Undoes the most recent action.
	/// </summary>
	public void UndoAction() { 
		while (m_deltaHistory.Count > 0 && !m_deltaHistory.Peek().Valid) m_deltaHistory.Pop();
		
		if (m_deltaHistory.Count == 0 || 
			(m_currentDelta != null && m_currentIsUndo) || 
			!m_deltaHistory.Peek().CanUndo()) 
		{
			Dispatcher<Sfx>.Broadcast(AudioManager.kPlaySfx, Sfx.Invalid);
			return;
		}
		
		Dispatcher<Sfx>.Broadcast(AudioManager.kPlaySfx, Sfx.Select);
		m_currentIsUndo = true;
		m_currentDelta = m_deltaHistory.Pop();
		m_deltaFuture.Push(m_currentDelta);
		m_currentDelta.UndoAction(this, OnDeltaDone);
	}
	
	
	/// <summary>
	/// Redoes the most recent undo.
	/// </summary>
	public void RedoAction() { 
		while (m_deltaFuture.Count > 0 && !m_deltaFuture.Peek().Valid) m_deltaFuture.Pop();

		if (m_deltaFuture.Count == 0 || 
			(m_currentDelta != null && !m_currentIsUndo) || 
			!m_deltaFuture.Peek().CanRedo()) {
			Dispatcher<Sfx>.Broadcast(AudioManager.kPlaySfx, Sfx.Invalid);
			return;
		}
		
		Dispatcher<Sfx>.Broadcast(AudioManager.kPlaySfx, Sfx.Select);
		m_currentIsUndo = false;
		m_currentDelta = m_deltaFuture.Pop();
		m_deltaHistory.Push(m_currentDelta);
		m_currentDelta.RedoAction(this, OnDeltaDone);
	}
	#endregion
	
	//public List<Vector3> usedChunks = new List<Vector3>();
	void OnDrawGizmos() {
		if (!Application.isPlaying) return;
		float tempX = m_blob.width;
		float tempZ = m_blob.height;
		Gizmos.DrawLine(Vector3.zero, new Vector3(tempX,0,0));
		Gizmos.DrawLine(Vector3.zero, new Vector3(0,0,tempZ));
		Gizmos.DrawLine(new Vector3(tempX,0,0), new Vector3(tempX,0,tempZ));
		Gizmos.DrawLine(new Vector3(0,0,tempZ), new Vector3(tempX,0,tempZ));
	}
}
