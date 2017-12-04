using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Represents an area of connected voxels.
/// </summary>
public class VoxelRegion : System.Object {
	#region Public properties
	/// <summary>
	/// Width of the region; the same as the voxel blob.
	/// </summary>
	/// <value>The width.</value>
	public int width {
		get { return m_width; }
	}

	/// <summary>
	/// Depth of the region; the same as the voxel blob.
	/// </summary>
	/// <value>The depth.</value>
	public int depth {
		get { return m_depth; }
	}

	/// <summary>
	/// Returns the voxel at [x, z].
	/// </summary>
	/// <param name="x">The x coordinate.</param>
	/// <param name="z">The z coordinate.</param>
	public byte this[int x, int z] {
		get {
			return m_bytes[x * m_depth + z];
		}
		set {
			m_bytes[x * m_depth + z] = value;
		}
	}
	#endregion

	#region Variables
	int m_width;
	int m_depth;
	int2 m_minBoundaries;
	int2 m_maxBoundaries;
	byte[] m_bytes;
	#endregion

	#region Initialization & deconstruction
	/// <summary>
	/// Initializes a new instance of the <see cref="VoxelRegion"/> class.
	/// </summary>
	/// <param name="width">Width.</param>
	/// <param name="depth">Depth.</param>
	public VoxelRegion(int width, int depth, int2 minBounds, int2 maxBounds) {
		m_width = width;
		m_depth = depth;
		m_bytes = new byte[m_width * m_depth];
		m_minBoundaries = minBounds;
		m_maxBoundaries = maxBounds;
	}
	#endregion

	#region Converters
	/// <summary>
	/// Returns the voxel distance for the provided millimeters.
	/// </summary>
	/// <returns>
	/// The voxel from mm.
	/// </returns>
	/// <param name='mm'>
	/// Mm.
	/// </param>
	public float GetSampleFromMm(float mm) {
		return mm / VoxelBlob.kVoxelSizeInMm;
	}
	#endregion 

	/// <summary>
	/// Clears the region.
	/// </summary>
	void Clear() {
		System.Array.Clear(m_bytes, 0, m_width * m_depth);
	}

	/// <summary>
	/// Determines whether (width, depth) is in bounds.
	/// </summary>
	/// <returns><c>true</c> if this instance is a valid point; otherwise, <c>false</c>.</returns>
	/// <param name="width">Width in voxels.</param>
	/// <param name="depth">Depth in voxels.</param>
	public bool IsValidPoint(int width, int depth) {
		return width >= 0 && width < m_width
			&& depth >= 0 && depth < m_depth;
	}

	/// <summary>
	/// Voxel region enumerator.
	/// </summary>
	public class VoxelRegionEnumerator : IEnumerator<VoxelRegion>, IEnumerable<VoxelRegion> {
		#region Variables
		public delegate bool MoveNextDelegate();
		public MoveNextDelegate DirectedMoveNext;

		byte[,] m_layer;

		int m_x;
		int m_z;
		int m_width;
		int m_depth;
		int2 m_minVals;
		int2 m_maxVals;
		VoxelRegion m_region;
		#endregion
		
		#region Initialization
		public VoxelRegionEnumerator(VoxelBlob aBlob, int aLayer, bool isReverse) {
			bool isEmpty;

			int2 minBoundary;
			int2 maxBoundary;

			aBlob.GetLayer(aLayer, ref m_layer, out isEmpty, out minBoundary, out maxBoundary);

			m_width = m_layer.GetLength(0);
			m_depth = m_layer.GetLength(1);
			m_region = new VoxelRegion(m_width, m_depth, minBoundary, maxBoundary);

			m_minVals = minBoundary;
			m_maxVals = maxBoundary;

			if (isReverse) {
				m_x = maxBoundary.x - 1;
				m_z = maxBoundary.y - 1;
				DirectedMoveNext = ReverseMoveNext;
			}
			else {
				m_x = minBoundary.x;
				m_z = minBoundary.y;
				DirectedMoveNext = ForwardMoveNext;
			}

			// Positioned before the first region when created;
			// <http://msdn.microsoft.com/en-us/library/system.collections.ienumerator.movenext(v=vs.110).aspx>
		}
		#endregion

		#region IEnumerable(T) hack
		public IEnumerator<VoxelRegion> GetEnumerator() {
			return this;
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return this;
		}
		#endregion
		
		#region IEnumerator(T) interface
		/// <summary>
		/// Gets the current item.
		/// </summary>
		/// <value>The current item.</value>
		public VoxelRegion Current {
			get {
				return m_region;
			}
		}

		object IEnumerator.Current {
			get {
				return Current;
			}
		}

