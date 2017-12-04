using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text; 

/// <summary>
/// Class that holds face data from things like stl imports or the pattern definition tool.
/// </summary>
public class MeshPattern : IPattern {
	public const string kFileExtension = ".rcesurface";
	public const string kOnExportUpdate = "OnExportUpdate";

	private List<Face> faces;
	public List<Face> Faces {
		get {
			return faces;
		}
	}
	
	private List<List<Face>> multicolorFaces;
	
	Vector3 m_size;
	public Vector3 Size { get { return m_size; } }
	public bool LockToGrid { get { return false; } }
	public string FileExtension { get { return kFileExtension; } }
	
	bool m_loaded = false;
	public bool loaded { get { return m_loaded; }}
	public bool IsLoaded() { return loaded; }
	public float loadProgress;

	#region Init
	public void InitFromStlResource(TextAsset asset) {
		Scheduler.StartCoroutine(LoadFromResource(asset));
	}
	
	IEnumerator LoadFromResource(TextAsset asset) {
		yield return Scheduler.StartCoroutine(ReadStlAscii(asset.text));
		Resources.UnloadAsset(asset);
	}
	
	/// <summary>
	/// Loads our data from the given cache dir
	/// </summary>
	public void InitFromCache(string dir, string fileName) {
		Scheduler.StartCoroutine(DoLoadFromCache(dir + fileName));
	}
	
	IEnumerator DoLoadFromCache(string filePath) {
		FileStream stream = new FileStream(filePath, FileMode.Open);
		faces = new List<Face>();

		while (stream.Position < stream.Length) {
			if (Scheduler.ShouldYield()) yield return null;
			List<Face> currFaces = (List<Face>)SerializationHelpers.DeserializeFromStream(stream);
			faces.AddRange(currFaces);
		}
		
		m_size = Face.FindUpperBound(faces) - Face.FindLowerBound(faces);
		m_loaded = true;
	}
	
	public void InitFromStlFile(string aPath) {
		Scheduler.StartCoroutine(ReadStl(aPath), this);
	}
	
	public void InitFromBlob(VoxelBlob blob) {
		Scheduler.StartCoroutine(GenFacesFromBlob(blob), this);
	}
	
	public void SaveStlFromBlob(VoxelBlob blob, string fileName) {
		Scheduler.StartCoroutine(GenFacesFromBlob(blob), this);
		Scheduler.StartCoroutine(WriteStlBinary(fileName), this);
	}
	
	public void SaveStlFromBlob(VoxelBlob blob, System.IO.Stream aStream, bool shouldClose) {
		Scheduler.StartCoroutine(GenFacesFromBlob(blob), this);
		Scheduler.StartCoroutine(WriteStlBinaryToStream(aStream, shouldClose), this);
	}

	public void CancelCoroutines() {
		// Scheduler.StopAllCoroutines?
	}
	
	IEnumerator GenFacesFromBlob(VoxelBlob blob) {
		float currentProgress = 0;
		float maxProgress = blob.width * blob.height * blob.depth;
		loadProgress = 0;
		
		faces = new List<Face>();
		for (int x = 0; x < blob.width; ++x) {
			for (int y = 0; y < blob.height; ++y) {
				for (int z = 0; z < blob.depth; ++z) {
					if (Scheduler.ShouldYield()) yield return null;
					if (blob[x, y, z] == MeshManager.kVoxelEmpty)
						continue;
					
					//figure out if each face is a surface face
					Vector3 block = new Vector3(x, y, z);
					for (int dim = 0; dim < 3; ++dim) {
						for (int dir = -1; dir < 2; dir += 2) {
							Vector3 neighbor = block;
							neighbor[dim] += dir;
							bool onSurface = true;
							if (blob.IsValidPoint((int)neighbor.x, (int) neighbor.y, (int)neighbor.z) && 
								blob[(int)neighbor.x, (int)neighbor.y, (int)neighbor.z] != MeshManager.kVoxelEmpty)
								onSurface = false;
							
							if (onSurface) {
								int axis1 = (dir < 0 ? (dim + 2) % 3 : (dim + 1) % 3);
								int axis2 = (dir < 0 ? (dim + 1) % 3 : (dim + 2) % 3);
								
								Vector3 p0;
								if (dir < 0) 
									p0 = block;
								else
									p0 = block + Vector3.one;
								
								Vector3 p1 = p0;
								p1[axis1] -= dir;
								Vector3 p2 = p0;
								p2[axis2] -= dir;
								Face face = new Face(p0, p1, p2);
								face.Init();
								faces.Add(face);
								Vector3 p3 = p1;
								p3[axis2] -= dir;
								face = new Face(p3, p2, p1);
								face.Init();
								faces.Add(face);
							}
						}
					}
				}
			}
			currentProgress += blob.height * blob.depth;
			loadProgress = currentProgress / maxProgress;
		}
		MoveFacesToOrigin();
		m_loaded = true;
	}
	
