using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// The object data; at 402x402x402, this is about 64MB.
/// If we go larger, we'll need to refactor this to load
/// data from disk.
/// </summary>
public class VoxelBlob {
	/// <summary>
	/// The magic cookie that identifies the file format.
	/// </summary>
	const uint kMagicCookie = 0xFAB0B10B;

	/// <summary>
	/// The quick serialize key for one voxel blob.
	/// </summary>
	public const string kSerializedKey = "VoxelBlob";

	/// <summary>
	/// The voxel size, in mm.
	/// </summary>
	public const float kVoxelSizeInMm = 0.5f;

	/// <summary>
	/// The material data.
	/// </summary>
	//byte[,,] m_data;
	byte[] m_data;

	byte Index(int x, int y, int z) {
		return m_data[x * depth * height + y * depth + z];
	}
	void Index(int x, int y, int z, byte value) {
		m_data[x * depth * height + y * depth + z] = value;
	}

	/// <summary>
	/// Gets the width in voxels.
	/// </summary>
	/// <value>
	/// The width.
	/// </value>
	public int width { get { return m_width; } }// return m_data.GetLength(0); } }
	int m_width;

	/// <summary>
	/// Gets the depth in voxels.
	/// </summary>
	/// <value>
	/// The depth.
	/// </value>
	public int depth { get { return m_depth; } }//return m_data.GetLength(2); } }
	int m_depth;

	/// <summary>
	/// Gets the height in voxels.
	/// </summary>
	/// <value>
	/// The height.
	/// </value>
	public int height { get { return m_height; } }//return m_data.GetLength(1); } }
	int m_height;

	public int3 size { get { return new int3(width, height, depth); } }

	/// <summary>
	/// The position of the voxel blob.  This is being added to simplify chunk by chunk loading of STL and other Mesh files.
	/// </summary>
	public Vector3 position;

	/// <summary>
	/// The version of the blob file.
	/// </summary>
	public int version = 1;

	public int3 minVoxelBounds = new int3(-1, -1, -1);
	public int3 maxVoxelBounds = new int3(-1, -1, -1);
	bool m_trackVoxelBounds = false;
	int[][] m_voxelCounts;

	#region Testing
	/// <summary>
	/// Default size for testing.
	/// </summary>
	/// <description>
	/// Our platform has 75mm radius; thus, with 0.5mm blocks,
	/// we need (75mm / 0.5mm * 2) = 300 blocks to cover it.
	/// </description>
	public const int kTestSize = (int)((75 / kVoxelSizeInMm) * 2) + 1;// 301;


	const int kTestBlockSize = 150;//150;

	/// <summary>
	/// Returns a test blob of the default test size.
	/// </summary>
	/// <returns>
	/// The test BLOB.
	/// </returns>
	public static VoxelBlob NewTestBlob() {
		VoxelBlob blob = new VoxelBlob(kTestSize, kTestSize, kTestSize, true);

		// A 20x20 square centered at the platform origin
		// for kTestSize = 100.
		int start = (kTestSize - kTestBlockSize) / 2; // -> 145.5
		int spaceStart = (kTestBlockSize / 2) - 1 + start;
		int spaceEnd = spaceStart + 0;

		/*for (int aLayer = 0; aLayer < blob.height; ++aLayer) {
			for (int aRow = 0; aRow < blob.width; ++aRow) {
				for (int aCol = 0; aCol < blob.depth; ++aCol) {
					 blob[aRow, aLayer, aCol] = 1;
				}
			}
		}*/

		for (int aLayer = 2; aLayer < 4; ++aLayer) {
			for (int aRow = start; aRow < start + kTestBlockSize; ++aRow) {
			//for (int aRow = (kTestSize - 15) / 2; aRow < (kTestSize - 15) / 2 + 30; ++aRow) {
				for (int aCol = start; aCol < start + kTestBlockSize; ++aCol) {
					// Uncomment for a split square.
					if (aCol < spaceStart || aCol > spaceEnd || aRow % 2 == 0) {
						blob[aCol, aLayer, aRow] = 1;
					}
				}
			}
		}

		return blob;
	}