		/// <summary>
		/// Releases all resource used by the <see cref="VoxelRegion+VoxelRegionEnumerator"/> object.
		/// </summary>
		/// <remarks>Call <see cref="Dispose"/> when you are finished using the <see cref="VoxelRegion+VoxelRegionEnumerator"/>. The
		/// <see cref="Dispose"/> method leaves the <see cref="VoxelRegion+VoxelRegionEnumerator"/> in an unusable state.
		/// After calling <see cref="Dispose"/>, you must release all references to the
		/// <see cref="VoxelRegion+VoxelRegionEnumerator"/> so the garbage collector can reclaim the memory that the
		/// <see cref="VoxelRegion+VoxelRegionEnumerator"/> was occupying.</remarks>
		public void Dispose() {
			// Nop
		}

		public bool MoveNext() {
			return DirectedMoveNext();
		}

		/// <summary>
		/// Moves to the next item if possible.
		/// </summary>
		/// <returns><c>true</c>, if move was successful, <c>false</c> otherwise.</returns>
		bool ForwardMoveNext() {
			for (m_x = m_minVals.x; m_x <= m_maxVals.x; m_x++) {
				for (m_z = m_minVals.y; m_z <= m_maxVals.y; m_z++) {
					if (m_layer[m_x, m_z] != 0) {
						m_region.Clear();
						ExtractVoxels(m_region, m_layer, m_x, m_z);
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Moves to the next item if possible in reverse order.
		/// </summary>
		/// <returns><c>true</c>, if move was successful, <c>false</c> otherwise.</returns>
		bool ReverseMoveNext() {
			// Caching m_x and m_z causes this to fail…?
			for (m_x = m_maxVals.x ; m_x >= m_minVals.x; m_x--) {
				for (m_z = m_maxVals.y ; m_z >= m_minVals.y; m_z--) {
					if (m_layer[m_x, m_z] != 0) {
						//Text.Log("Found region.");

						m_region.Clear();
						ExtractVoxels(m_region, m_layer, m_x, m_z);
						//Text.Log("==> {0}, {1}", m_x, m_z);
						return true;
					}
				}
			}

			return false;
		}
		
		/// <summary>
		/// Not implemented.
		/// </summary>
		public void Reset() {
			throw new System.NotSupportedException();
		}
		#endregion
		
		/// <summary>
		/// Recursively extracts voxels from the layer.
		/// </summary>
		/// <param name="r">The red component.</param>
		/// <param name="layer">Layer.</param>
		/// <param name="x">The x coordinate.</param>
		/// <param name="z">The z coordinate.</param>
		void ExtractVoxels(VoxelRegion r, byte[,] layer, int x, int z) {
			x = Mathf.Clamp(x, 0, m_width - 1);
			z = Mathf.Clamp(z, 0, m_depth - 1);

			HashSet<int2> positionsToCheck = new HashSet<int2>();
			List<int2> positionStorage = new List<int2>();
			positionsToCheck.Add(new int2(x, z));
			positionStorage.Add(new int2(x, z));
			int2 pos;

			while (positionStorage.Count > 0) {
				pos =  positionStorage[0];
				ExtractVoxel(r, layer, pos.x, pos.y);

				for (int xDelta = -1; xDelta < 2; xDelta++) {
					for (int zDelta = -1; zDelta < 2; zDelta++) {
						if (xDelta == 0 && zDelta == 0) continue;
						if (pos.x + xDelta < r.m_minBoundaries.x) continue;
						if (pos.x + xDelta > r.m_maxBoundaries.x) continue;
						if (pos.y + zDelta < r.m_minBoundaries.y) continue;
						if (pos.y + zDelta > r.m_maxBoundaries.y) continue;

						if (layer[pos.x + xDelta, pos.y + zDelta] != 0) {
							if (positionsToCheck.Add(new int2(pos.x + xDelta, pos.y + zDelta))) {
								positionStorage.Add(new int2(pos.x + xDelta, pos.y + zDelta));
							}
						}
					}
				}

				positionStorage.RemoveAt(0);
			}
		}
		
		/// <summary>
		/// Removes the voxel from the layer and set the voxel in the region.
		/// </summary>
		/// <param name="r">The red component.</param>
		/// <param name="layer">Layer.</param>
		/// <param name="x">The x coordinate.</param>
		/// <param name="z">The z coordinate.</param>
		void ExtractVoxel(VoxelRegion r, byte[,] layer, int x, int z) {
			r.m_bytes[x * m_depth + z] = layer[x, z];
			layer[x, z] = 0;
		}
	}

	public VoxelSurfaceEnumerator SurfaceEnumerator(VoxelBlob aBlob, int voxelLayer, int printLayer, float layerHeight, bool isReverse) {
		return new VoxelSurfaceEnumerator(aBlob, this, voxelLayer, printLayer, layerHeight, isReverse);
	}

	public class VoxelSurfaceEnumerator : IEnumerator<VoxelRegion>, IEnumerable<VoxelRegion> {
		#region Variables
		public delegate bool MoveNextDelegate();
		public MoveNextDelegate DirectedMoveNext;

		VoxelRegion m_sourceRegion;
		byte[,] m_currentLayer;
		byte[,] m_checkLayer;
		byte[,] m_otherCheckLayer;

		int m_x;
		int m_z;
		int m_width;
		int m_depth;
		int2 m_minVals;
		int2 m_maxVals;
		VoxelRegion m_region;
		#endregion
		
		#region Initialization
		public VoxelSurfaceEnumerator(VoxelBlob aBlob, VoxelRegion sourceRegion, 
		                              int voxelLayer, int printLayer, float layerHeight, bool isReverse) 
		{
			// We don't want to surface the first layer, we want 100% infill instead.
			if (printLayer == 0) {
				DirectedMoveNext = Empty;
				return;
			}

			float logicalHeight = Mathf.Repeat(printLayer * layerHeight, VoxelBlob.kVoxelSizeInMm);

			if (!(   Mathf.Approximately(logicalHeight, 0) 
			      || Mathf.Approximately(logicalHeight + layerHeight, VoxelBlob.kVoxelSizeInMm)))
			{
				// We're neither at the bottom of a voxel nor
				// at the top, so we don't care about surfacing.
				DirectedMoveNext = Empty;
				return;
			}

			// NOTE: Caching the information means that we're not thread safe!
			if (m_width != aBlob.width || m_depth != aBlob.depth) {
				m_currentLayer    = new byte[aBlob.width, aBlob.depth];
				m_checkLayer      = new byte[aBlob.width, aBlob.depth];
				m_otherCheckLayer = new byte[aBlob.width, aBlob.depth];
			}

			bool isEmpty;
			bool checkedFirstLayer = false;

			// Grab the adjacent layers to check for space.
			if (Mathf.Approximately(logicalHeight, 0.0f) && voxelLayer > 0) {
				// At the bottom of a voxel, so we need to check the lower layer.
				aBlob.GetLayer(voxelLayer - 1, ref m_checkLayer, out isEmpty, out m_minVals, out m_maxVals);
				checkedFirstLayer = true;
			}
			if (Mathf.Approximately(logicalHeight + layerHeight, VoxelBlob.kVoxelSizeInMm)
			    && voxelLayer + 1 < aBlob.height) 
			{
				if (!checkedFirstLayer) {
					aBlob.GetLayer(voxelLayer + 1, ref m_checkLayer, out isEmpty, out m_minVals, out m_maxVals);
				}
				else {
					aBlob.GetLayer(voxelLayer + 1, ref m_otherCheckLayer, out isEmpty, out m_minVals, out m_maxVals);
					for (int x = m_minVals.x; x <= m_maxVals.x; x++) {
						for (int y = m_minVals.y; y <= m_maxVals.y; y++) {
							m_checkLayer[x, y] = (byte)Mathf.Min(m_checkLayer[x, y], m_otherCheckLayer[x, y]);
						}
					}
				}
			}

			// Grab the current layer for later comparison.
			aBlob.GetLayer(voxelLayer, ref m_currentLayer, out isEmpty, out m_minVals, out m_maxVals);
			if (isEmpty) {
				DirectedMoveNext = Empty;
				return;
			}
			
			m_width = m_checkLayer.GetLength(0);
			m_depth = m_checkLayer.GetLength(1);

			m_region = new VoxelRegion(m_width, m_depth, m_minVals, m_maxVals);
			m_sourceRegion = sourceRegion;

			if (isReverse) {
				// This means we're actually within a voxel, and so we 
				// shouldn't do anything.
				m_x = m_maxVals.x - 1;
				m_z = m_maxVals.y - 1;
				DirectedMoveNext = ReverseMoveNext;
			}
			else {
				m_x = m_minVals.x;
				m_z = m_minVals.y;
				DirectedMoveNext = ForwardMoveNext;
			}

			// Positioned before the first region when created;
			// <http://msdn.microsoft.com/en-us/library/system.collections.ienumerator.movenext(v=vs.110).aspx>
		}
		#endregion
		
		#region IEnumerable(T) hack
		public IEnumerator<VoxelRegion> GetEnumerator() {
			return this;
		}
		
		IEnumerator IEnumerable.GetEnumerator() {
			return this;
		}
		#endregion
		
		#region IEnumerator(T) interface
		/// <summary>
		/// Gets the current item.
		/// </summary>
		/// <value>The current item.</value>
		public VoxelRegion Current {
			get {
				return m_region;
			}
		}
		
		object IEnumerator.Current {
			get {
				return Current;
			}
		}
		
		/// <summary>
		/// Releases all resource used by the <see cref="VoxelRegion+VoxelRegionEnumerator"/> object.
		/// </summary>
		/// <remarks>Call <see cref="Dispose"/> when you are finished using the <see cref="VoxelRegion+VoxelRegionEnumerator"/>. The
		/// <see cref="Dispose"/> method leaves the <see cref="VoxelRegion+VoxelRegionEnumerator"/> in an unusable state.
		/// After calling <see cref="Dispose"/>, you must release all references to the
		/// <see cref="VoxelRegion+VoxelRegionEnumerator"/> so the garbage collector can reclaim the memory that the
		/// <see cref="VoxelRegion+VoxelRegionEnumerator"/> was occupying.</remarks>
		public void Dispose() {
			// Nop
		}
		
		public bool MoveNext() {
			return DirectedMoveNext();
		}

		public bool Empty() {
			return false;
		}
		
		/// <summary>
		/// Moves to the next item if possible.
		/// </summary>
		/// <returns><c>true</c>, if move was successful, <c>false</c> otherwise.</returns>
		bool ForwardMoveNext() {
			for (m_x = m_minVals.x; m_x < m_maxVals.x; m_x++) {
				for (m_z = m_minVals.y; m_z < m_maxVals.y; m_z++) {
					if (m_currentLayer[m_x, m_z] != 0 && m_checkLayer[m_x, m_z] == 0) {
						m_region.Clear();
						// Text.Log("Found surface.");
						ExtractVoxels(m_region, m_currentLayer, m_x, m_z);
						return true;
					}
				}
			}
			
			return false;
		}
		
		/// <summary>
		/// Moves to the next item if possible in reverse order.
		/// </summary>
		/// <returns><c>true</c>, if move was successful, <c>false</c> otherwise.</returns>
		bool ReverseMoveNext() {
			// Caching m_x and m_z causes this to fail…?
			for (m_x = m_maxVals.x ; m_x >= m_minVals.x; m_x--) {
				for (m_z = m_maxVals.y ; m_z >= m_minVals.y; m_z--) {
					if (m_currentLayer[m_x, m_z] != 0 && m_checkLayer[m_x, m_z] == 0) {
						m_region.Clear();
						//Text.Log("Found surface.");
						ExtractVoxels(m_region, m_currentLayer, m_x, m_z);
						return true;
					}
				}
			}
			
			return false;
		}
		
		/// <summary>
		/// Not implemented.
		/// </summary>
		public void Reset() {
			throw new System.NotSupportedException();
		}
		#endregion

		static readonly int2[] kDeltaPoints = new int2[] {
			new int2(-1,  0),
			new int2( 0, -1),
			new int2( 1,  0),
			new int2( 0,  1)
		};

		/// <summary>
		/// Identifies voxels that are vertically next to air and
		/// collects them in a region.
		/// </summary>
		/// <param name="r">The voxel region.</param>
		/// <param name="layer">Layer.</param>
		/// <param name="x">The x coordinate.</param>
		/// <param name="z">The z coordinate.</param>
		void ExtractVoxels(VoxelRegion r, byte[,] layer, int x, int z) {
			HashSet<int2> allPoints = new HashSet<int2>();
			Queue<int2> checkQueue  = new Queue<int2>();

			int2 p = new int2(x, z);
			allPoints.Add(p);
			checkQueue.Enqueue(p);

			int voxelCount = 0;
			while (checkQueue.Count > 0) {
				// The point under consideration.
				p = checkQueue.Dequeue();
				voxelCount++;

				// Record the point in the region.
				r.m_bytes[p.x * m_depth + p.y] = m_currentLayer[p.x, p.y];
				m_currentLayer[p.x, p.y] = 0;
				m_sourceRegion[p.x, p.y] = 0;

				for (int checkI = 0; checkI < kDeltaPoints.Length; checkI++) {
					int2 newP = new int2(p.x + kDeltaPoints[checkI].x, p.y + kDeltaPoints[checkI].y);

					if (   newP.x < r.m_minBoundaries.x 
					    || newP.x > r.m_maxBoundaries.x
					    || newP.y < r.m_minBoundaries.y
					    || newP.y > r.m_maxBoundaries.y
					    || m_currentLayer[newP.x, newP.y] == 0
					    || m_checkLayer[  newP.x, newP.y] != 0
					    || allPoints.Contains(newP))
					{
						continue;
					}

					allPoints.Add(newP);
					checkQueue.Enqueue(newP);
				}
			}
		} // ExtractVoxels
	}
}
