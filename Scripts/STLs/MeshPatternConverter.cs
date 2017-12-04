using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MeshPatternConverter : MonoBehaviour, IPatternConverter {
	public class DirData {
		public int i;
		public int j;
	}
	
	public class Data {
		public List<Face> faces;
		public byte[,,] voxelData;
		public byte material;
		public DirData[] dirData;
		public Material meshMat;
		
		public Data() {
			dirData = new DirData[] {new DirData(), new DirData(), new DirData() };
		}
	}
	
	public bool shouldAbort = false;
	Data m_data;
	
	Octree m_meshOctree;
	
	MeshManager m_manager;
	Vector3 m_lowerBound;
	Vector3 m_upperBound;
	int3 m_voxelStart;
	int3 m_voxelEnd;
	MeshPatternDelta m_delta;
	MeshPattern m_pattern;
	
	public void Init(MeshPattern pattern, MeshManager manager, Vector3 position, Quaternion rotation, Vector3 scale, 
		byte materialChoice, Material meshMat) {
		m_data = new Data();
		m_data.meshMat = meshMat;
		m_pattern = pattern;

		Dispatcher.AddListener(MenuController.kEventNewBlob, CancelConversion);

		m_data.material = materialChoice;
		if (m_data.material == MeshManager.kVoxelSubtract) m_data.material = MeshManager.kVoxelEmpty;
		
		m_data.faces = new List<Face>();
		m_manager = manager;
		
		bool flip = false;
		for (int i = 0; i < 3; ++i) {
			if (scale[i] < 0) flip = !flip;
		}
		
		foreach(Face f in pattern.Faces) {
			Vector3[] tempVec = new Vector3[3];
			for(int v = 0; v < 3; v++) {
				tempVec[v] = f.vertices[v];
				tempVec[v].x *= scale.x;
				tempVec[v].y *= scale.y;
				tempVec[v].z *= scale.z;
				tempVec[v] = rotation * tempVec[v];
				tempVec[v] += position;
			}
			Face newFace;
			if (flip)
				newFace = new Face(tempVec[2], tempVec[1], tempVec[0]);
			else
				newFace = new Face(tempVec[0], tempVec[1], tempVec[2]);
			newFace.Init();
			m_data.faces.Add(newFace);
		}
		
		CalcBounds();
		
		for (int dir = 0; dir < 3; ++dir) {
			int axis0 = ((dir + 1) % 3);
			int axis1 = ((dir + 2) % 3);
			m_data.dirData[dir].i = m_voxelStart[axis0];
			m_data.dirData[dir].j = m_voxelStart[axis1];
		}
		
		int3 voxelBlobDimensions = m_voxelEnd - m_voxelStart;
		if (voxelBlobDimensions.x < 0 || voxelBlobDimensions.y < 0 
			|| voxelBlobDimensions.z < 0) Text.Error("Dimension was calculated as NEGATIVE!");			

		m_data.voxelData = new byte[voxelBlobDimensions.x, voxelBlobDimensions.y, voxelBlobDimensions.z];
	
	}
	
	public void Init(MeshManager manager, Data data, MeshPatternDelta delta) {
		m_data = data;
		m_delta = delta;
		m_manager = manager;
		
		CalcBounds();
	}
	
	void CalcBounds() {
		m_lowerBound = Face.FindLowerBound(m_data.faces);
		m_upperBound = Face.FindUpperBound(m_data.faces);
		
		m_voxelStart = new int3(
			Mathf.FloorToInt(m_lowerBound.x), 
			Mathf.FloorToInt(m_lowerBound.y), 
			Mathf.FloorToInt(m_lowerBound.z));
		
		m_voxelEnd = new int3(
			Mathf.FloorToInt(m_upperBound.x + 1f),
			Mathf.FloorToInt(m_upperBound.y + 1f),
			Mathf.FloorToInt(m_upperBound.z + 1f));
	}
	
	public IEnumerator Convert() {
		// check if this is totally out of bounds
		if (m_voxelStart.x >= m_manager.m_blob.width || m_voxelStart.y >= m_manager.m_blob.height ||
			m_voxelStart.z >= m_manager.m_blob.depth || m_voxelEnd.x < 0 || m_voxelEnd.y < 0 ||
			m_voxelEnd.z < 0) 
		{
			if (m_delta != null)
				m_delta.MarkConversionDone();
			gameObject.SetActive(false);
			Destroy(gameObject);
			yield break;
		}
		
		/// Create an Octree for the altered faces
		m_meshOctree = new Octree(0, m_data.faces, m_lowerBound, m_upperBound);
		
		if (m_delta == null) {
			m_delta = new MeshPatternDelta(this, m_data, m_manager.chunkSize);
			m_manager.PushNewDelta(m_delta);
		}
		
		if (Scheduler.ShouldYield()) yield return null;
		if (shouldAbort) yield break;
		
		IWaitCondition[] waitConds = new IWaitCondition[3];
		for (int dir = 0; dir < 3; ++dir) {
			waitConds[dir] = new WaitCoroutine(Scheduler.StartCoroutine(CreateVoxelsFromDirection(dir)));
		}
		
		yield return new WaitAll(waitConds);
		
		if (!shouldAbort)
			m_delta.MarkConversionDone();
		
		gameObject.SetActive(false);
		Destroy(gameObject);
		Dispatcher.RemoveListener(MenuController.kEventNewBlob, CancelConversion);
	}

	void CancelConversion() {
		shouldAbort = true;
		Dispatcher.RemoveListener(MenuController.kEventNewBlob, CancelConversion);
	}

	~MeshPatternConverter() {
		CancelConversion();
	}
	
	SortedDictionary<float, bool>  BuildIntersections(int rayDirection, int planeAxis0, int planeAxis1, Vector3 lineLoc) {
		SortedDictionary<float, bool> intersections = new SortedDictionary<float, bool>();
		List<Face> suspectFaces = m_meshOctree.FindIntersectingFacesOnAASegment(rayDirection, lineLoc, 
			m_lowerBound[rayDirection] - 0.1f, m_upperBound[rayDirection] + 0.1f);
		float minVal = float.MaxValue;

		foreach (Face currFace in suspectFaces) {
			if (IsAALineInFace(currFace, rayDirection, lineLoc)) {
				float intersect = AALinePlaneIntersection(rayDirection, lineLoc, currFace);
				if (intersect >= m_lowerBound[rayDirection] - 0.1f && intersect < m_upperBound[rayDirection] + 0.1f) {
					bool val = (currFace.normal[rayDirection] > 0);

					if (intersections.ContainsKey(intersect)) {
						if (intersections[intersect] != val) {
							intersections.Remove(intersect);
							if (intersections.Count == 0) {
								minVal = float.MaxValue;
							} 
							else {
								minVal = float.MaxValue;
								foreach(KeyValuePair<float, bool> kvp in intersections) {
									minVal = Mathf.Min(minVal, kvp.Key);
								}
							}
						}
					}
					else {
						intersections.Add(intersect, val);
						minVal = Mathf.Min(minVal, intersect);
					}
				}
			}
		}

		if(minVal != float.MaxValue && intersections.ContainsKey(minVal) && intersections[minVal]) {
			SortedDictionary<float, bool> altIntersections = new SortedDictionary<float, bool>();
			foreach(KeyValuePair<float, bool> kvp in intersections) {
				altIntersections.Add(kvp.Key,!kvp.Value);
			}
			intersections = altIntersections;
		}
		
		return intersections;
	}
	
	/// <summary>
	/// Creates the voxels from ray casting through the face from rayDirection
	/// </summary>
	private IEnumerator CreateVoxelsFromDirection(int rayDirection) {
		int3 blobBounds = new int3(m_manager.m_blob.width, m_manager.m_blob.height, m_manager.m_blob.depth);
		DirData dirData = m_data.dirData[rayDirection];
		int3 dataIndex = new int3();
		int planeAxis0 = ((rayDirection + 1) % 3);
		int planeAxis1 = ((rayDirection + 2) % 3);
		for (; dirData.i < m_voxelEnd[planeAxis0]; ++dirData.i) {
			int i = dirData.i;
			if (i < 0 || i >= blobBounds[planeAxis0]) continue;
			for (; dirData.j < m_voxelEnd[planeAxis1]; ++dirData.j) {
				int j = dirData.j;
				if (j < 0 || j >= blobBounds[planeAxis1]) continue;
				if (Scheduler.ShouldYield()) yield return null;
				if (shouldAbort) yield break;
				
				Vector3 point = Vector3.zero;
				point[planeAxis0] = i + 0.5f;
				point[planeAxis1] = j + 0.5f;
				
				SortedDictionary<float, bool> intersections = BuildIntersections(rayDirection, planeAxis0, planeAxis1, point);
				
				int3 index = new int3(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), Mathf.FloorToInt(point.z));
				Stack<float> startPoints = new Stack<float>();
				float last = -1f;
				foreach (KeyValuePair<float, bool> kvp in intersections) {
					if (Mathf.Approximately(kvp.Key, last)) continue;
					
					if (kvp.Value) {
						if (startPoints.Count > 0) {
							for (int k = Mathf.RoundToInt(startPoints.Peek()); k < Mathf.RoundToInt(kvp.Key); ++k) {
								index[rayDirection] = k;
								for (int dim = 0; dim < 3; ++dim) dataIndex[dim] = index[dim] - m_voxelStart[dim];
								m_data.voxelData[dataIndex[0], dataIndex[1], dataIndex[2]]++;
							}
							if (startPoints.Count > 1) {
								startPoints.Pop();
							}
						}
					}
					else {
						startPoints.Push(kvp.Key);
					}
					
					last = kvp.Key;
				}
				
				for (int k = m_voxelStart[rayDirection]; k < m_voxelEnd[rayDirection]; ++k) {
					index[rayDirection] = k;
					for (int dim = 0; dim < 3; ++dim) dataIndex[dim] = index[dim] - m_voxelStart[dim];
					m_data.voxelData[dataIndex.x, dataIndex.y, dataIndex.z] += 10;
					if (m_data.voxelData[dataIndex.x, dataIndex.y, dataIndex.z] >= 32) {
						if (m_manager.m_blob.IsValidPoint(index)) {
							byte oldMat = m_manager.m_blob[index];
							if (oldMat == MeshManager.kVoxelEmpty) oldMat = MeshManager.kVoxelSubtract;
							m_delta.blobDelta[index] = oldMat;
							m_manager.m_blob[index] = m_data.material;
							m_manager.MarkChunksForRegenForPoint(index);
						}
					}
				}
			}
			dirData.j = m_voxelStart[planeAxis1];
		}
		
		yield return null;
	}
	
	static bool IsParallelPlane(Face face, int axis) {
		return Mathf.Approximately(face.normal[axis], 0f);
	}
	
	static bool IsAALineInFace(Face face, int lineAxis, Vector3 lineLoc) {
		if (IsParallelPlane(face, lineAxis)) return false;

		int axis0 = (lineAxis + 1) % 3;
		int axis1 = (lineAxis + 2) % 3;
		
		if (lineLoc[axis0] < face.lowerBound[axis0] || lineLoc[axis0] > face.upperBound[axis0] ||
			lineLoc[axis1] < face.lowerBound[axis1] || lineLoc[axis1] > face.upperBound[axis1]) {
			return false;
		}
		
		Vector3[] vertices = face.vertices;
		
		float coeff = 1f/(-vertices[1][axis1] * vertices[2][axis0] + 
			vertices[0][axis1] * (vertices[2][axis0] - vertices[1][axis0]) + 
			vertices[0][axis0] * (vertices[1][axis1] - vertices[2][axis1]) +
			vertices[1][axis0] * vertices[2][axis1]);
		
		float s = coeff * (vertices[0][axis1] *vertices[2][axis0] - 
			vertices[0][axis0] * vertices[2][axis1] + 
			lineLoc[axis0] * (vertices[2][axis1] - vertices[0][axis1]) +
			lineLoc[axis1] * (vertices[0][axis0] - vertices[2][axis0]));
		
		float t = coeff * (vertices[0][axis0] * vertices[1][axis1] -
			vertices[0][axis1] * vertices[1][axis0] + 
			lineLoc[axis0] * (vertices[0][axis1] - vertices[1][axis1]) +
			lineLoc[axis1] * (vertices[1][axis0] - vertices[0][axis0]));
		
		return (s >= 0 && t >= 0 && 1 - s - t >= 0);
	}
	
	static float AALinePlaneIntersection(int lineAxis, Vector3 lineLoc, Face face) {
		lineLoc[lineAxis] = 0;
		
		return Vector3.Dot(face.normal, face.vertices[0] - lineLoc) / face.normal[lineAxis];
	}
}