	IEnumerator GenColorFacesFromBlob(VoxelBlob blob) {
		float currentProgress = 0;
		float maxProgress = blob.width * blob.height * blob.depth;
		loadProgress = 0;
		
		int3 minVals = new int3(0,0,0);
		int3 maxVals = new int3(blob.width, blob.height, blob.depth);
		
		if (blob.minVoxelBounds.x != -1) {
			minVals = blob.minVoxelBounds;
			maxVals = blob.maxVoxelBounds;
		}
		
		multicolorFaces = new List<List<Face>>();
		
		for(int mat = 1; mat < 5; mat++) {
			faces = new List<Face>();
			for (int x = minVals.x; x < maxVals.x; ++x) {
				for (int y = minVals.y; y < maxVals.y; ++y) {
					for (int z = minVals.z; z < maxVals.z; ++z) {
						if (Scheduler.ShouldYield()) yield return null;
						if (blob[x, y, z] != mat)
							continue;
						
						//figure out if each face is a surface face
						Vector3 block = new Vector3(x, y, z);
						for (int dim = 0; dim < 3; ++dim) {
							for (int dir = -1; dir < 2; dir += 2) {
								Vector3 neighbor = block;
								neighbor[dim] += dir;
								bool onSurface = true;
								if (blob.IsValidPoint((int)neighbor.x, (int) neighbor.y, (int)neighbor.z) && 
									blob[(int)neighbor.x, (int)neighbor.y, (int)neighbor.z] == mat)
									onSurface = false;
								
								if (onSurface) {
									int axis1 = (dir < 0 ? (dim + 2) % 3 : (dim + 1) % 3);
									int axis2 = (dir < 0 ? (dim + 1) % 3 : (dim + 2) % 3);
									
									Vector3 p0;
									if (dir < 0) 
										p0 = block;
									else
										p0 = block + Vector3.one;
									
									Vector3 p1 = p0;
									p1[axis1] -= dir;
									Vector3 p2 = p0;
									p2[axis2] -= dir;
									Face face = new Face(p0, p1, p2);
									face.Init();
									faces.Add(face);
									Vector3 p3 = p1;
									p3[axis2] -= dir;
									face = new Face(p3, p2, p1);
									face.Init();
									faces.Add(face);
								}
							}
						}
					}
				}
				currentProgress += blob.height * blob.depth;
				loadProgress = currentProgress / maxProgress;
			}
			multicolorFaces.Add(faces);
		}
		
		m_loaded = true;
	}
	
	#endregion
	
	#region Inventory Cache
	/// <summary>
	/// Dumps the face data to a binary file.
	/// </summary>
	public void WriteToFile(string filePath) {
		Scheduler.StartCoroutine(DoWrite(filePath));
	}
	
	IEnumerator DoWrite(string filePath) {
		FileStream stream = new FileStream(filePath, FileMode.Create);
		//save faces in chunks
		List<Face> currFaces = new List<Face>();
		foreach (Face face in faces) {
			if (Scheduler.ShouldYield()) yield return null;
			currFaces.Add(face);
			if (currFaces.Count == 100) {
				SerializationHelpers.SerializeToStream(stream, currFaces);
				currFaces.Clear();
			}
		}
		
		if (currFaces.Count > 0) {
			SerializationHelpers.SerializeToStream(stream, currFaces);
		}
		
		stream.Close();
	}
	#endregion
	
	#region Mesh/Face/GameObject interaction	
	public void ChangeScale(Vector3 aScale) {
		foreach(Face f in Faces) {
			for(int v = 0; v < 3; v++) {
				f.vertices[v].x *= aScale.x;
				f.vertices[v].y *= aScale.y;
				f.vertices[v].z *= aScale.z;
			}
			f.Init();
		}
		
		MoveFacesToOrigin();
	}
	