	public static VoxelBlob NewExtrusionBlob() {
		VoxelBlob blob = new VoxelBlob(kTestSize, kTestSize, kTestSize, true);

		int origin = kTestSize / 2;

		for (int aLayer = 0; aLayer < 20; ++aLayer) {
			for (float r = 45 / kVoxelSizeInMm; r < 70 / kVoxelSizeInMm; ++r) {
				for (float angle = -45; angle <= 45.0f; angle += 0.25f) {
					float radians = angle * Mathf.Deg2Rad;
					float x = Mathf.Cos(radians);
					float y = Mathf.Sin(radians);

					PlaceRadialBlock(blob,
						origin + r * x,
						aLayer,
						origin + r * y);

					PlaceRadialBlock(blob,
						Mathf.Floor(origin + r * Mathf.Cos(75.0f * Mathf.Deg2Rad)),
						aLayer,
						Mathf.Floor(origin + r * Mathf.Sin(75.0f * Mathf.Deg2Rad)));

					PlaceRadialBlock(blob,
						Mathf.Floor(origin + r * Mathf.Cos(-75.0f * Mathf.Deg2Rad)),
						aLayer,
						Mathf.Floor(origin + r * Mathf.Sin(-75.0f * Mathf.Deg2Rad)));
				}
			}
		}

		return blob;
	}

	public static VoxelBlob NewTestDisc() {
		VoxelBlob blob = new VoxelBlob(kTestSize, kTestSize, kTestSize, false);

		int origin = kTestSize / 2;

		// Disc with ~25mm radius.
		for (int aLayer = 0; aLayer < 30; ++aLayer) {
			for (float r = 0; r < 24.85f / kVoxelSizeInMm; ++r) {
				for (float angle = 0.0f; angle < 90.0f; angle += 0.25f) {
					float radians = angle * Mathf.Deg2Rad;
					float x = Mathf.Cos(radians);
					float y = Mathf.Sin(radians);

					PlaceRadialBlock(blob,
						origin + r * x,
						aLayer,
						origin + r * y);
				}
			}
		}

		return blob;
	}

	static void PlaceRadialBlock(VoxelBlob aBlob, float rawX, int aLayer, float rawZ) {
		int lowX = Mathf.FloorToInt(rawX);
		int lowZ = Mathf.FloorToInt(rawZ);
		aBlob[lowX, aLayer, lowZ] = 1;

		int highX = Mathf.CeilToInt(rawX);
		int highZ = Mathf.CeilToInt(rawZ);
		aBlob[highX, aLayer, highZ] = 1;
	}

	/// <summary>
	/// Makes a new blob that should strain the chunk regeneration
	/// </summary>
	public static VoxelBlob NewFpsTestBlob() {
		VoxelBlob blob = new VoxelBlob(kTestSize, kTestSize, kTestSize, true);

		int on = 0;
		for (int x = 0; x < 300; ++x) {
			if ((x % 2) == 0) on = (on + 2) % 4;
			for (int y = 0; y < 128; ++y) {
				if ((y % 2) == 0) on = (on + 2) % 4;
				for (int z = 0; z < 300; ++z) {
					if (on > 1 && !(x == 17 && z == 17)) {
						blob[x, y, z] = MeshManager.kVoxelFirstMat;
					}
					on = (on + 1) % 4;
				}
			}
		}
		return blob;
	}
	#endregion

	/// <summary>
	/// Initializes a new instance of the <see cref="VoxelBlob"/> class.
	/// </summary>
	/// <param name='height'>
	/// The height of the blob, in voxels.
	/// </param>
	/// <param name='depth'>
	/// The depth of the blob, in voxels.
	/// </param>
	/// <param name='width'>
	/// The width of the blob, in voxels.
	/// </param>
	public VoxelBlob(int width, int height, int depth, bool trackVoxelBounds) {
		m_data = new byte[width * height * depth];
		m_width  = width;
		m_height = height;
		m_depth  = depth;

		for (int i = 0; i < width * height * depth; i++) {
			try {
				m_data[i] = 0;
			}
			catch (System.Exception e) {
				Debug.Log(string.Format("Failed at index {0}: {1}", i, e.Message));
			}
		}

		m_trackVoxelBounds = trackVoxelBounds;
		if (m_trackVoxelBounds) {
			m_voxelCounts = new int[3][];
			m_voxelCounts[0] = new int[width];
			m_voxelCounts[1] = new int[height];
			m_voxelCounts[2] = new int[depth];
		}
	}

