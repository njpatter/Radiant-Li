using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Stores a delta as a bunch of blobs (typically chunk-sized).
/// </summary>
public class BlobDelta : IDelta {
	public int blobSize;
	RCECoroutine m_co;
	
	public bool Valid { get { return blobs.Count > 0; } }

	public byte this[int x, int y, int z] {
		set {
			int3 chunk = new int3(x, y, z) / blobSize;
			chunk *= blobSize;
			
			//if we already have that chunk, assign to that, otherwise create a new one
			VoxelBlob blob;
			if (blobs.ContainsKey(chunk))
				blob = blobs[chunk].blob;
			else {
				blob = new VoxelBlob(blobSize, blobSize, blobSize, false);
				blob.position = new Vector3(chunk.x, chunk.y, chunk.z);
				blobs.Add(chunk, new BlobHolder(blob));
			}
			
			blob[x - chunk.x, y - chunk.y, z - chunk.z] = value;
		}
	}
	
	public byte this[int3 point] {
		set {
			this[point.x, point.y, point.z] = value;
		}
	}
	
	public class BlobHolder {
		public VoxelBlob blob;
		public bool unDone;
		public BlobHolder(VoxelBlob blob) {
			this.blob = blob;
			unDone = false;
		}
	}
	
	public Dictionary<int3, BlobHolder> blobs = new Dictionary<int3, BlobHolder>();
	
	public BlobDelta(int blobSizes) {
		this.blobSize = blobSizes;
	}
	
	#region IDelta methods
	public void Apply(MeshManager manager, DeltaDoneDelegate onDone) {
		Apply(manager, onDone, true);
	}
	
	void Apply(MeshManager manager, DeltaDoneDelegate onDone, bool undo) {
		if (m_co != null) m_co.done = true;
		m_co = Scheduler.StartCoroutine(DoApply(manager, onDone, undo));
	}
	
	IEnumerator DoApply(MeshManager manager, DeltaDoneDelegate onDone, bool undo) {
		VoxelBlob targetData = manager.m_blob;
		foreach (BlobHolder holder in blobs.Values) {
			if (Scheduler.ShouldYield()) yield return null;
			VoxelBlob blob = holder.blob;
			if (holder.unDone == undo) continue;
			for (int x = 0; x < blob.width; ++x) {
				for (int y = 0; y < blob.height; ++y) {
					for (int z = 0; z < blob.depth; ++z) {
						byte newMat = blob[x, y, z];
						if (newMat != System.Convert.ToByte(0)) {
							int dataX = x + (int)blob.position.x;
							int dataY = y + (int)blob.position.y;
							int dataZ = z + (int)blob.position.z;
							byte oldMat = targetData[dataX, dataY, dataZ];
							if (oldMat == 0) oldMat = MeshManager.kVoxelSubtract;
							if (newMat == MeshManager.kVoxelSubtract) newMat = 0;
							
							blob[x, y, z] = oldMat;
							targetData[dataX, dataY, dataZ] = newMat;
							manager.MarkChunksForRegenForPoint(dataX, dataY, dataZ);
						}
					}
				}
			}
			holder.unDone = undo;
		}
		
		if (onDone != null)
			onDone();
	}
	
	public void UndoAction(MeshManager manager, DeltaDoneDelegate onDone) {
		Apply(manager, onDone, true);
	}
	
	public void RedoAction(MeshManager manager, DeltaDoneDelegate onDone) {
		Apply(manager, onDone, false);
	}
	
	public bool CanUndo() { return true; }
	public bool CanRedo() { return true; }
	public bool Stop() { return false; }
	
	/// <summary>
	/// Coalesce all of the blobs into one blob.
	/// </summary>
	public VoxelBlob MakeSingleBlob() {
		//find our bounds
		Vector3 lowerBound = Vector3.one * float.MaxValue;
		Vector3 upperBound = Vector3.one * float.MinValue;
		
		foreach (BlobHolder holder in blobs.Values) {
			VoxelBlob blob = holder.blob;
			int3 size = blob.size;
			for (int i = 0; i < 3; ++i) {
				lowerBound[i] = Mathf.Min(lowerBound[i], blob.position[i]);
				upperBound[i] = Mathf.Max(upperBound[i], blob.position[i] + size[i]);
			}
		}
		
		Vector3 blobSize = upperBound - lowerBound;
		VoxelBlob target = new VoxelBlob((int)blobSize[0], (int)blobSize[1], (int)blobSize[2], false);
		
		target.position = lowerBound;
		
		foreach(BlobHolder holder in blobs.Values) {
			VoxelBlob blob = holder.blob;
			Vector3 offset = blob.position - target.position;
			for (int x = 0; x < blob.width; ++x) {
				for (int y = 0; y < blob.height; ++y) {
					for (int z = 0; z < blob.depth; ++z) {
						target[x + (int)offset.x, y + (int)offset.y, z + (int)offset.z] = 
							blob[x, y, z];
					}
				}
			}
		}
		
		return target;
	}
	#endregion
}