	public void ChangeRotation(Quaternion aRotation) {
		foreach(Face f in Faces) {
			for(int v = 0; v < 3; v++) {
				f.vertices[v] = aRotation * f.vertices[v];
			}
			f.Init();
		}
		MoveFacesToOrigin();
	}
	
	public IEnumerator CreateMeshObject(GameObject targetObj, Material mat, int layer, bool collide) {
		return CreateMeshObject(targetObj, faces, mat, layer, collide);
	}
	
	const int kFacesPerMesh = 2000;
	
	public static IEnumerator CreateMeshObject(GameObject targetObj, List<Face> faces, Material mat, int layer, bool collide) {
		if (targetObj == null) yield break;

		int meshCount = Mathf.CeilToInt((float)faces.Count / (float)kFacesPerMesh);
		int currentIndex = 0;
		
		for (int msh = 0; msh < meshCount; msh++) {
			if (Scheduler.ShouldYield()) yield return null;
			if (targetObj == null) yield break;
			
			Mesh mesh = new Mesh();
			List<Vector3> verts = new List<Vector3>();
			List<int> tris = new List<int>();
			
			for (int fac = 0; fac < kFacesPerMesh; fac++) {
				if (currentIndex >= faces.Count) continue;
				verts.Add(faces[currentIndex].vertices[0] );
				verts.Add(faces[currentIndex].vertices[1] );
				verts.Add(faces[currentIndex].vertices[2] );
				
				tris.Add(tris.Count);
				tris.Add(tris.Count);
				tris.Add(tris.Count);
				
				currentIndex++;
			}
			
			mesh.vertices = verts.ToArray();
			mesh.triangles = tris.ToArray();
			mesh.RecalculateNormals();

			GameObject childObj = new GameObject(msh.ToString());
			childObj.AddComponent<MeshFilter>().sharedMesh = mesh;
			childObj.AddComponent<MeshRenderer>();
			childObj.renderer.material = mat;
			childObj.transform.parent = targetObj.transform;
			childObj.transform.localPosition = Vector3.zero;
			childObj.transform.localScale = Vector3.one;
			childObj.transform.localRotation = Quaternion.identity;
			childObj.layer = layer;
			
			if (collide) {
				childObj.AddComponent<MeshCollider>().sharedMesh = mesh;
			}

			if (currentIndex >= faces.Count) break;
		}
	}
	
	public IPatternConverter CreateConverter(MeshManager manager, Vector3 position, Quaternion rotation, 
		Vector3 scale, byte blockMat, Material meshMat) 
	{
		GameObject go = new GameObject("MeshPatternConverter");
		go.transform.position = position;
		go.transform.rotation = rotation;
		go.transform.localScale = scale;

		Scheduler.StartCoroutine(CreateMeshObject(
			go, faces, meshMat, LayerMask.NameToLayer("Voxel"), (blockMat != MeshManager.kVoxelSubtract)));
		MeshPatternConverter pc = go.AddComponent<MeshPatternConverter>();
		pc.Init(this, manager, position, rotation, scale, blockMat, meshMat);

		return pc;
	}
	
	public float GetMinScaleIncrement(InterfaceController controller) {
		float shortestDim = Mathf.Min(m_size.x, m_size.y, m_size.z);
		return VoxelBlob.kVoxelSizeInMm * 2f * 
			controller.blockScaleIncrement / shortestDim;
	}
		
	public float GetMinScale(InterfaceController controller) {
		return GetMinScaleIncrement(controller);
	}
	
	#endregion
	
	
	#region STL Reading
	private IEnumerator ReadStl(string fileName) {
		StreamReader sr = new StreamReader (fileName);
		char[] buffer = new char[80];
		sr.Read(buffer, 0, 80);
		string fileContents = new string(buffer);
		sr.Close();
		string[] lines = fileContents.Split("\n"[0]);
		if (lines[0].Contains("Solid") || lines[0].Contains("SOLID") || lines[0].Contains("solid")) {
			//certain programs (*cough* solidworks) use a nonstandard header where they put "solid" at the top of a binary file
			//check for that
			bool asciifound = false;
			sr = new StreamReader(fileName);
			for (int i = 0; i < 100 && !sr.EndOfStream; ++i) {
				string line = sr.ReadLine().Trim();
				if (line.Length == 0) continue;
				string[] facetTokens = line.Split(' ');
				if (facetTokens[0].ToLower() == "facet") {
					asciifound = true;
					break;
				}
			}
			sr.Close();
			
			if (asciifound) {
				sr = new StreamReader(fileName);
				fileContents = sr.ReadToEnd();
				sr.Close();
				yield return Scheduler.StartCoroutine(ReadStlAscii(fileContents));
			}
			else
				yield return Scheduler.StartCoroutine(ReadStlBinary(fileName));
		} 
		else {
			yield return Scheduler.StartCoroutine(ReadStlBinary(fileName));
		}
	}
	