	/// <summary>
	/// Returns a voxel blob with the object moved closer
	/// to the origin.
	/// </summary>
	/// <returns>
	/// Returns a compacted voxel blob.
	/// </returns>
	public VoxelBlob CompactBlob() {
		VoxelBlob compacted = new VoxelBlob(width, depth, height, true);

		int xMin = minVoxelBounds[0];
		int yMin = minVoxelBounds[1];
		int zMin = minVoxelBounds[2];

		if (xMin < 0 || yMin < 0 || zMin < 0) {
			return this;
		}

		if (!m_trackVoxelBounds) {
			xMin = width;
			yMin = depth;
			zMin = height;

			for (int x = 0; x < width; ++x) {
				for (int y = 0; y < depth; ++y) {
					for (int z = 0; z < height; ++z) {
						int block = this[x, y, z];

						if (block > 0) {
							xMin = Mathf.Min(x, xMin);
							yMin = Mathf.Min(y, yMin);
							zMin = Mathf.Min(z, zMin);
						}
					} // end z for
				} // end y for
			} // end x for
		}

		for (int x = xMin; x < width; ++x) {
			for (int y = yMin; y < depth; ++y) {
				for (int z = zMin; z < height; ++z) {
					compacted[x - xMin, y - yMin, z - zMin] = this[x, y, z];
				}
			}
		}
		compacted.position = new Vector3(xMin, yMin, zMin);
		return compacted;
	}

	/// <summary>
	/// Returns a region enumerator for the given voxel layer.
	/// </summary>
	/// <returns>The enumerator.</returns>
	/// <param name="aLayer">The layer to extract.</param>
	/// <param name="isReverse">True for a reverse iterator; false otherwise.</param>
	public VoxelRegion.VoxelRegionEnumerator RegionEnumerator(int aLayer, bool isReverse) {
		return new VoxelRegion.VoxelRegionEnumerator(this, aLayer, isReverse);
	}

	public IEnumerator GenerateSupport(byte support, VoxelBlob source) {
		System.Array.Copy(source.m_data, m_data, source.m_data.Length);

		for (int x = 0; x < width; ++x) {
			int minX = Mathf.Max(0, x - 1);
			int maxX = Mathf.Min(width - 1, x + 1);

			for (int z = 0; z < depth; ++z) {
				int minZ = Mathf.Max(0, z - 1);
				int maxZ = Mathf.Min(depth - 1, z + 1);

				bool isBlockPresent = this[x, height - 1, z] != 0;
				for (int y = height - 2; y >= 0; --y) {
					if (!isBlockPresent) {
						isBlockPresent = this[x, y, z] != 0;
						continue;
					}

					// First pass: Generate support under each block.
					if (   this[minX, y, minZ] == 0
						&& this[minX, y, minZ] == 0
						&& this[minX, y,    z] == 0
						&& this[minX, y, maxZ] == 0
						&& this[x,    y, minZ] == 0
						&& this[x,    y,    z] == 0
						&& this[x,    y, maxZ] == 0
						&& this[maxX, y, minZ] == 0
						&& this[maxX, y,    z] == 0
						&& this[maxX, y, maxZ] == 0)
					{
						this[x, y, z] = support;
					}
				}
				if (Scheduler.ShouldYield()) yield return null;
			}
		}
	}

	/// <summary>
	/// Region voxels with non-zero material are removed from the blob.
	/// </summary>
	/// <param name="r">The voxel region to subtract.</param>
	/// <param name="layer">The layer height of the region.</param>
	public void SubtractRegion(VoxelRegion r, int layer) {
		for (int col = 0; col < width; col++) {
			for (int row = 0; row < depth; row++) {
				if (r[col, row] != 0) {
					this[col, layer, row] = 0;
				}
			}
		}
	}

