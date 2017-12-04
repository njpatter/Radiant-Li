using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A change in a voxel blob; used to undo and redo actions.
/// </summary>
[System.Serializable]
public class VoxelDelta : System.Object, IDelta {
	/// <summary>
	/// Size in bytes.
	/// </summary>
	const int kIntSize = sizeof(int);
	
	/// <summary>
	/// The target width.
	/// </summary>
	public int  width;
	
	/// <summary>
	/// The target layer.
	/// </summary>
	public int  layer;
	
	/// <summary>
	/// The target depth.
	/// </summary>
	public int  depth;
	
	/// <summary>
	/// The original material before interaction.
	/// </summary>
	public byte material;
	
	public bool Valid { get { return true; } }

	/// <summary>
	/// Initializes a new instance of the <see cref="VoxelDelta"/> class.
	/// </summary>
	/// <param name='operationId'>
	/// Operation identifier.
	/// </param>
	/// <param name='width'>
	/// Toggled X.
	/// </param>
	/// <param name='layer'>
	/// Toggled Y.
	/// </param>
	/// <param name='depth'>
	/// Toggled Z.
	/// </param>
	public VoxelDelta(int width, int layer, int depth, byte material) {
		this.width = width;
		this.layer = layer;
		this.depth = depth;
		this.material = material;
	}
	
	#region IDelta methods

	public void Apply(MeshManager manager) {
		VoxelBlob targetData = manager.m_blob;
		byte newMat = material;
		material = targetData[width, layer, depth];
		targetData[width, layer, depth] = newMat;
		manager.MarkChunksForRegenForPoint(width, layer, depth);
	}
	
	public void UndoAction(MeshManager manager, DeltaDoneDelegate onDone) {
		Apply(manager);
		if (onDone != null) onDone();
	}
	
	public void RedoAction(MeshManager manager, DeltaDoneDelegate onDone) {
		Apply(manager);
		if (onDone != null) onDone();
	}

	public bool CanUndo() { return true; }
	public bool CanRedo() { return true; }
	public bool Stop() { return false; }
	
#endregion
	
	#region Serialization support
	/// <summary>
	/// Returns the byte-representation of the data structure.
	/// </summary>
	/// <returns>
	/// The byte array.
	/// </returns>
	public byte[] GetBytes() {
		byte[] buffer = new byte[sizeInBytes];
		
		System.Array.Copy(System.BitConverter.GetBytes(width),           0, buffer, kIntSize    , kIntSize);
		System.Array.Copy(System.BitConverter.GetBytes(layer),           0, buffer, kIntSize * 2, kIntSize);
		System.Array.Copy(System.BitConverter.GetBytes(depth),           0, buffer, kIntSize * 3, kIntSize);
		buffer[sizeInBytes - 1] = material;
		
		return buffer;
	}
		
	/// <summary>
	/// Returns a new Voxel Delta object from the provided binary reader.
	/// </summary>
	/// <returns>
	/// The new voxel delta.
	/// </returns>
	/// <param name='fin'>
	/// The source binary reader.
	/// </param>
	public static VoxelDelta FromBinaryReader(System.IO.BinaryReader fin) {
		int x           = fin.ReadInt32();
		int y           = fin.ReadInt32();
		int z           = fin.ReadInt32();
		byte material   = fin.ReadByte();
		
		return new VoxelDelta(x, y, z, material);
	}
	#endregion
	
	#region Support methods
	/// <summary>
	/// Gets the data size in bytes.
	/// </summary>
	/// <value>
	/// The size in bytes.
	/// </value>
	public static int sizeInBytes {
		get { return sizeof(int) * 4 + 1; }
	}
	
	#endregion
}