	private IEnumerator ReadStlAscii(string fileContents) {
		faces = new List<Face>();
		string[] facetTokens;
		string[] lines = fileContents.Split('\n');
		Vector3[] tempVerts = new Vector3[3];
		
		for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++) {
			if (Scheduler.ShouldYield()) yield return null;
			string trimmedString = lines[lineNumber].Trim();
			if (trimmedString.Length == 0) continue;
			facetTokens = trimmedString.Split(' ');
			
			if (facetTokens[0].ToLower() == "facet") {
				for (int anAxis = 0; anAxis < 3; anAxis++) {
					string vertsString = lines[lineNumber + 2 + anAxis].Trim();
					string[] vertTokens = vertsString.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
					
					//switch y and z to go into unity's coordinate system
					tempVerts[anAxis] = ParseVector3(vertTokens[1], vertTokens[3], vertTokens[2]);
				}
				
				//since we switched y and z we also have to switch the face order
				Face f = new Face(tempVerts[2] / VoxelBlob.kVoxelSizeInMm, 
					tempVerts[1] / VoxelBlob.kVoxelSizeInMm, 
					tempVerts[0] / VoxelBlob.kVoxelSizeInMm);
				faces.Add(f);
			}
		}

		MoveFacesToOrigin();
		m_loaded = true;
	}
	
	void MoveFacesToOrigin() {
		Vector3 lowerBound = Face.FindLowerBound(faces);
		Vector3 upperBound = Face.FindUpperBound(faces);
		m_size = upperBound - lowerBound;
		Vector3 mid = (upperBound - lowerBound) * 0.5f + lowerBound;
		
		foreach (Face f in faces) {
			for (int i = 0; i < 3; ++i) {
				f.vertices[i] -= mid;
			}
			f.Init();
		}
	}
		
	Vector3 ParseVector3(string xToken, string yToken, string zToken) {
		Vector3 result = Vector3.zero;
		
		bool success = true;
		success &= float.TryParse(xToken, out result.x);
		success &= float.TryParse(yToken, out result.y);
		success &= float.TryParse(zToken, out result.z);
		
		if (!success) {
			Text.Error(string.Format("Failed parsing <{0}, {1}, {2}>", xToken, yToken, zToken));
		}
		
		return result;
	}
	
	private void SwapYZ(ref Vector3 v) {
		float temp = v.y;
		v.y = v.z;
		v.z = temp;
	}
	
	private IEnumerator ReadStlBinary(string fileName) {
		FileStream testStream = File.Open(fileName, FileMode.Open, FileAccess.Read);
		BinaryReader br = new BinaryReader(testStream);
		byte[] m_buffer = new byte[80];
		br.Read(m_buffer, 0, 80);
		
		int numberOfFaces = (int)br.ReadUInt32();
		faces = new List<Face>();
		for (int i = 0; i < numberOfFaces; i++) {
			if (Scheduler.ShouldYield()) yield return null;
			//Vector3 normal = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
			/// Get the normal out of the way...
			br.ReadSingle();
			br.ReadSingle();
			br.ReadSingle();
			Vector3 v1 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
			Vector3 v2 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
			Vector3 v3 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
			//switch y and z to go into unity's coordinate system
			SwapYZ(ref v1);
			SwapYZ(ref v2);
			SwapYZ(ref v3);
			
			//since we switched y and z we also have to switch the face order
			Face f = new Face(v3/ VoxelBlob.kVoxelSizeInMm, v2/ VoxelBlob.kVoxelSizeInMm, v1/ VoxelBlob.kVoxelSizeInMm);
			faces.Add(f);
			
			br.ReadByte();
			br.ReadByte();
		}
		
		br.Close();
		
		MoveFacesToOrigin(); 
		m_loaded = true;
	}
	#endregion STL Reading
	
	#region STL Writing
	private IEnumerator WriteStlBinary(string fileName) {
		yield return Scheduler.StartCoroutine(WriteStlBinaryToStream(
			System.IO.File.Open(fileName, System.IO.FileMode.Create),
			true));
	}
	
	private IEnumerator WriteStlBinaryToStream(System.IO.Stream aStream, bool shouldClose) {
		while (!m_loaded) yield return null;
		
		System.IO.BinaryWriter bw = new System.IO.BinaryWriter(aStream);
		
		byte[] m_buffer = new byte[80];
		bw.Write(m_buffer) ;
		
		//int numberOfFaces = (int)br.ReadUInt32();
		bw.Write((Int32)faces.Count);
		for (int i = 0; i < faces.Count; i++) {
			if (Scheduler.ShouldYield()) yield return null;
			Dispatcher<float>.Broadcast(kOnExportUpdate, (float)i / faces.Count);
			
			//swap Y and Z to go back to sane coordinates, and write the face vertices in reverse
			
			///Write face normal
			bw.Write(faces[i].normal.x);
			bw.Write(faces[i].normal.z);
			bw.Write(faces[i].normal.y);
			///Write face vertices
			for (int j = 2; j >= 0; --j) {
				bw.Write(faces[i].vertices[j].x * VoxelBlob.kVoxelSizeInMm);
				bw.Write(faces[i].vertices[j].z * VoxelBlob.kVoxelSizeInMm);
				bw.Write(faces[i].vertices[j].y * VoxelBlob.kVoxelSizeInMm);
			}
			
			bw.Write((byte)0);
			bw.Write((byte)0);
		}
		
		if (shouldClose) bw.Close();
		
		Dispatcher<float>.Broadcast(kOnExportUpdate, 1f);
	}
	#endregion STL Writing
	
	#region VMRL Writing
	private IEnumerator WriteVmrlToStream(System.IO.Stream aStream, bool shouldClose) {
		while(!m_loaded) yield return null;
		StreamWriter sw = new StreamWriter(aStream);
		//StringBuilder sb = new StringBuilder();
		//System.IO.StringWriter sw = new System.IO.StringWriter(aStream);
		sw.WriteLine("#VRML V1.0 ascii");
		sw.WriteLine();
		sw.WriteLine("Separator {");
		
		
		
		sw.WriteLine("}");
		if (shouldClose) sw.Close();
		Dispatcher<float>.Broadcast(kOnExportUpdate, 1f);
	}
	
	
	#endregion VMRL Writing
	
	/*
	public void OnDrawGizmos() {
		Gizmos.color = Color.green;
		int rayIndex = Mathf.RoundToInt(Time.time / 0.3333f) % 3;
		//if (rayIndex == 0) Gizmos.color = Color.green;
		//else if (rayIndex == 1) Gizmos.color = Color.red;
		//else Gizmos.color = Color.yellow;
		foreach(Vector4 v in viewableIntersections) {
			//if (v.w != rayIndex) continue;
			Vector3 offset = Vector3.one * 25f * v.w;
			//if (v.w < 0) Gizmos.color = Color.green;
			//else Gizmos.color = Color.red;
			
			Gizmos.DrawSphere(new Vector3(v.x,v.y,v.z) + offset, 0.5f);
		}
		Gizmos.color = Color.green;
		
		for (int i = 0; i < startingPoints.Count; i++) {
			Gizmos.color = Color.magenta;
			//Vector3 dir = Vector3.zero;
			//dir[rayCastingAxes[i]] = 1f;
			//Gizmos.DrawRay(new Ray(startingPoints[i], dir));
			Gizmos.DrawSphere(startingPoints[i],0.25f);
			
		}
		Gizmos.color = Color.green;
		foreach(Face f in tempFaces) {
			foreach(Vector3 v in f.vertices) {
				//Text.Log("Drawing a sphere");
				Gizmos.DrawSphere(v, 0.1f);
			}
		}
		
		for (int i = 0; i < voxelChunkLocations.Count; i++) {
			//Gizmos.DrawWireCube(voxelChunkLocations[i], voxelChunkSizes[i]);
		}
	}
	*/
}