	/// <summary>
	/// Returns the requested layer.
	/// </summary>
	/// <returns>
	/// The layer.
	/// </returns>
	/// <param name='aLayer'>
	/// A layer.
	/// </param>
	public void GetLayer(int aLayer, ref byte[,] outBuffer, out bool layerIsEmpty,
	                     out int2 minVals, out int2 maxVals) 
	{
		minVals = new int2(int.MaxValue, int.MaxValue);
		maxVals = new int2(int.MinValue, int.MinValue);

		if (outBuffer == null) {
			outBuffer = new byte[width, depth];
		}

		int greatestMaterialFound = 0;
		// NOTE: This could be improved.
		// Option 1: Use a one-dimensional array for m_data
		//			 and then copy everything to the new array
		//			 using Array.Copy.
		// Option 2: Return a multi-dimensional array–––but this
		//			 would lead to changing the original model!
		for (int aRow = 0; aRow < depth; ++aRow) {
			for (int aCol = 0; aCol < width; ++aCol) {
				outBuffer[aCol, aRow] = Index(aCol, aLayer, aRow);
				greatestMaterialFound = Mathf.Max(greatestMaterialFound, outBuffer[aCol, aRow]);

				if (Index(aCol, aLayer, aRow) != 0) {
					minVals.x = Mathf.Min(minVals.x, aCol);
					minVals.y = Mathf.Min(minVals.y, aRow);
					maxVals.x = Mathf.Max(maxVals.x, aCol);
					maxVals.y = Mathf.Max(maxVals.y, aRow);
				}
			}
		}

		layerIsEmpty = greatestMaterialFound == 0;
	}

	/// <summary>
	/// Gets or sets the <see cref="VoxelBlob"/> material at
	/// the specified layer, depth, and width.
	/// </summary>
	/// <param name='layer'>
	/// Layer.
	/// </param>
	/// <param name='depth'>
	/// Depth.
	/// </param>
	/// <param name='width'>
	/// Width.
	/// </param>
	public byte this[int width, int layer, int depth] {
		get { return Index(width, layer, depth); } //m_data[width, layer, depth];  }
		set {
			byte oldVox = Index(width, layer, depth); //m_data[width, layer, depth];
			Index (width, layer, depth, value);
			//m_data[width, layer, depth] = value;
			if (m_trackVoxelBounds) {
				int3 index = new int3(width, layer, depth);
				if (oldVox == 0 && value > 0) {
					m_voxelCounts[0][width]++;
					m_voxelCounts[1][layer]++;
					m_voxelCounts[2][depth]++;

					if (minVoxelBounds.x == -1) {
						minVoxelBounds = index;
						maxVoxelBounds = index;
					}
					else {
						minVoxelBounds.x = Mathf.Min(minVoxelBounds.x, width);
						minVoxelBounds.y = Mathf.Min(minVoxelBounds.y, layer);
						minVoxelBounds.z = Mathf.Min(minVoxelBounds.z, depth);
						maxVoxelBounds.x = Mathf.Max(maxVoxelBounds.x, width);
						maxVoxelBounds.y = Mathf.Max(maxVoxelBounds.y, layer);
						maxVoxelBounds.z = Mathf.Max(maxVoxelBounds.z, depth);
					}
				}
				else if (oldVox > 0 && value == 0) {
					m_voxelCounts[0][width]--;
					m_voxelCounts[1][layer]--;
					m_voxelCounts[2][depth]--;

					for (int i = 0; i < 3; ++i) {
						while (maxVoxelBounds[i] >= 0 && m_voxelCounts[i][maxVoxelBounds[i]] == 0) {
							maxVoxelBounds[i]--;
						}

						if (maxVoxelBounds[i] == -1) {
							minVoxelBounds = new int3(-1, -1, -1);
							maxVoxelBounds = new int3(-1, -1, -1);
							break;
						}

						while (m_voxelCounts[i][minVoxelBounds[i]] == 0) {
							minVoxelBounds[i]++;
						}
					}
				}
			}
		}
	}

	public byte this[int3 index] {
		get { 
			return Index (index.x, index.y, index.z);
			//return m_data[index.x, index.y, index.z]; 
		}
		set { this[index.x, index.y, index.z] = value; }
	}

	public bool IsValidPoint(int width, int layer, int depth) {
			return (width >= 0 && width < this.width &&
				layer >= 0 && layer < this.height &&
				depth >= 0 && depth < this.depth);
	}

	public bool IsValidPoint(int3 index) {
		return IsValidPoint(index.x, index.y, index.z);
	}

	public byte VoxelAt(int x, int y, int z) {
		if (!IsValidPoint(x, y, z))
			return 0;

		return Index(x, y, z);//m_data[x, y, z];
	}
	
	#region Shared Serialization
	/// <summary>
	/// The maximum amount of time to spend processing files
	/// per frame.
	/// </summary>
	const float kMaxTime = 1 / 30.0f;

	/// <summary>
	/// Delegate for completing deserialization.
	/// </summary>
	public delegate void DeserilizationCompleted(VoxelBlob aNewBlob);
	#endregion

	#region Binary serialization
	/// <summary>
	/// Returns a byte representation of the data.
	/// </summary>
	/// <description>
	/// Currently, we're storing materials as bytes already,
	/// so this isn't doing much more than prepending the size
	/// information.
	/// </description>
	/// <returns>
	/// The bytes.
	/// </returns>
	public byte[] ToBytes() {
		int intSize = sizeof(System.Int32);

		int3 minBounds = minVoxelBounds;
		int3 maxBounds = maxVoxelBounds;

		if (!m_trackVoxelBounds) {
			minBounds = int3.zero;
			maxBounds = new int3(width, height, depth) - int3.one;
		}

		int headerSize = 11 * intSize;
		int packSize = headerSize;
		if (minBounds.x > -1) {
			packSize += (maxBounds.x - minBounds.x + 1) * (maxBounds.y - minBounds.y + 1) *
				(maxBounds.z - minBounds.z + 1);
		}

		// NOTE: We don't currently have a header. Will
		// need some basic versioning, checksum, etc. in
		// the future.
		byte[] packed = new byte[packSize];
		System.Array.Copy(System.BitConverter.GetBytes(kMagicCookie), 0, packed, 0          , intSize);
		System.Array.Copy(System.BitConverter.GetBytes(version),      0, packed, intSize    , intSize);
		System.Array.Copy(System.BitConverter.GetBytes(height),       0, packed, intSize * 2, intSize);
		System.Array.Copy(System.BitConverter.GetBytes(depth),        0, packed, intSize * 3, intSize);
		System.Array.Copy(System.BitConverter.GetBytes(width),        0, packed, intSize * 4, intSize);

		System.Array.Copy(System.BitConverter.GetBytes(minBounds.y),  0, packed, intSize * 5, intSize);
		System.Array.Copy(System.BitConverter.GetBytes(minBounds.z),  0, packed, intSize * 6, intSize);
		System.Array.Copy(System.BitConverter.GetBytes(minBounds.x),  0, packed, intSize * 7, intSize);
		System.Array.Copy(System.BitConverter.GetBytes(maxBounds.y),  0, packed, intSize * 8, intSize);
		System.Array.Copy(System.BitConverter.GetBytes(maxBounds.z),  0, packed, intSize * 9, intSize);
		System.Array.Copy(System.BitConverter.GetBytes(maxBounds.x),  0, packed, intSize * 10, intSize);

		int index = headerSize;
		if (minBounds.x > -1) {
			for (int aLayer = minBounds.y; aLayer <= maxBounds.y; ++aLayer) {
				for (int aRow = minBounds.z; aRow <= maxBounds.z; ++aRow) {
					for (int aCol = minBounds.x; aCol <= maxBounds.x; ++aCol) {
						packed[index] = this[aCol, aLayer, aRow];
						index++;
					}
				}
			}
		}
		return packed;
	}

	public void WriteToFile(string path) {
		byte[] packedBlob = ToBytes();

		using (System.IO.BinaryWriter fout = new System.IO.BinaryWriter(System.IO.File.Open(path, System.IO.FileMode.Create))) {
			fout.Write(packedBlob);
		}
	}

	public static bool ReadHeader(BinaryReader fin, out int height, out int width, out int depth,
		out int3 minBounds, out int3 maxBounds) {
		height = kTestSize;
		width = kTestSize;
		depth = kTestSize;
		minBounds = int3.one * -1;
		maxBounds = minBounds;
		uint cookie = fin.ReadUInt32();
		if (cookie != kMagicCookie) {
			// Not a valid file (T_T)
			Text.Error("File doesn't appear to be valid.");
			fin.Close();
			return false;
		}

		int version = fin.ReadInt32();
		// NOTE: We don't currently have a header. Will need
		// some basic versioning and checks at least in the
		// future.
		height = fin.ReadInt32();
		depth  = fin.ReadInt32();
		width  = fin.ReadInt32();

		switch(version) {
		case 0:
			minBounds = int3.zero;
			maxBounds = new int3(width - 1, height - 1, depth - 1);
			break;
		case 1:
			minBounds.y = fin.ReadInt32();
			minBounds.z = fin.ReadInt32();
			minBounds.x = fin.ReadInt32();
			maxBounds.y = fin.ReadInt32();
			maxBounds.z = fin.ReadInt32();
			maxBounds.x = fin.ReadInt32();
			break;
		default:
			Text.Error("Unable to read version " + version);
			fin.Close();
			return false;
		}

		return true;
	}

	public static VoxelBlob NewFromFile(System.IO.FileStream aFile, bool trackVoxelBounds) {
		VoxelBlob result = null;

		using (System.IO.BinaryReader fin = new System.IO.BinaryReader(aFile)) {
			int width, height, depth;
			int3 start, end;
			if (!ReadHeader(fin, out height, out width, out depth, out start, out end)) {
				return new VoxelBlob(kTestSize, kTestSize, kTestSize, trackVoxelBounds);
			}

			result = new VoxelBlob(width, height, depth, trackVoxelBounds);

			for (int aLayer = start.y; aLayer <= end.y; ++aLayer) {
				for (int aRow = start.z; aRow <= end.z; ++aRow) {
					for (int aCol = start.x; aCol <= end.x; ++aCol) {
						result[aCol,  aLayer, aRow] = fin.ReadByte();
					}
				}
			}
			fin.Close();
		}

		return result;
	}

	/// <summary>
	/// Creates a new VoxelBlob from the provided filestream.
	/// </summary>
	/// <returns>
	/// The from file.
	/// </returns>
	/// <param name='aFile'>
	/// A file.
	/// </param>
	/// <param name='aCallback'>
	/// A callback.
	/// </param>
	public static IEnumerator NewFromFile(System.IO.FileStream aFile, bool trackVoxelBounds,
		DeserilizationCompleted aCallback)
	{
		VoxelBlob result;

		using (System.IO.BinaryReader fin = new System.IO.BinaryReader(aFile)) {
			int width, height, depth;
			int3 start, end;
			if (!ReadHeader(fin, out height, out width, out depth, out start, out end)) {
				aCallback(new VoxelBlob(kTestSize, kTestSize, kTestSize, trackVoxelBounds));
			}

			result = new VoxelBlob(width, height, depth, trackVoxelBounds);

			for (int aLayer = start.y; aLayer <= end.y; ++aLayer) {
				for (int aRow = start.z; aRow <= end.z; ++aRow) {
					for (int aCol = start.x; aCol <= end.x; ++aCol) {
						result[aCol, aLayer, aRow] = fin.ReadByte();

						if (Scheduler.ShouldYield()) {
							yield return null;
						}
					}
				}
			}
			fin.Close();
		}

		aCallback(result);
	}
	#endregion

	#region Overrides
	/// <summary>
	/// Returns a printable string summarizing the voxel blob.
	/// </summary>
	/// <returns>
	/// A <see cref="System.String"/> that represents the current <see cref="VoxelBlob"/>.
	/// </returns>
	public override string ToString() {
		return string.Format("[x: {0}, y: {1}, z:{2} blob]", width, height, depth);
	}
	#endregion
}
